using UnityEngine;

namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class CpcThreatSensor : MonoBehaviour
    {
        public ShieldCompanion Companion;
        [Min(.02f)] public float SampleInterval = .1f;
        [Min(0f)] public float PrioritySwitchMargin = 1f;
        [Min(0f)] public float PlayerThreatWeight = 60f;
        [Min(0f)] public float SpecialZombieWeight = 25f;
        [Min(0f)] public float DistanceWeight = 5f;
        public LayerMask VisibilityLayers = ~0;
        private readonly ObservedZombie[] _observed = new ObservedZombie[48];
        private readonly RaycastHit[] _hits = new RaycastHit[48];
        private float _nextSample;
        public int Count { get; private set; }
        public int SignalVersion { get; private set; }
        public SurvivalZombie Priority { get; private set; }
        public ObservedZombie Get(int index) => _observed[index];

        public void Refresh(bool force = false)
        {
            if (!force && Time.time < _nextSample || Companion == null || Companion.Player == null) return;
            _nextSample = Time.time + SampleInterval;
            Count = 0;
            int signal = 17;
            float best = float.MinValue;
            SurvivalZombie previousPriority = Priority;
            float previousScore = float.MinValue;
            Priority = null;
            for (int i = 0; i < SurvivalZombie.Active.Count && Count < _observed.Length; i++)
            {
                SurvivalZombie zombie = SurvivalZombie.Active[i];
                if (!TryObserve(zombie, out ObservedZombie observation)) continue;
                _observed[Count++] = observation;
                ZombieThreat threat = observation.Threat;
                unchecked { signal += zombie.GetEntityId().GetHashCode() * 397 ^ threat.Revision * 31 ^ (int)threat.Stage; }
                float score = Score(observation);
                if (zombie == previousPriority) previousScore = score;
                if (score <= best) continue;
                best = score;
                Priority = zombie;
            }
            if (previousScore != float.MinValue && previousScore + PrioritySwitchMargin >= best) Priority = previousPriority;
            unchecked { signal = signal * 31 + (Priority != null ? Priority.GetEntityId().GetHashCode() : 0); }
            SignalVersion = signal;
        }

        public float Score(ObservedZombie observation)
        {
            ZombieThreat threat = observation.Threat;
            return (threat.IsDangerous ? 30f : 0f) +
                (threat.Target == Companion.Player.Health ? PlayerThreatWeight : 0f) +
                (observation.Source.Kind != ZombieKind.Normal ? SpecialZombieWeight : 0f) -
                Vector3.Distance(observation.Position, Companion.Player.transform.position) * DistanceWeight;
        }

        public bool TryObserve(SurvivalZombie zombie, out ObservedZombie observation)
        {
            observation = default;
            if (zombie == null || !zombie.isActiveAndEnabled || zombie.HitPoints == null ||
                !zombie.HitPoints.IsAlive && zombie.Threat.Stage != ZombieAttackStage.Exploding) return false;
            ZombieThreat threat = zombie.Threat;
            bool hidden = zombie.IsHidden;
            if (hidden && !threat.HasCue) return false;
            Vector3 point = hidden ? threat.CuePosition + Vector3.up * .04f : zombie.AimPoint;
            if ((point - Companion.transform.position).sqrMagnitude > Companion.DetectionRange * Companion.DetectionRange) return false;
            Vector3 eye = Companion.transform.position + Vector3.up * Companion.GetNavigationHeight();
            Vector3 offset = point - eye;
            int hits = Physics.RaycastNonAlloc(eye, offset.normalized, _hits, offset.magnitude, VisibilityLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Transform hit = _hits[i].transform;
                if (hit.IsChildOf(Companion.transform) || hit.IsChildOf(Companion.Player.transform) || hit.IsChildOf(zombie.transform)) continue;
                return false;
            }
            observation = new ObservedZombie { Source = zombie, Position = hidden ? threat.CuePosition : zombie.transform.position, Threat = threat, CueOnly = hidden };
            return true;
        }
    }
}

