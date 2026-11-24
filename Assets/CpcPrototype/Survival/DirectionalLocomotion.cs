using UnityEngine;

namespace CpcPrototype
{
    internal static class DirectionalLocomotion
    {
        private static readonly int MOVE_X = Animator.StringToHash("MoveX");
        private static readonly int MOVE_Z = Animator.StringToHash("MoveZ");

        public static void Update(Animator body, Transform root, Vector3 velocity, bool sprinting)
        {
            Vector3 local = root.InverseTransformDirection(velocity);
            local.y = 0f;
            Vector3 direction = local.sqrMagnitude > .0225f ? local.normalized * (sprinting ? 2f : 1f) : Vector3.zero;
            body.SetFloat(MOVE_X, direction.x, .1f, Time.deltaTime);
            body.SetFloat(MOVE_Z, direction.z, .1f, Time.deltaTime);
        }
    }
}
