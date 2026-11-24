using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(LocalLlmClient))]
    public sealed class ShieldCompanion : MonoBehaviour
    {
        public bool DirectionalMovement;
        public CpcTactics Tactics;
        [Min(.1f)] public float HealInterval = 5f;
        [Range(0f, 1f)] public float HealFraction = .1f;
        private float _nextHeal;
        public bool GroundedNavigation;
        [Min(.01f)] public float GroundHeightTolerance = .12f;
        private float _groundHeight;
        private NavMeshPath _groundPath;
        public SurvivalPlayer Player;
        public Animator Body;
        public Transform Shield;
        public EquipmentGrip ShieldGrip;
        public SurvivalZombie[] Monsters;
        [Min(.1f)] public float FollowDistance = 2f;
        [Min(.1f), Tooltip("대기 중 다시 따라오기 시작하기까지 필요한 추가 거리입니다.")]
        public float FollowResumeMargin = 1f;
        [Min(.1f)] public float FollowSpeed = 4.6f;
        [Min(.1f)] public float SprintSpeed = 6.5f;
        [Min(.1f)] public float DetectionRange = 12f;
        [Min(.1f)] public float GuardDistance = 1.75f;
        [Min(.1f)] public float TauntRange = 6f;
        [Min(.1f)] public float TauntCooldown = 6f;
        [Min(.1f)] public float TauntDuration = 6f;
        [Min(.1f)] public float TauntMotionTime = .8f;
        [Min(.1f)] public float MeleeCooldown = 5f;
        [Min(.1f)] public float MeleeRange = 2.3f;
        [Min(0)] public float MeleeDamage = 20f;
        [Min(.1f)] public float MeleeMotionTime = .9f;
        [Range(0, 1)] public float MeleeHitFraction = .5f;
        [Range(0, 1)] public float BlockDamageMultiplier = .2f;
        [Range(-1, 1)] public float BlockFacingDot = .2f;
        [Range(.05f, .95f)] public float RetreatHealthFraction = .3f;
        [Min(.1f)] public float RetreatDistance = 3.3f;
        [Min(.1f)] public float RetreatSpeed = .65f;
        [Range(0f, .3f)] public float RetreatRecoveryMargin = .1f;
        private Vector3 _retreatDestination;
        [Min(.1f)] public float RepathInterval = .16f;
        [TextArea] public string RetreatLine = "체력이 부족해! 네 뒤로 물러날게.";
        private CpcTacticalMotor _tacticalMotor;
        private CpcCrowdProtection _crowdProtection;
        private CpcSkills _skills;
        private bool _counterSwing;
        private readonly RaycastHit[] _meleeHits = new RaycastHit[24];
        private readonly Vector3[] _groundCorners = new Vector3[128];
        private Health _health;
        private NavMeshAgent _agent;
        private Transform _root;
        private SurvivalZombie _target, _swingTarget;
        private float _nextTaunt, _nextMelee, _motionEnd, _hitTime, _nextPath;
        private bool _blocking, _retreating;
        private bool _following;
        private string _retreatLine;
        private static readonly int SPEED = Animator.StringToHash("Speed");
        public Health HitPoints => _health;
        public LocalLlmClient Llm { get; private set; }
        public bool IsRetreating => _retreating;
        public bool IsBlocking => _blocking;
        public float MeleeReadyIn => Mathf.Max(0f, _nextMelee - Time.time);
        public CpcCrowdProtection CrowdProtection => _crowdProtection;
        [Min(.01f)] public float MeleeInterceptWidth = .16f;
        public string Dialogue => _retreatLine ?? Llm.LastReply;
        public string CurrentActionDescription => _retreating ? "체력이 부족하여 긴급 후퇴 중" :
            _crowdProtection != null && _crowdProtection.IsProtecting ? "플레이어 곁에서 몰려오는 좀비를 막는 중" :
            _tacticalMotor != null && _tacticalMotor.HasAction ? _tacticalMotor.Description :
            Tactics != null && Tactics.IsHolding ? "플레이어의 지시로 그 자리에서 대기 중" :
            _target != null ? (_blocking ? "좀비를 막으며 플레이어 보호 중" : "좀비에 대응 중") :
            _following ? "플레이어를 따라가는 중" : "플레이어 근처에서 경계 중";

        public void StopForPlayerHold()
        {
            _target = _swingTarget = null;
            _motionEnd = 0f;
            _following = _blocking = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            _nextHeal = Time.time + HealInterval;
            _agent = GetComponent<NavMeshAgent>();
            Llm = GetComponent<LocalLlmClient>();
            _root = transform; _health.Configure(CombatTeam.Companion, 200f); if (!TryGetComponent<CpcSkills>(out _)) gameObject.AddComponent<CpcSkills>();
            _skills = GetComponent<CpcSkills>();
            _tacticalMotor = GetComponent<CpcTacticalMotor>();
            _crowdProtection = GetComponent<CpcCrowdProtection>();
            if (_crowdProtection == null && _tacticalMotor != null) _crowdProtection = gameObject.AddComponent<CpcCrowdProtection>();
            InstallShieldInterceptor();
            if (GroundedNavigation)
            {
                _groundHeight = _root.position.y;
                _groundPath = new NavMeshPath();
                _agent.autoTraverseOffMeshLink = false;
                Body.applyRootMotion = false;
            }
        }

        private void Update()
        {
            if (_skills != null && _skills.IsCharging) return; if (Time.time >= _nextHeal)
            {
                _nextHeal = Time.time + HealInterval;
                _health.Heal(_health.MaxHealth * HealFraction);
            }
            if (!_agent.isOnNavMesh || Player == null) { if (_crowdProtection != null) _crowdProtection.Suspend(); return; }
            if (!_health.IsAlive || !Player.Health.IsAlive)
            {
                if (_crowdProtection != null) _crowdProtection.Suspend();
                _agent.isStopped = true;
                _blocking = false;
                Body.SetFloat(SPEED, 0);
                if (DirectionalMovement) DirectionalLocomotion.Update(Body, _root, Vector3.zero, false);
                Body.SetLayerWeight(1, 0);
                return;
            }

            if (_health.Current <= _health.MaxHealth * RetreatHealthFraction)
            {
                if (!_retreating)
                {
                    _retreating = true;
                    if (_crowdProtection != null) _crowdProtection.Suspend();
                    if (Tactics != null) Tactics.SuspendForRetreat();
                    _retreatDestination = Player.transform.position - Player.transform.forward * RetreatDistance;
                    _nextPath = 0f;
                    _agent.ResetPath();
                    _swingTarget = null;
                    _motionEnd = 0;
                    _retreatLine = RetreatLine;
                    Debug.Log("[CPC/Retreat] " + RetreatLine, this);
                    foreach (SurvivalZombie zombie in SurvivalZombie.Active) if (zombie != null) zombie.ReleaseTaunt(this);
                }
            }
            else if (_retreating && _health.Current >= _health.MaxHealth * Mathf.Min(1f, RetreatHealthFraction + RetreatRecoveryMargin))
            {
                _retreating = false;
                _retreatLine = null;
            }

            ResolvePendingMelee();

            SurvivalZombie threat = _retreating ? null : FindThreat();
            CpcAction action = CpcAction.None;
            SurvivalZombie selected = null;
            bool tactical = Tactics != null && Tactics.isActiveAndEnabled;
            if (tactical) Tactics.Observe(threat);
            bool ordered = false;
            if (!_retreating && tactical && _tacticalMotor != null)
            {
                bool hasSafety = _tacticalMotor.TryImmediateSafety(out CpcOrder safety);
                if (hasSafety && safety.Action == CpcAction.Evade)
                {
                    _tacticalMotor.Tick(safety);
                    UpdateLocomotion();
                    return;
                }
                if (_crowdProtection != null && _crowdProtection.TryTick()) { UpdateLocomotion(); return; }
                if (hasSafety)
                {
                    _tacticalMotor.Tick(safety); UpdateLocomotion(); return;
                }
                if (Tactics.TryGetExecutionOrder(out CpcOrder order))
                {
                    if (order.Action == CpcAction.Follow && Tactics.UseAutonomousFallback &&
                        Tactics.TryGetFollowProtectionThreat(out SurvivalZombie protectionThreat))
                    {
                        // 추종 지시는 유지하면서 기존 도발/방패/근접 폴백으로 주변을 보호한다.
                        threat = protectionThreat;
                        _tacticalMotor.Clear();
                    }
                    else
                    {
                        ordered = true; action = order.Action; selected = order.Target;
                        if (_tacticalMotor.Tick(order)) { UpdateLocomotion(); return; }
                    }
                }
                else _tacticalMotor.Clear();
            }
            bool fallback = !ordered && (!tactical || Tactics.UseAutonomousFallback);
            bool Allowed(CpcAction value) => !tactical || Tactics.IsAllowed(value);
            _target = ordered ? selected : fallback ? threat : null;
            if (!ordered && !Allowed(CpcAction.Guard) && !Allowed(CpcAction.Taunt) && !Allowed(CpcAction.Attack))
                _target = null;
            if (_target == null)
            {
                _blocking = false;
                Body.SetLayerWeight(1, ShieldGrip != null ? 1 : 0);
                if ((_retreating || action == CpcAction.Retreat) && Allowed(CpcAction.Retreat))
                {
                    _following = false;
                    Vector3 destination = _retreating ? _retreatDestination :
                        Player.transform.position - Player.transform.forward * RetreatDistance;
                    Vector3 offset = destination - _root.position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= .55f * .55f ||
                        _agent.hasPath && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
                    {
                        _agent.ResetPath();
                        _agent.isStopped = true;
                        if (_retreating) _retreatDestination = _root.position;
                    }
                    else MoveTo(destination, RetreatSpeed, .5f);
                }
                else if ((action == CpcAction.Follow || !ordered) && Allowed(CpcAction.Follow)) FollowPlayer();
                else
                {
                    _following = false;
                    _agent.ResetPath();
                    _agent.isStopped = true;
                }
            }
            else
            {
                _following = false;
                Vector3 offset = _target.transform.position - _root.position;
                offset.y = 0;
                float distance = offset.magnitude;
                if (distance > .05f) _root.rotation = Quaternion.LookRotation(offset);
                _agent.updateRotation = false;
                bool acting = Time.time < _motionEnd;
                if (!acting && (action == CpcAction.Taunt || fallback && Allowed(CpcAction.Taunt)) && distance <= TauntRange && Time.time >= _nextTaunt)
                {
                    TryBeginTaunt(_target);
                    acting = true;
                }
                else if (!acting && (action == CpcAction.Attack || fallback && Allowed(CpcAction.Attack)) && distance <= MeleeRange && Time.time >= _nextMelee)
                {
                    TryBeginMelee(_target, false);
                    acting = true;
                }
                if (acting) _agent.isStopped = true;
                else MoveTo(_target.transform.position, FollowSpeed, action == CpcAction.Attack ? Mathf.Min(GuardDistance, MeleeRange * .85f) : GuardDistance);
                bool shouldBlock = !acting && Allowed(CpcAction.Guard) && distance <= GuardDistance + .6f;
                if (ShieldGrip == null && shouldBlock && !_blocking) Body.CrossFadeInFixedTime("Block", .1f, 1);
                _blocking = shouldBlock;
                Body.SetLayerWeight(1, ShieldGrip != null || acting || _blocking ? 1 : 0);
            }

            UpdateLocomotion();
        }

        public float GetNavigationHeight() => _agent != null ? _agent.height * .75f : .3f;
        public bool TryInterceptMelee(SurvivalZombie attacker, Health intendedTarget, float damage)
        {
            if (!_blocking || !_health.IsAlive || Player == null || intendedTarget != Player.Health || attacker == null) return false;
            Vector3 from = attacker.transform.position;
            Vector3 line = Player.transform.position - from; line.y = 0f;
            float length = line.sqrMagnitude;
            if (length < .0001f) return false;
            Vector3 offset = _root.position - from; offset.y = 0f;
            float fraction = Vector3.Dot(offset, line) / length;
            if (fraction <= 0f || fraction >= 1f || (offset - line * fraction).sqrMagnitude > MeleeInterceptWidth * MeleeInterceptWidth) return false;
            Vector3 incoming = from - _root.position; incoming.y = 0f;
            if (Vector3.Dot(_root.forward, incoming.normalized) < BlockFacingDot) return false;
            ReceiveHit(damage, attacker.gameObject);
            Debug.Log("[CPC/Shield] Melee intercepted for player", this);
            return true;
        }
        public void CancelPendingMelee() { _swingTarget = null; _counterSwing = false; _motionEnd = 0f; }
        public void ApplyTacticalPath(NavMeshPath path, float stop)
        {
            _following = false;
            _agent.speed = SprintSpeed;
            _agent.stoppingDistance = stop;
            _agent.updateRotation = false;
            _agent.SetPath(path);
            _agent.isStopped = false;
        }
        public void SetTacticalBlock(bool blocking, Vector3 source)
        {
            _blocking = blocking;
            Vector3 facing = source - _root.position; facing.y = 0f;
            if (facing.sqrMagnitude > .0001f) _root.rotation = Quaternion.LookRotation(facing);
            if (Body != null) Body.SetLayerWeight(1, ShieldGrip != null || blocking ? 1f : 0f);
        }
        public bool TryBeginTaunt(SurvivalZombie target)
        {
            if (target == null || !target.HitPoints.IsAlive || Time.time < _nextTaunt ||
                Vector3.Distance(_root.position, target.transform.position) > TauntRange) return false;
            _nextTaunt = Time.time + TauntCooldown;
            _motionEnd = Time.time + TauntMotionTime;
            target.Taunt(this, TauntDuration);
            if (Body != null) Body.CrossFadeInFixedTime("Taunt", .1f, 1);
            return true;
        }
        public bool TryBeginMelee(SurvivalZombie target, bool counter)
        {
            if (_swingTarget != null || target == null || !target.HitPoints.IsAlive || target.IsHidden ||
                Time.time < _nextMelee || Vector3.Distance(_root.position, target.transform.position) > MeleeRange ||
                !HasMeleeLine(target) || counter && target.Threat.Stage != ZombieAttackStage.Recovering) return false;
            float windup = MeleeMotionTime * MeleeHitFraction;
            if (counter && target.Threat.Remaining < windup) return false;
            _nextMelee = Time.time + MeleeCooldown;
            _motionEnd = Time.time + MeleeMotionTime;
            _hitTime = Time.time + windup;
            _swingTarget = target;
            _counterSwing = counter;
            if (Body != null) Body.CrossFadeInFixedTime("Melee", .1f, 1);
            return true;
        }
        private bool HasMeleeLine(SurvivalZombie target)
        {
            Vector3 from = _root.position + Vector3.up * GetNavigationHeight();
            Vector3 direction = target.AimPoint - from;
            int count = Physics.RaycastNonAlloc(from, direction.normalized, _meleeHits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = _meleeHits[i].transform;
                if (!hit.IsChildOf(_root) && !hit.IsChildOf(target.transform)) return false;
            }
            return true;
        }
        private void ResolvePendingMelee()
        {
            if (_swingTarget == null || Time.time < _hitTime) return;
            if (_swingTarget.HitPoints.IsAlive && !_swingTarget.IsHidden &&
                Vector3.Distance(_root.position, _swingTarget.transform.position) <= MeleeRange && HasMeleeLine(_swingTarget) &&
                (!_counterSwing || _swingTarget.Threat.Stage == ZombieAttackStage.Recovering && _swingTarget.Threat.Remaining > 0f))
                _swingTarget.HitPoints.TakeDamage(MeleeDamage, gameObject);
            _swingTarget = null;
            _counterSwing = false;
        }
        private void UpdateLocomotion()
        {
            if (Body == null) return;
            float speed = _agent.isStopped ? 0f : _agent.velocity.magnitude;
            Body.SetFloat(SPEED, speed > .15f ? Mathf.Clamp01(speed / SprintSpeed) : 0f, .12f, Time.deltaTime);
            if (DirectionalMovement) DirectionalLocomotion.Update(Body, _root, _agent.isStopped ? Vector3.zero : _agent.velocity, speed > SprintSpeed * .7f);
        }
        private void InstallShieldInterceptor()
        {
            if (GetComponentInChildren<CpcShieldInterceptor>() != null) return;
            var shield = new GameObject("Tactical Shield Interceptor");
            shield.transform.SetParent(_root, false);
            float scale = Mathf.Max(.001f, _root.lossyScale.x);
            shield.transform.localPosition = new Vector3(0f, _agent.height * .5f, _agent.radius + .03f) / scale;
            var trigger = shield.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(_agent.radius * 3f, _agent.height * 1.1f, .06f) / scale;
            shield.AddComponent<CpcShieldInterceptor>().Companion = this;
        }
        private void FollowPlayer()
        {
            Vector3 playerPosition = Player.transform.position;
            Vector3 offset = playerPosition - _root.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (_following && distance <= FollowDistance)
                _following = false;
            else if (!_following && distance >= FollowDistance + FollowResumeMargin)
            {
                _following = true;
                _nextPath = 0f;
            }

            if (_following)
            {
                // 플레이어가 회전해도 목적지가 좌우로 움직이지 않게 위치만 추적한다.
                MoveTo(playerPosition, FollowSpeed, Mathf.Max(.1f, FollowDistance - .15f));
                return;
            }

            _agent.updateRotation = false;
            if (_agent.hasPath || _agent.pathPending) _agent.ResetPath();
            _agent.isStopped = true;
        }

        private SurvivalZombie FindThreat()
        {
            SurvivalZombie nearest = null;
            float best = DetectionRange * DetectionRange;
            foreach (SurvivalZombie zombie in SurvivalZombie.Active)
            {
                if (zombie == null || !zombie.HitPoints.IsAlive) continue;
                float distance = (zombie.transform.position - _root.position).sqrMagnitude;
                if (distance > best) continue;
                nearest = zombie;
                best = distance;
            }
            return nearest;
        }

        private void LateUpdate()
        {
            if (ShieldGrip != null)
            {
                Shield.rotation = Quaternion.Euler(0f, _root.eulerAngles.y, 0f);
                ShieldGrip.Align();
            }
            else if (_blocking && Shield != null) Shield.rotation = _root.rotation;
        }

        private void MoveTo(Vector3 point, float speed, float stop)
        {
            _agent.updateRotation = _target == null;
            _agent.speed = speed;
            _agent.stoppingDistance = stop;
            _agent.isStopped = false;
            if (Time.time < _nextPath) return;
            _nextPath = Time.time + RepathInterval;
            if (Tactics != null && Tactics.isActiveAndEnabled)
            {
                Vector3 center = Player.transform.position;
                Vector3 offset = point - center;
                offset.y = 0f;
                Vector3 limited = center + Vector3.ClampMagnitude(offset, Tactics.MaxPlayerDistance);
                point.x = limited.x;
                point.z = limited.z;
            }
            if (GroundedNavigation) point.y = _groundHeight;
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, _agent.areaMask))
            {
                if (!GroundedNavigation)
                {
                    _agent.SetDestination(hit.position);
                    return;
                }
                if (Mathf.Abs(hit.position.y - _groundHeight) <= GroundHeightTolerance &&
                    _agent.CalculatePath(hit.position, _groundPath) && _groundPath.status == NavMeshPathStatus.PathComplete && WithinLeash(_groundPath))
                {
                    _agent.SetPath(_groundPath);
                    return;
                }
            }
            if (GroundedNavigation) _agent.ResetPath();
        }

        private bool WithinLeash(NavMeshPath path)
        {
            if (Tactics == null || !Tactics.isActiveAndEnabled) return true;
            Vector3 center = Player.transform.position;
            Vector3 current = _root.position - center;
            current.y = 0f;
            float limit = Mathf.Max(Tactics.MaxPlayerDistance, current.magnitude) + .05f;
            int count = path.GetCornersNonAlloc(_groundCorners);
            if (count >= _groundCorners.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Vector3 corner = _groundCorners[i];
                Vector3 offset = corner - center;
                offset.y = 0f;
                if (offset.sqrMagnitude > limit * limit) return false;
            }
            return true;
        }

        public void ReceiveHit(float damage, GameObject source)
        {
            Vector3 direction = source != null ? source.transform.position - _root.position : Vector3.zero;
            direction.y = 0;
            if (_blocking) damage *= BlockDamageMultiplier;
            _health.TakeDamage(damage, source);
            if (Tactics != null && Tactics.isActiveAndEnabled) Tactics.NotifyDamage();
        }

        public void SayToCompanion(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _retreatLine = null;
            CpcLlmSettings settings = Llm.Settings;
            string prompt = settings != null ? settings.DialoguePrompt : "플레이어: {message}";
            prompt = prompt.Replace("{playerHealth}", Player.Health.Current.ToString("0"))
                .Replace("{companionHealth}", _health.Current.ToString("0"))
                .Replace("{message}", message);
            string fallback = "지금은 응답을 받지 못했어. " + CurrentActionDescription + ".";
            if (Tactics != null && Tactics.isActiveAndEnabled) Tactics.Chat(message.Trim(), fallback);
            else Llm.Enqueue(prompt, fallback, true);
        }
    }
}













