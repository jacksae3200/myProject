using UnityEngine;

namespace CpcPrototype
{
    public sealed class SpitProjectile : MonoBehaviour
    {
        [Min(0f)] public float Damage = 20f;
        private GameObject _source;
        private bool _spent;
        private SurvivalZombie _zombie;
        public Vector3 Velocity => _body != null ? _body.linearVelocity : Vector3.zero;
        private Rigidbody _body;
        private Vector3 _gravityAcceleration;
        public static SpitProjectile Launch(Vector3 origin, Vector3 target, GameObject source, float damage = 20f, float speed = 12f, float lifetime = 6f, float size = .2f, float range = 12f, float dropAtRange = .5f)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Zombie Spit";
            cube.transform.position = origin;
            cube.transform.localScale = Vector3.one * Mathf.Max(.01f, size);
            var projectile = cube.AddComponent<SpitProjectile>();
            projectile._zombie = source.GetComponent<SurvivalZombie>();
            projectile._source = source; projectile.Damage = damage;
            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = false;
            body.linearDamping = 0f;
            projectile._body = body;
            float travelTime = Mathf.Max(.01f, range) / Mathf.Max(.01f, speed);
            // 속도가 달라도 사거리 도달 시 낙하량이 같도록 중력 가속도를 계산한다.
            projectile._gravityAcceleration = Physics.gravity.normalized *
                (2f * Mathf.Max(0f, dropAtRange) / (travelTime * travelTime));
            // 목표 방향으로 곧바로 발사한다. 낙하량을 보정하는 위쪽 초기 속도는 넣지 않는다.
            body.linearVelocity = (target - origin).normalized * Mathf.Max(.01f, speed);
            foreach (Collider collider in source.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(cube.GetComponent<Collider>(), collider);
            // 느린 투사체가 사거리에 도달하기 전에 수명으로 사라지는 것을 방지한다.
            Destroy(cube, Mathf.Max(lifetime, travelTime + Time.fixedDeltaTime));
            return projectile;
        }
        public bool TryBlock(ShieldCompanion companion)
        {
            if (_spent || companion == null || !companion.IsBlocking) return false;
            _spent = true;
            companion.ReceiveHit(Damage, _source != null ? _source : gameObject);
            Debug.Log("[CPC/Shield] Projectile intercepted", companion);
            Destroy(gameObject);
            return true;
        }
        private void OnDestroy() { if (_zombie != null) _zombie.ProjectileEnded(this); }
        private void FixedUpdate()
        {
            if (!_spent && _body != null) _body.AddForce(_gravityAcceleration, ForceMode.Acceleration);
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (_spent) return;
            _spent = true;
            Health victim = collision.collider.GetComponentInParent<Health>();
            if (victim != null && victim.Team != CombatTeam.Monster) CombatStatus.Damage(victim, Damage, _source != null ? _source : gameObject);
            Destroy(gameObject);
        }
    }
}


