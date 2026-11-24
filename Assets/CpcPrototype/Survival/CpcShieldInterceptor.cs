using UnityEngine;

namespace CpcPrototype
{
    public sealed class CpcShieldInterceptor : MonoBehaviour
    {
        public ShieldCompanion Companion;
        private void OnTriggerEnter(Collider other)
        {
            if (Companion == null || !Companion.IsBlocking) return;
            if (other.TryGetComponent(out SpitProjectile projectile)) projectile.TryBlock(Companion);
        }
    }
}
