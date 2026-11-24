using UnityEngine;

namespace CpcPrototype
{
    public enum ZombieAttackStage { Idle, Approach, Aiming, Windup, Projectile, Emerging, Recovering, Leaping, Exploding }
    public enum CpcDecisionOrigin { PlayerCommand, SituationLlm, Safety }
    public struct CpcOrder
    {
        public int Serial;
        public CpcAction Action;
        public SurvivalZombie Target;
        public CpcDecisionOrigin Origin;
        public int ThreatRevision;
        public Vector3 HoldPosition;
        public float Expires;
    }

    public struct ZombieThreat
    {
        public int Revision;
        public ZombieAttackStage Stage;
        public Health Target;
        public Vector3 Origin;
        public Vector3 Impact;
        public float Radius;
        public float Deadline;
        public bool HasCue;
        public Vector3 CuePosition;
        public float Remaining => Mathf.Max(0f, Deadline - Time.time);
        public bool IsDangerous => Stage == ZombieAttackStage.Aiming || Stage == ZombieAttackStage.Windup ||
            Stage == ZombieAttackStage.Projectile || Stage == ZombieAttackStage.Emerging ||
            Stage == ZombieAttackStage.Leaping || Stage == ZombieAttackStage.Exploding;
    }

    public struct ObservedZombie
    {
        public SurvivalZombie Source;
        public Vector3 Position;
        public ZombieThreat Threat;
        public bool CueOnly;
    }
}
