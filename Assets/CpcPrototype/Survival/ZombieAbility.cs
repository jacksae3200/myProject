using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    public abstract class ZombieAbility : MonoBehaviour
    {
        protected SurvivalZombie Zombie { get; private set; }
        protected Health HitPoints { get; private set; }
        protected NavMeshAgent Agent { get; private set; }
        protected CombatStatus Status { get; private set; }
        public virtual bool ControlsMovement => false;
        public virtual float SpeedMultiplier => 1f;
        public abstract ZombieKind Kind { get; }
        private bool _initialized;
        public void Initialize(SurvivalZombie zombie)
        {
            if (_initialized) return;
            _initialized = true;
            Zombie = zombie;
            HitPoints = GetComponent<Health>();
            Agent = GetComponent<NavMeshAgent>();
            Status = CombatStatus.Get(gameObject);
            OnInitialized();
        }
        protected virtual void OnInitialized() { }
        public abstract void Tick();
        protected Health Nearest(float range)
        {
            if (Zombie.TryGetTauntTarget(out Health taunted) && (taunted.transform.position - transform.position).sqrMagnitude <= range * range) return taunted;
            Health best = null;
            float distance = range * range;
            Consider(Zombie.Player != null ? Zombie.Player.Health : null);
            Consider(Zombie.Companion != null ? Zombie.Companion.HitPoints : null);
            return best;
            void Consider(Health candidate)
            {
                if (candidate == null || !candidate.IsAlive) return;
                float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqr > distance) return;
                distance = sqr;
                best = candidate;
            }
        }
    }
}

