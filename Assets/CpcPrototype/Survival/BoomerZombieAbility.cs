using UnityEngine;
using UnityEngine.AI;
namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class BoomerZombieAbility : ZombieAbility
    {
        public override ZombieKind Kind => ZombieKind.Boomer;
        [Min(0f)] public float TriggerRange = 2f;
        [Min(0f)] public float ExplosionDelay = 2f;
        [Min(0f)] public float ExplosionDamage = 50f;
        [Min(0f)] public float ExplosionRadiusMultiplier = 2f;
        private float _explodeAt = -1f;
        private bool _exploded;
        public override bool ControlsMovement => _explodeAt >= 0f;
        public override void Tick()
        {
            if (_explodeAt < 0f && (!HitPoints.IsAlive || Nearest(TriggerRange) != null))
            {
                _explodeAt = Time.time + ExplosionDelay;
                float size = TryGetComponent(out CapsuleCollider capsule) ? Mathf.Max(capsule.bounds.size.x, capsule.bounds.size.y, capsule.bounds.size.z) : 1f;
                Zombie.ReportThreat(ZombieAttackStage.Exploding, Nearest(TriggerRange), transform.position, transform.position, size * ExplosionRadiusMultiplier, ExplosionDelay);
            }
            if (ControlsMovement && Agent.isOnNavMesh) Agent.isStopped = true;
            if (!_exploded && _explodeAt >= 0f && Time.time >= _explodeAt) Explode();
        }        private void Explode()
        {
            _exploded = true;
            float size = 1f;
            if (TryGetComponent(out CapsuleCollider capsule)) size = Mathf.Max(capsule.bounds.size.x, capsule.bounds.size.y, capsule.bounds.size.z);
            float radius = size * ExplosionRadiusMultiplier;
            Hit(Zombie.Player != null ? Zombie.Player.Health : null);
            Hit(Zombie.Companion != null ? Zombie.Companion.HitPoints : null);
            if (HitPoints.IsAlive) HitPoints.TakeDamage(HitPoints.Current, gameObject);
            gameObject.SetActive(false);
            void Hit(Health victim)
            {
                if (victim != null && (victim.transform.position - transform.position).sqrMagnitude <= radius * radius)
                    CombatStatus.Damage(victim, ExplosionDamage, gameObject);
            }
        }

    }
}



