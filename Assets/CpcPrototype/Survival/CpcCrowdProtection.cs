using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    // 다수의 근접 압박은 모델 응답을 기다리지 않고 플레이어 곁에서 방패로 대응한다.2
    [DisallowMultipleComponent, RequireComponent(typeof(ShieldCompanion))]
    public sealed class CpcCrowdProtection : MonoBehaviour
    {
        [Min(.1f)] public float CrowdRadius = 1.25f;
        [Min(2)] public int EnterCount = 3;
        [Min(0)] public int ExitCount = 1;
        [Min(0f)] public float MinimumGuardSeconds = 1f;
        [Min(0f)] public float ClearDelay = 1f;
        [Min(.05f)] public float StandDistance = .22f;
        [Min(.01f)] public float ArrivalDistance = .07f;
        [Min(.02f)] public float SampleInterval = .15f;
        public bool ProtectDuringHold = true;
        public bool UseTaunt = true;
        public int NearbyCount { get; private set; }
        public int SignalVersion { get; private set; }
        public bool IsProtecting { get; private set; }
        public bool HasPressure => isActiveAndEnabled && NearbyCount >= Mathf.Max(2, EnterCount);
        public Vector3 CrowdDirection { get; private set; } = Vector3.forward;
        private ShieldCompanion _companion;
        private CpcTacticalMotor _motor;
        private NavMeshAgent _agent;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private float _nextSample, _nextPath, _startedAt, _clearSince = -1f;
        private SurvivalZombie _nearest;
        private bool _pathBlocked;

        private void Awake()
        {
            _companion = GetComponent<ShieldCompanion>();
            _motor = GetComponent<CpcTacticalMotor>();
            _agent = GetComponent<NavMeshAgent>();
        }
        public void Refresh(bool force = false)
        {
            if (!isActiveAndEnabled || !force && Time.time < _nextSample || _companion == null || _companion.Player == null) return;
            _nextSample = Time.time + SampleInterval;
            int previousBand = NearbyCount >= Mathf.Max(2, EnterCount) ? 2 : NearbyCount > Mathf.Min(ExitCount, EnterCount - 1) ? 1 : 0;
            NearbyCount = 0;
            _nearest = null;
            Vector3 player = _companion.Player.transform.position;
            Vector3 weighted = Vector3.zero, nearestDirection = CrowdDirection;
            float closest = float.MaxValue;
            for (int i = 0; i < SurvivalZombie.Active.Count; i++)
            {
                SurvivalZombie zombie = SurvivalZombie.Active[i];
                if (zombie == null || !zombie.isActiveAndEnabled || zombie.HitPoints == null || !zombie.HitPoints.IsAlive || zombie.IsHidden) continue;
                Vector3 offset = zombie.transform.position - player;
                if (Mathf.Abs(offset.y) > _companion.GroundHeightTolerance + _agent.height || offset.sqrMagnitude > CrowdRadius * CrowdRadius || !HasClearContact(zombie)) continue;
                NearbyCount++;
                offset.y = 0f;
                float distance = offset.magnitude;
                Vector3 direction = distance > .001f ? offset / distance : CrowdDirection;
                weighted += direction * (zombie.Target == _companion.Player.Health ? 2f : 1f) / Mathf.Max(.15f, distance);
                if (distance >= closest) continue;
                closest = distance; _nearest = zombie; nearestDirection = direction;
            }
            if (NearbyCount > 0) CrowdDirection = weighted.sqrMagnitude > .01f ? weighted.normalized : nearestDirection;
            int band = HasPressure ? 2 : NearbyCount > Mathf.Min(ExitCount, EnterCount - 1) ? 1 : 0;
            if (band != previousBand) SignalVersion++;
        }
        private bool HasClearContact(SurvivalZombie zombie)
        {
            Vector3 start = _companion.Player.transform.position + Vector3.up * _companion.GetNavigationHeight();
            Vector3 direction = zombie.AimPoint - start;
            int count = Physics.RaycastNonAlloc(start, direction.normalized, _hits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count >= _hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].transform;
                if (hit.IsChildOf(_companion.Player.transform) || hit.IsChildOf(transform)) continue;
                // 좀비 몸이 겹쳐도 압박 수를 세되, 벽이나 차량 뒤의 적은 세지 않는다.
                if (_hits[i].collider.GetComponentInParent<SurvivalZombie>() != null) continue;
                return false;
            }
            return true;
        }
        public bool TryTick()
        {
            if (!isActiveAndEnabled || _motor == null || !_motor.isActiveAndEnabled || _agent == null || !_agent.isOnNavMesh || _companion.Player == null ||
                !_companion.HitPoints.IsAlive || !_companion.Player.Health.IsAlive || _companion.IsRetreating ||
                !ProtectDuringHold && _companion.Tactics != null && _companion.Tactics.IsHolding)
            { Suspend(); return false; }
            Refresh();
            bool entering = !IsProtecting && HasPressure;
            if (entering)
            {
                IsProtecting = true; _startedAt = Time.time; _clearSince = -1f; _nextPath = 0f;
                _companion.CancelPendingMelee(); _motor.Clear();
                Debug.Log($"[CPC/Safety] crowd protect player; nearby={NearbyCount}", this);
                Debug.Log("[CPC/Execute/Safety] ProtectPlayer", this);
            }
            if (!IsProtecting) return false;
            if (NearbyCount <= Mathf.Clamp(ExitCount, 0, Mathf.Max(0, EnterCount - 1)))
            {
                if (_clearSince < 0f) _clearSince = Time.time;
                if (Time.time - _startedAt >= MinimumGuardSeconds && Time.time - _clearSince >= ClearDelay)
                { Suspend(); Debug.Log("[CPC/Safety] crowd cleared; resume previous directive", this); return false; }
            }
            else _clearSince = -1f;

            Vector3 player = _companion.Player.transform.position;
            Vector3 point = player + CrowdDirection * Mathf.Max(StandDistance, _agent.radius * 2.5f);
            if (Time.time >= _nextPath)
            {
                _nextPath = Time.time + _motor.ReplanInterval;
                _pathBlocked = !_motor.TryMoveToProtectionPoint(point, ArrivalDistance);
                if (_pathBlocked) _companion.StopForPlayerHold();
            }
            Vector3 flat = transform.position - point; flat.y = 0f;
            if (flat.sqrMagnitude <= ArrivalDistance * ArrivalDistance) _companion.StopForPlayerHold();
            _companion.SetTacticalBlock(true, transform.position + CrowdDirection);
            if (UseTaunt && _nearest != null && Vector3.Distance(transform.position, player) <= StandDistance + .2f)
                _companion.TryBeginTaunt(_nearest);
            if (entering) _companion.Llm.PublishFeedback(_pathBlocked ? "좀비가 몰려와. 길이 막혀서 여기서 방패를 들게." : "좀비가 몰려와. 네 곁에서 막을게!");
            return true;
        }
        public void Suspend()
        {
            if (IsProtecting) SignalVersion++;
            IsProtecting = false; _clearSince = -1f;
        }
        private void OnDisable() { Suspend(); }
    }
}
