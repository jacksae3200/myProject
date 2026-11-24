using System.Collections.Generic;
using CpcPrototype.BehaviorTree;
using UnityEngine;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterMotor), typeof(Health))]
    public sealed class CubeMonsterBrain : MonoBehaviour
    {
        public enum MonsterState { Idle, Chase, Attack, Return, Dead }
        private static readonly List<CubeMonsterBrain> MONSTERS = new List<CubeMonsterBrain>();
        public static IReadOnlyList<CubeMonsterBrain> ActiveMonsters => MONSTERS;
        [SerializeField] private Health _player;
        [SerializeField] private Health _companion;
        [SerializeField] private float _detectRange = 8f;
        [SerializeField] private float _loseRange = 12f;
        [SerializeField] private float _leashRange = 15f;
        [SerializeField] private float _attackRange = 1.5f;
        [SerializeField] private float _speed = 2.5f;
        [SerializeField] private float _damage = 10f;
        [SerializeField] private float _attackCooldown = 1.3f;
        private CharacterMotor _motor;
        private Health _health;
        private Transform _cachedTransform;
        private Health _target;
        private Node _root;
        private Vector3 _home;
        private float _nextAttack;
        private bool _returning;
        public Health Health => _health;
        public MonsterState State { get; private set; }
        public Vector3 Home => _home;

        public void Configure(Health player, Health companion) { _player = player; _companion = companion; }
        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>(); _health = GetComponent<Health>();
            _cachedTransform = transform; _home = _cachedTransform.position;
            _root = new Selector(
                new Sequence(new Condition(() => !_health.IsAlive), new ActionNode(Dead)),
                new Sequence(new Condition(HasTarget), new Selector(
                    new Sequence(new Condition(InAttackRange), new ActionNode(Attack)),
                    new ActionNode(Chase))),
                new ActionNode(ReturnHome));
        }
        private void OnEnable() { if (!MONSTERS.Contains(this)) MONSTERS.Add(this); }
        private void OnDisable() { MONSTERS.Remove(this); if (_motor != null) _motor.SetMotion(Vector3.zero); }
        private void FixedUpdate() { _root.Tick(); }

        private bool HasTarget()
        {
            if (_returning) return false;
            if ((_cachedTransform.position - _home).sqrMagnitude > _leashRange * _leashRange)
            { _target = null; _returning = true; return false; }
            if (_target != null)
            {
                if (_target.IsAlive && DistanceSquared(_target) <= _loseRange * _loseRange) return true;
                _target = null; _returning = true; return false;
            }
            float nearest = _detectRange * _detectRange;
            SelectTarget(_player, ref nearest); SelectTarget(_companion, ref nearest);
            return _target != null;
        }
        private void SelectTarget(Health candidate, ref float nearest)
        {
            if (candidate == null || !candidate.IsAlive) return;
            float distance = DistanceSquared(candidate);
            if (distance <= nearest) { nearest = distance; _target = candidate; }
        }
        private float DistanceSquared(Health target) => (target.transform.position - _cachedTransform.position).sqrMagnitude;
        private bool InAttackRange() => DistanceSquared(_target) <= _attackRange * _attackRange;
        private NodeStatus Dead() { State = MonsterState.Dead; _motor.SetMotion(Vector3.zero); return NodeStatus.Success; }
        private NodeStatus Chase()
        {
            State = MonsterState.Chase;
            MoveToward(_target.transform.position);
            return NodeStatus.Running;
        }
        private NodeStatus Attack()
        {
            State = MonsterState.Attack; _motor.SetMotion(Vector3.zero);
            Vector3 direction = _target.transform.position - _cachedTransform.position; direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) _cachedTransform.rotation = Quaternion.LookRotation(direction);
            if (Time.time >= _nextAttack)
            {
                _nextAttack = Time.time + _attackCooldown;
                _motor.ShowAttack(); _target.TakeDamage(_damage, gameObject);
            }
            return NodeStatus.Running;
        }
        private NodeStatus ReturnHome()
        {
            Vector3 delta = _home - _cachedTransform.position; delta.y = 0f;
            if (delta.sqrMagnitude < 0.09f)
            { State = MonsterState.Idle; _returning = false; _motor.SetMotion(Vector3.zero); return NodeStatus.Success; }
            State = MonsterState.Return; MoveToward(_home); return NodeStatus.Running;
        }
        private void MoveToward(Vector3 destination)
        {
            Vector3 direction = destination - _cachedTransform.position; direction.y = 0f;
            _motor.SetMotion(direction.normalized * _speed);
        }
    }
}
