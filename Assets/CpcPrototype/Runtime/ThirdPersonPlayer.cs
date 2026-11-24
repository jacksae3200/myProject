using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterMotor), typeof(Health))]
    public sealed class ThirdPersonPlayer : MonoBehaviour
    {
        [SerializeField] private Transform _camera;
        [SerializeField] private float _walkSpeed = 3.5f;
        [SerializeField] private float _runSpeed = 6.5f;
        [SerializeField] private float _attackDamage = 25f;
        [SerializeField] private float _attackReach = 1.8f;
        [SerializeField] private float _attackCooldown = 0.45f;
        private readonly Collider[] _hits = new Collider[24];
        private readonly HashSet<Health> _damaged = new HashSet<Health>();
        private CharacterMotor _motor;
        private Health _health;
        private Transform _cachedTransform;
        private float _nextAttack;
        private bool _inputBlocked;
        public CharacterMotor Motor => _motor;
        public bool InputBlocked => _inputBlocked;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _health = GetComponent<Health>();
            _cachedTransform = transform;
        }

        public void Configure(Transform cameraTransform) { _camera = cameraTransform; }
        public void BlockInput(bool blocked) { _inputBlocked = blocked; if (blocked) _motor.SetMotion(Vector3.zero); }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (_inputBlocked || !_health.IsAlive || keyboard == null || _camera == null)
            {
                _motor.SetMotion(Vector3.zero);
                return;
            }
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            Vector3 forward = _camera.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = _camera.right; right.y = 0f; right.Normalize();
            Vector3 direction = Vector3.ClampMagnitude(right * x + forward * z, 1f);
            _motor.SetMotion(direction * (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? _runSpeed : _walkSpeed));
            if (keyboard.spaceKey.wasPressedThisFrame) _motor.RequestJump();
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())) TryAttack();
        }

        public void TryAttack()
        {
            if (!_health.IsAlive || Time.time < _nextAttack) return;
            _nextAttack = Time.time + _attackCooldown;
            _motor.ShowAttack();
            Vector3 origin = _cachedTransform.position + Vector3.up * 0.85f;
            int count = Physics.OverlapSphereNonAlloc(origin, _attackReach, _hits, ~0, QueryTriggerInteraction.Ignore);
            _damaged.Clear();
            for (int i = 0; i < count; i++)
            {
                Health victim = _hits[i].GetComponentInParent<Health>();
                if (victim == null || victim.Team != CombatTeam.Monster || !victim.IsAlive || !_damaged.Add(victim)) continue;
                Vector3 direction = victim.transform.position - _cachedTransform.position; direction.y = 0f;
                if (Vector3.Dot(_cachedTransform.forward, direction.normalized) < 0.25f) continue;
                victim.TakeDamage(_attackDamage, gameObject);
                Debug.Log($"[Combat] {victim.name}: {victim.Current:0}/{victim.MaxHealth:0}", victim);
            }
        }
    }
}
