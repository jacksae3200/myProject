using UnityEngine;

namespace CpcPrototype
{
    // 속박은 이동만, 스턴은 이동과 공격을 함께 차단한다.
    public sealed class CombatStatus : MonoBehaviour
    {
        private float _rootUntil, _stunUntil;
        private readonly System.Collections.Generic.Dictionary<Object, float> _roots = new System.Collections.Generic.Dictionary<Object, float>();
        public bool IsRooted
        {
            get
            {
                if (Time.time < _rootUntil) return true;
                foreach (var root in _roots) if (root.Key != null && Time.time < root.Value) return true;
                return false;
            }
        }
        public bool IsStunned => Time.time < _stunUntil;
        public bool CannotMove => IsRooted || IsStunned;
        public void Root(float seconds) => _rootUntil = Mathf.Max(_rootUntil, Time.time + seconds);
        public void Stun(float seconds) => _stunUntil = Mathf.Max(_stunUntil, Time.time + seconds);
        public void ReleaseRoot() => _rootUntil = 0f;
        public void Root(Object source, float seconds) => _roots[source] = Time.time + seconds;
        public void ReleaseRoot(Object source) => _roots.Remove(source);
        public static CombatStatus Get(GameObject actor)
        {
            if (!actor.TryGetComponent(out CombatStatus status)) status = actor.AddComponent<CombatStatus>();
            return status;
        }
        public static void Damage(Health victim, float damage, GameObject source)
        {
            if (victim == null || !victim.IsAlive) return;
            if (victim.TryGetComponent(out ShieldCompanion companion)) companion.ReceiveHit(damage, source);
            else victim.TakeDamage(damage, source);
        }
    }
}
