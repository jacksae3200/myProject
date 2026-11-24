using System;
using UnityEngine;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _jumpHeight = 1.3f;
        [SerializeField] private float _gravity = -24f;
        [SerializeField] private Transform _visual;
        private CharacterController _controller;
        private Health _health;
        private Transform _cachedTransform;
        private Vector3 _motion;
        private float _verticalSpeed;
        private bool _jumpRequested;
        private Vector3 _visualOrigin;
        private float _attackUntil;
        public event Action Jumped;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public Vector3 Position => _cachedTransform.position;
        public float MotionSpeed => _health != null && _health.IsAlive ? _motion.magnitude : 0f;
        public bool IsAttacking => Time.time < _attackUntil;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _cachedTransform = transform;
            if (_visual != null) _visualOrigin = _visual.localPosition;
        }

        public void ConfigureVisual(Transform visual) { _visual = visual; _visualOrigin = visual.localPosition; }
        public void SetMotion(Vector3 motion) { _motion = motion; }
        public void RequestJump() { if (_health.IsAlive && IsGrounded) _jumpRequested = true; }
        public void ShowAttack() { _attackUntil = Time.time + 0.18f; }

        private void FixedUpdate()
        {
            bool grounded = _controller.isGrounded;
            if (grounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
            if (_jumpRequested && grounded && _health.IsAlive)
            {
                _verticalSpeed = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
                Jumped?.Invoke();
            }
            _jumpRequested = false;
            _verticalSpeed += _gravity * Time.fixedDeltaTime;
            Vector3 horizontal = _health.IsAlive ? _motion : Vector3.zero;
            _controller.Move((horizontal + Vector3.up * _verticalSpeed) * Time.fixedDeltaTime);
            if (horizontal.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(horizontal);
                _cachedTransform.rotation = Quaternion.RotateTowards(_cachedTransform.rotation, target, 720f * Time.fixedDeltaTime);
            }
        }

        private void LateUpdate()
        {
            if (_visual == null) return;
            // 원본 에셋에는 모션 클립이 없어 걷기 바운스와 공격 기울기로 상태를 표현한다.
            float bounce = IsGrounded && _motion.sqrMagnitude > 0.1f ? Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.035f : 0f;
            _visual.localPosition = _visualOrigin + Vector3.up * bounce;
            _visual.localRotation = Quaternion.Euler(Time.time < _attackUntil ? 22f : 0f, 0f, 0f);
        }

        private void OnDisable() { _motion = Vector3.zero; _jumpRequested = false; }
    }
}
