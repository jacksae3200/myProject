using UnityEngine;
using UnityEngine.AI;
namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class JumperZombieAbility : ZombieAbility
    {
        public override ZombieKind Kind => ZombieKind.Jumper;
        [Min(0f)] public float TriggerRange = 2f;
        [Min(0f)] public float LeapRange = 8f;
        [Min(.01f)] public float LeapInterval = 5f;
        [Min(.01f)] public float LeapDuration = .65f;
        [Min(0f)] public float LeapHeight = 0.1f;
        [Min(0f)] public float LeapDamage = 30f;
        [Min(0f)] public float FleeDuration = 1.5f;
        [Min(0f)] public float FleeDistance = 5f;
        private float _nextSkill, _leapStart, _fleeUntil;
        private bool _leaping;
        private Vector3 _start, _landing;
        public override bool ControlsMovement => _leaping || Time.time < _fleeUntil;
        protected override void OnInitialized() { _nextSkill = Time.time + LeapInterval; }
        public override void Tick()
        {
            if (!HitPoints.IsAlive || Status.IsStunned) { FinishLeap(); return; }
            if (_leaping)
            {
                float t = Mathf.Clamp01((Time.time - _leapStart) / Mathf.Max(.01f, LeapDuration));
                transform.position = Vector3.Lerp(_start, _landing, t) + Vector3.up * (4f * t * (1f - t) * LeapHeight);
                if (t >= 1f)
                {
                    FinishLeap();
                    Health victim = Nearest(TriggerRange);
                    CombatStatus.Damage(victim, LeapDamage, gameObject);
                    _fleeUntil = Time.time + FleeDuration;
                    Zombie.ReportThreat(ZombieAttackStage.Recovering, victim, transform.position, _landing, 0f, FleeDuration);
                    if (victim != null) _landing = transform.position + (transform.position - victim.transform.position).normalized * FleeDistance;
                }
                return;
            }
            if (Time.time < _fleeUntil)
            {
                if (Agent.isOnNavMesh) { Agent.isStopped = false; Agent.SetDestination(_landing); }
                return;
            }
            if (Time.time < _nextSkill) return;
            Health target = Nearest(LeapRange);
            if (target == null) return;
            if (Agent.isOnNavMesh &&
                NavMesh.SamplePosition(target.transform.position, out NavMeshHit hit, 1f, Agent.areaMask) &&
                !NavMesh.Raycast(transform.position, hit.position, out _, Agent.areaMask))
            {
                _nextSkill = Time.time + LeapInterval;
                _start = transform.position;
                _landing = hit.position;
                _leapStart = Time.time;
                _leaping = true;
                Zombie.ReportThreat(ZombieAttackStage.Leaping, target, _start, _landing, TriggerRange, LeapDuration);
                Agent.isStopped = true;
                Agent.updatePosition = false;
            }
        }

        private void FinishLeap()
        {
            if (!_leaping) return;
            _leaping = false;
            Agent.updatePosition = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, Agent.areaMask)) Agent.Warp(hit.position);
        }

        private void OnDisable() { FinishLeap(); }

    }
}



