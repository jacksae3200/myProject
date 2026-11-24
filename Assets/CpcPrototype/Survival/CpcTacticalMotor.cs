using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class CpcTacticalMotor : MonoBehaviour
    {
        public ShieldCompanion Companion;
        public CpcThreatSensor Sensor;
        [Min(.01f)] public float InterceptOffset = .2f;
        [Min(.01f)] public float EvadeMargin = .2f;
        [Min(.05f)] public float ImmediateResponseSeconds = 1f;
        [Min(.02f)] public float ReplanInterval = .12f;
        private NavMeshAgent _agent;
        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[128];
        private readonly Collider[] _overlaps = new Collider[32];
        private float _nextPlan;
        private int _serial = int.MinValue;
        private int _safetySerial;
        private SurvivalZombie _safetyTarget;
        private int _safetyRevision;
        private CpcOrder _safety;
        private Vector3 _pathEnd;
        public CpcAction CurrentAction { get; private set; }
        public CpcDecisionOrigin CurrentOrigin { get; private set; }
        public bool HasAction { get; private set; }
        public string LastRejection { get; private set; }
        public int ExecutionCount { get; private set; }
        public string Description => CurrentAction switch
        {
            CpcAction.Hold => "지시한 위치에서 대기 중",
            CpcAction.Follow => "플레이어를 따라가는 중",
            CpcAction.Intercept => "플레이어를 향한 투사체를 가로막는 중",
            CpcAction.Evade => "위험 범위 밖으로 이동 중",
            CpcAction.Counterattack => "공격 후 빈틈에 반격 중",
            CpcAction.Attack => "지정한 좀비를 공격 중",
            CpcAction.Taunt => "좀비를 도발하는 중",
            CpcAction.ProtectPlayer => "플레이어 곁에서 몰려오는 좀비를 막는 중",
            _ => "방패로 경계 중"
        };
        private void Awake() { _agent = GetComponent<NavMeshAgent>(); _path = new NavMeshPath(); }

        public bool Validate(CpcAction action, SurvivalZombie target, bool player, out string reason)
        {
            reason = null;
            if (_agent == null || !_agent.isOnNavMesh) { reason = "이동할 NavMesh가 없어."; return false; }
            if (action == CpcAction.Hold || action == CpcAction.Follow || action == CpcAction.Retreat) return true;
            if (action == CpcAction.ProtectPlayer)
            {
                if (Companion.CrowdProtection != null && Companion.CrowdProtection.HasPressure) return true;
                reason = "플레이어 주변의 군집 위협은 이미 해소됐어."; return false;
            }
            if (target == null || !Sensor.TryObserve(target, out ObservedZombie observed) || !target.HitPoints.IsAlive)
            { reason = "대상을 더 이상 확인할 수 없어."; return false; }
            ZombieThreat threat = observed.Threat;
            if (action == CpcAction.Intercept && (target.Kind != ZombieKind.Spitter ||
                !player && (threat.Target != Companion.Player.Health || !threat.IsDangerous || threat.Remaining <= 0f)))
            { reason = "가로막을 투사체 공격 예고가 이미 끝났어."; return false; }
            if (action == CpcAction.Counterattack && threat.Stage != ZombieAttackStage.Recovering &&
                !(player && (threat.Stage == ZombieAttackStage.Windup || threat.Stage == ZombieAttackStage.Aiming)))
            { reason = "지금은 반격할 빈틈이 없어."; return false; }
            if (action == CpcAction.Counterattack && threat.Stage == ZombieAttackStage.Recovering && threat.Remaining <= 0f)
            { reason = "반격할 빈틈이 이미 끝났어."; return false; }
            if (action == CpcAction.Evade && (!threat.IsDangerous || threat.Remaining <= 0f))
            { reason = "회피할 공격 예고가 이미 끝났어."; return false; }
            return Plan(action, observed, out _, out reason);
        }

        public bool TryImmediateSafety(out CpcOrder order)
        {
            order = default;
            if (_agent == null || !_agent.isOnNavMesh) return false;
            for (int i = 0; i < Sensor.Count; i++)
            {
                ObservedZombie observed = Sensor.Get(i);
                ZombieThreat threat = observed.Source.Threat;
                if (!threat.IsDangerous || threat.Remaining <= 0f || threat.Remaining > ImmediateResponseSeconds) continue;
                bool incoming = threat.Target == Companion.Player.Health && observed.Source.Kind == ZombieKind.Spitter;
                bool inside = Vector3.Distance(Companion.transform.position, threat.Impact) <= threat.Radius + EvadeMargin;
                bool playerMelee = threat.Target == Companion.Player.Health &&
                    observed.Source.Kind == ZombieKind.Crawler &&
                    Vector3.Distance(Companion.transform.position, Companion.Player.transform.position) < Companion.MeleeRange;
                if (!incoming && !inside && !playerMelee) continue;
                if (_safetyTarget == observed.Source && _safetyRevision == threat.Revision)
                { order = _safety; return true; }
                CpcAction action = incoming ? CpcAction.Intercept : playerMelee ? CpcAction.Taunt : CpcAction.Evade;
                if (!Validate(action, observed.Source, false, out _)) action = CpcAction.Guard;
                _safetyTarget = observed.Source;
                _safetyRevision = threat.Revision;
                _safety = new CpcOrder { Serial = --_safetySerial, Action = action, Target = observed.Source,
                    ThreatRevision = threat.Revision, Origin = CpcDecisionOrigin.Safety, Expires = threat.Deadline + .1f };
                Debug.Log($"[CPC/Safety] {action} {observed.Source.TacticalId} phase={threat.Stage} remaining={threat.Remaining:F2}", this);
                order = _safety;
                return true;
            }
            return false;
        }

        public bool Tick(CpcOrder order)
        {
            if (_serial != order.Serial || CurrentOrigin != order.Origin)
            {
                _serial = order.Serial;
                _nextPlan = 0f;
                Companion.CancelPendingMelee();
                CurrentAction = order.Action;
                CurrentOrigin = order.Origin;
                HasAction = true;
                ExecutionCount++;
                Debug.Log($"[CPC/Execute/{order.Origin}] {order.Action}", this);
            }
            if (order.Action == CpcAction.Follow || order.Action == CpcAction.Retreat) return false;
            if (order.Action == CpcAction.ProtectPlayer)
            {
                if (Companion.CrowdProtection == null || !Companion.CrowdProtection.TryTick())
                { Companion.Tactics.Complete(order.Serial); Clear(); }
                return true;
            }
            if (order.Action == CpcAction.Hold)
            {
                if (Vector3.Distance(transform.position, order.HoldPosition) <= .08f) Companion.StopForPlayerHold();
                else if (Time.time >= _nextPlan)
                {
                    _nextPlan = Time.time + ReplanInterval;
                    if (TryPath(order.HoldPosition, true, out _)) Companion.ApplyTacticalPath(_path, .04f);
                    else Companion.StopForPlayerHold();
                }
                return true;
            }
            if (order.Target == null || !Sensor.TryObserve(order.Target, out ObservedZombie observed))
            { Reject(order, "대상을 더 이상 확인할 수 없어."); return true; }
            if (order.Origin != CpcDecisionOrigin.PlayerCommand && order.Origin != CpcDecisionOrigin.Safety &&
                (order.Action == CpcAction.Intercept || order.Action == CpcAction.Evade || order.Action == CpcAction.Counterattack) &&
                order.Target.Threat.Revision != order.ThreatRevision)
            { Reject(order, "공격 단계가 바뀌어 이전 전술을 취소했어."); return true; }
            if (order.Action == CpcAction.Guard)
            {
                if (order.Origin != CpcDecisionOrigin.Safety && !Validate(order.Action, order.Target,
                    order.Origin == CpcDecisionOrigin.PlayerCommand, out string guardReason))
                { Reject(order, guardReason); return true; }
                Companion.StopForPlayerHold();
                Companion.SetTacticalBlock(true, observed.Threat.Origin);
                if (order.Origin != CpcDecisionOrigin.Safety) return false;
                return true;
            }
            if (!Validate(order.Action, order.Target, order.Origin == CpcDecisionOrigin.PlayerCommand, out string reason))
            { Reject(order, reason); return true; }
            if (Time.time >= _nextPlan)
            {
                _nextPlan = Time.time + ReplanInterval;
                if (!Plan(order.Action, observed, out _, out reason)) { Reject(order, reason); return true; }
                Companion.ApplyTacticalPath(_path, .025f);
            }
            switch (order.Action)
            {
                case CpcAction.Intercept:
                    Companion.SetTacticalBlock(true, observed.Threat.Origin);
                    break;
                case CpcAction.Evade:
                    Companion.SetTacticalBlock(false, observed.Position);
                    if (Vector3.Distance(transform.position, observed.Threat.Impact) > observed.Threat.Radius + EvadeMargin)
                        Companion.Tactics.Complete(order.Serial);
                    break;
                case CpcAction.Counterattack:
                    Companion.SetTacticalBlock(true, observed.Position);
                    if (observed.Threat.Stage == ZombieAttackStage.Recovering && Companion.TryBeginMelee(order.Target, true))
                        Companion.Tactics.Complete(order.Serial);
                    break;
                case CpcAction.Attack:
                    Companion.SetTacticalBlock(false, observed.Position);
                    Companion.TryBeginMelee(order.Target, false);
                    break;
                case CpcAction.Taunt:
                    Companion.SetTacticalBlock(true, observed.Position);
                    if (Companion.TryBeginTaunt(order.Target)) Companion.Tactics.Complete(order.Serial);
                    break;
            }
            return true;
        }

        public void Clear() { HasAction = false; _serial = int.MinValue; }
        public bool TryMoveToProtectionPoint(Vector3 point, float arrivalDistance)
        {
            for (int i = 0; i < Sensor.Count; i++)
            {
                ZombieThreat threat = Sensor.Get(i).Source.Threat;
                if ((threat.Stage == ZombieAttackStage.Exploding || threat.Stage == ZombieAttackStage.Emerging) &&
                    threat.Remaining > 0f && Vector3.Distance(point, threat.Impact) <= threat.Radius + EvadeMargin) return false;
            }
            if (!TryPath(point, false, out _)) return false;
            Companion.ApplyTacticalPath(_path, arrivalDistance);
            return true;
        }
        private void Reject(CpcOrder order, string reason)
        {
            LastRejection = reason;
            Companion.StopForPlayerHold();
            Companion.Tactics.RejectExecution(order, reason);
            Clear();
        }
        private bool Plan(CpcAction action, ObservedZombie observed, out Vector3 destination, out string reason)
        {
            reason = null;
            Vector3 player = Companion.Player.transform.position;
            Vector3 source = observed.CueOnly ? observed.Position : observed.Threat.IsDangerous ? observed.Threat.Origin : observed.Position;
            Vector3 direction = player - source; direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) direction = transform.forward;
            direction.Normalize();
            destination = observed.Position;
            if (action == CpcAction.Intercept) destination = player - direction * InterceptOffset;
            else if (action == CpcAction.Evade)
            {
                Vector3 away = transform.position - observed.Threat.Impact; away.y = 0f;
                if (away.sqrMagnitude < .0001f) away = Vector3.Cross(direction, Vector3.up);
                away.Normalize();
                for (int i = 0; i < 16; i++)
                {
                    Vector3 candidate = observed.Threat.Impact + Quaternion.Euler(0, i * 22.5f, 0) * away *
                        (observed.Threat.Radius + EvadeMargin + _agent.radius);
                    if (TryPath(candidate, false, out reason) && IsSafeDestination(_pathEnd) &&
                        PathLength() / Mathf.Max(.01f, Companion.SprintSpeed) <= observed.Threat.Remaining)
                    { destination = candidate; return true; }
                }
                reason = "위험 범위 밖으로 이어지는 안전한 경로가 없어.";
                return false;
            }
            else
            {
                Vector3 approach = transform.position - observed.Position; approach.y = 0f;
                if (approach.sqrMagnitude < .0001f) approach = -direction;
                destination += approach.normalized * Mathf.Max(_agent.radius * 2f, Companion.MeleeRange * .6f);
            }
            if (!TryPath(destination, false, out reason)) return false;
            if (action == CpcAction.Intercept && observed.Threat.IsDangerous)
            {
                float travel = PathLength() / Mathf.Max(.01f, Companion.SprintSpeed);
                if (travel > observed.Threat.Remaining)
                { reason = "공격 전에 가로막을 위치에 도착할 수 없어."; return false; }
            }
            if (action == CpcAction.Counterattack && observed.Threat.Stage == ZombieAttackStage.Recovering)
            {
                float travel = PathLength() / Mathf.Max(.01f, Companion.SprintSpeed);
                float hitDelay = Mathf.Max(travel, Companion.MeleeReadyIn) + Companion.MeleeMotionTime * Companion.MeleeHitFraction;
                if (hitDelay > observed.Threat.Remaining)
                { reason = "빈틈이 끝나기 전에 근접 공격을 맞힐 수 없어."; return false; }
            }
            return true;
        }
        private bool TryPath(Vector3 point, bool ignoreLeash, out string reason)
        {
            reason = "목적지까지 안전한 경로가 없어.";
            point.y = transform.position.y;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, .3f, _agent.areaMask) ||
                Companion.GroundedNavigation && Mathf.Abs(hit.position.y - transform.position.y) > Companion.GroundHeightTolerance ||
                !_agent.CalculatePath(hit.position, _path) || _path.status != NavMeshPathStatus.PathComplete) return false;
            int count = _path.GetCornersNonAlloc(_corners);
            if (count == 0 || count >= _corners.Length) return false;
            _pathEnd = _corners[count - 1];
            if (!ignoreLeash)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = _corners[i] - Companion.Player.transform.position; offset.y = 0f;
                    float limit = Mathf.Max(Companion.Tactics.MaxPlayerDistance, Vector3.Distance(transform.position, Companion.Player.transform.position));
                    if (offset.sqrMagnitude > (limit + .05f) * (limit + .05f)) return false;
                }
            }
            reason = null;
            return true;
        }
        private float PathLength()
        {
            int count = _path.GetCornersNonAlloc(_corners);
            float length = 0f;
            for (int i = 1; i < count; i++) length += Vector3.Distance(_corners[i - 1], _corners[i]);
            return length;
        }
        private bool IsSafeDestination(Vector3 point)
        {
            for (int i = 0; i < Sensor.Count; i++)
            {
                ZombieThreat threat = Sensor.Get(i).Source.Threat;
                if (threat.IsDangerous && Vector3.Distance(point, threat.Impact) <= threat.Radius + EvadeMargin) return false;
            }
            int count = Physics.OverlapSphereNonAlloc(point + Vector3.up * (_agent.height * .5f), _agent.radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (!_overlaps[i].transform.IsChildOf(transform)) return false;
            return true;
        }
    }
}
