using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    [RequireComponent(typeof(ShieldCompanion))]
    public sealed class CpcSkills : MonoBehaviour
    {
        public float ChargeSpeed = 10f;
        public float ChargeDuration = .65f;
        public float ChargeCooldown = 6f;
        public float ShoveRadius = 3f;
        public float ShoveDistance = 3f;
        public float ShoveCooldown = 4f;
        public bool IsCharging { get; private set; }
        private float _chargeEnd, _nextCharge, _nextShove;
        private Vector3 _direction;
        private ShieldCompanion _companion;
        private NavMeshAgent _agent;
        private readonly List<SurvivalZombie> _pushed = new List<SurvivalZombie>();
        private void Awake() { _companion = GetComponent<ShieldCompanion>(); _agent = GetComponent<NavMeshAgent>(); }
        private void Update()
        {
            if (!_companion.HitPoints.IsAlive || _companion.Player == null || _companion.Player.IsChatOpen)
            { if (IsCharging) FinishCharge(); return; }
            Keyboard keys = Keyboard.current;
            if (keys == null) return;
            if (keys.qKey.wasPressedThisFrame) TryCharge();
            if (keys.eKey.wasPressedThisFrame) TryShove();
        }
        public bool TryCharge()
        {
            if (IsCharging || !_companion.HitPoints.IsAlive || !_agent.isOnNavMesh || Time.time < _nextCharge) return false;
            IsCharging = true;
            _pushed.Clear();
            _direction = transform.forward;
            _chargeEnd = Time.time + ChargeDuration;
            _nextCharge = Time.time + ChargeCooldown;
            _agent.isStopped = true;
            return true;
        }
        private void FixedUpdate()
        {
            if (!IsCharging) return;
            if (!_companion.HitPoints.IsAlive || !_agent.isOnNavMesh) { FinishCharge(); return; }
            Vector3 destination = transform.position + _direction * (ChargeSpeed * Time.fixedDeltaTime);
            if (NavMesh.Raycast(transform.position, destination, out _, _agent.areaMask)) { FinishCharge(); return; }
            _agent.Move(_direction * (ChargeSpeed * Time.fixedDeltaTime));
            foreach (SurvivalZombie zombie in SurvivalZombie.Active)
            {
                if (zombie == null || !zombie.HitPoints.IsAlive || _pushed.Contains(zombie)) continue;
                if ((zombie.transform.position - transform.position).sqrMagnitude <= 2.25f) _pushed.Add(zombie);
            }
            PlacePushed();
            if (Time.time >= _chargeEnd) FinishCharge();
        }
        private void PlacePushed()
        {
            for (int i = 0; i < _pushed.Count; i++)
            {
                SurvivalZombie zombie = _pushed[i];
                if (zombie != null && zombie.HitPoints.IsAlive)
                    zombie.PushTo(transform.position + _direction * (1.3f + i / 3 * .6f) + transform.right * ((i % 3 - 1) * .6f), 1f);
            }
        }
        private void FinishCharge() { PlacePushed(); IsCharging = false; if (_agent.isOnNavMesh) _agent.isStopped = false; }
        public bool TryShove()
        {
            if (IsCharging || !_companion.HitPoints.IsAlive || Time.time < _nextShove) return false;
            _nextShove = Time.time + ShoveCooldown;
            foreach (SurvivalZombie zombie in SurvivalZombie.Active)
            {
                if (zombie == null || !zombie.HitPoints.IsAlive) continue;
                Vector3 offset = zombie.transform.position - transform.position;
                if (offset.sqrMagnitude > ShoveRadius * ShoveRadius) continue;
                Vector3 direction = offset.sqrMagnitude > .001f ? offset.normalized : transform.forward;
                zombie.PushTo(zombie.transform.position + direction * ShoveDistance, .5f);
            }
            return true;
        }
        private void OnDisable() { if (IsCharging) FinishCharge(); }
    }
}
