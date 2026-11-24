using UnityEngine;

namespace CpcPrototype
{
    /// <summary>Only explicitly marked lamp bulbs can receive the player's grapple.</summary>
    public sealed class GrappleAnchor : MonoBehaviour
    {
        public Transform HookPoint;
        public Collider Platform;
        [Min(.001f)] public float RingRadius = .08f;
        public Vector3 Position => HookPoint != null ? HookPoint.position : transform.position;
    }
}
