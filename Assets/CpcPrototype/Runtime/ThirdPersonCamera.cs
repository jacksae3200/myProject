using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private ThirdPersonPlayer _player;
        [SerializeField] private float _distance = 5f;
        [SerializeField] private float _sensitivity = 0.12f;
        private float _yaw;
        private float _pitch = 22f;
        private Transform _cachedTransform;
        public void Configure(Transform target, ThirdPersonPlayer player) { _target = target; _player = player; }
        private void Awake() { _cachedTransform = transform; }

        private void LateUpdate()
        {
            if (_target == null) return;
            Mouse mouse = Mouse.current;
            if ((_player == null || !_player.InputBlocked) && mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * _sensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _sensitivity, -10f, 65f);
            }
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _target.position + Vector3.up * 1.35f;
            _cachedTransform.SetPositionAndRotation(pivot - rotation * Vector3.forward * _distance, rotation);
        }
    }
}
