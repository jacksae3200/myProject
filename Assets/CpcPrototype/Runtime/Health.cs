using System;
using UnityEngine;

namespace CpcPrototype
{
    public enum CombatTeam { Player, Companion, Monster }

    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _maxHealth = 100f;
        [SerializeField] private CombatTeam _team;
        private float _current;
        public float Current => _current;
        public float MaxHealth => _maxHealth;
        public bool IsAlive => _current > 0f;
        public CombatTeam Team => _team;
        public event Action<Health, float, GameObject> Damaged;
        public event Action<Health> Died;
        public event Action<Health> Changed;

        private void Awake() { _current = _maxHealth; }

        public void Configure(CombatTeam team, float maxHealth)
        {
            _team = team;
            _maxHealth = Mathf.Max(1f, maxHealth);
            _current = _maxHealth;
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f || _current >= _maxHealth) return;
            _current = Mathf.Min(_maxHealth, _current + amount);
            Changed?.Invoke(this);
        }

        public void TakeDamage(float amount, GameObject source)
        {
            if (!IsAlive || amount <= 0f) return;
            float applied = Mathf.Min(_current, amount);
            _current -= applied;
            Damaged?.Invoke(this, applied, source);
            Changed?.Invoke(this);
            if (!IsAlive) Died?.Invoke(this);
        }
    }
}

