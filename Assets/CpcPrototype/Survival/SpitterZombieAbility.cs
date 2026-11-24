using UnityEngine;
using UnityEngine.AI;
namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class SpitterZombieAbility : ZombieAbility
    {
        public override ZombieKind Kind => ZombieKind.Spitter;
        [Min(0f)] public float SpitRange = 12f;
        [Min(.01f)] public float SpitInterval = 3f;
        [Min(0f)] public float SpitDamage = 20f;
        [Min(.01f), Tooltip("발사 속도. 같은 거리의 낙하량에는 영향을 주지 않습니다.")]
        public float SpitSpeed = 1f;
        [Min(0f), Tooltip("SpitRange 거리에서 중력으로 떨어지는 거리. 속도와 별도로 궤적을 조절합니다.")]
        public float SpitDropAtRange = .5f;
        [Min(.01f), Tooltip("최소 수명. 사거리까지 도달하는 시간보다 짧으면 자동 연장됩니다.")]
        public float SpitLifetime = 6f;
        [Min(.01f)] public float SpitSize = .2f;
        public float SpitSpawnHeight = 0.3f;
        public float SpitTargetHeight = .3f;
        [Min(.05f)] public float AimDuration = 1.2f;
        [Min(.05f)] public float RecoveryDuration = .8f;
        private float _nextSkill, _fireAt;
        private Health _aimTarget;
        private Vector3 _aimPoint;
        protected override void OnInitialized() { _nextSkill = Time.time + SpitInterval; }
        public override void Tick()
        {
            if (!HitPoints.IsAlive || Status.IsStunned)
            {
                if (_aimTarget != null) Zombie.ReportThreat(ZombieAttackStage.Idle, null, transform.position, transform.position, 0f, 0f);
                _aimTarget = null;
                return;
            }
            if (_aimTarget != null)
            {
                if (!_aimTarget.IsAlive) { _aimTarget = null; return; }
                if (Time.time < _fireAt) return;
                Vector3 origin = transform.position + Vector3.up * SpitSpawnHeight;
                var projectile = SpitProjectile.Launch(origin, _aimPoint, gameObject, SpitDamage,
                    SpitSpeed, SpitLifetime, SpitSize, SpitRange, SpitDropAtRange);
                Zombie.TrackProjectile(projectile, _aimTarget, _aimPoint, Vector3.Distance(origin, _aimPoint) / Mathf.Max(.01f, SpitSpeed));
                _nextSkill = Time.time + SpitInterval;
                _aimTarget = null;
                return;
            }
            if (Time.time < _nextSkill) return;
            _aimTarget = Nearest(SpitRange);
            if (_aimTarget == null) return;
            _aimPoint = _aimTarget.transform.position + Vector3.up * SpitTargetHeight;
            _fireAt = Time.time + AimDuration;
            Zombie.ReportThreat(ZombieAttackStage.Aiming, _aimTarget, transform.position + Vector3.up * SpitSpawnHeight,
                _aimPoint, SpitSize * .5f, AimDuration);
        }
        private void OnDisable() { _aimTarget = null; }
    }
}
