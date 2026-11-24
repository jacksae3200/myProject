using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public sealed class SurvivalZombie : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<SurvivalZombie> Active = new System.Collections.Generic.List<SurvivalZombie>();
        public ZombieKind Kind;
        public float HeadHitRadius = .22f;
        [HideInInspector] public float CrawlerSpeedMultiplier = 1.5f;
        private CombatStatus _status;
        private ZombieAbility _ability;
        public float EffectiveMoveSpeed => MoveSpeed * (_ability != null && _ability.enabled ? _ability.SpeedMultiplier : 1f);
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
        public bool DirectionalMovement;
        public SurvivalPlayer Player;
        public ShieldCompanion Companion;
        public Animator Body;
        [Min(.1f)] public float ShotAggroDuration = 8f;
        private float _shotAggroUntil;
        [Min(.1f)] public float DetectionRange = 10f;
        [Min(.1f)] public float LoseRange = 17f;
        [Min(.1f)] public float HomeLeash = 21f;
        [Min(.1f)] public float MoveSpeed = 2.1f;
        [Min(.1f)] public float AttackRange = 1.9f;
        [Min(0)] public float Damage = 10f;
        [Min(.1f)] public float AttackCooldown = 1.45f;
        [Min(0)] public float AttackWindup = .43f;
        [Min(.02f)] public float RepathInterval = .22f;
        private NavMeshAgent _agent;
        private CapsuleCollider _capsule;
        private Transform _head;
        private Health _health, _target, _swing;
        private Transform _root;
        private Vector3 _home;
        private ShieldCompanion _tauntedBy;
        private float _tauntUntil, _nextAttack, _hitTime, _nextPath;
        private bool _returning;
        private static readonly int SPEED = Animator.StringToHash("Speed");
        public Health HitPoints => _health;
        public Health Target => _swing != null ? _swing : _target;
        public string TacticalId { get; private set; }
        public ZombieThreat Threat { get; private set; }
        public SpitProjectile ActiveProjectile { get; private set; }
        public bool IsHidden => _ability is BurrowerZombieAbility burrow && !burrow.BodyRevealed;

        public void ReportThreat(ZombieAttackStage stage, Health target, Vector3 origin, Vector3 impact,
            float radius, float duration, bool hasCue = false, Vector3 cuePosition = default)
        {
            ZombieThreat previous = Threat;
            Threat = new ZombieThreat { Revision = previous.Revision + 1, Stage = stage, Target = target,
                Origin = origin, Impact = impact, Radius = radius, Deadline = Time.time + Mathf.Max(0f, duration),
                HasCue = hasCue, CuePosition = cuePosition };
        }
        public void TrackProjectile(SpitProjectile projectile, Health target, Vector3 impact, float travelTime)
        {
            ActiveProjectile = projectile;
            ReportThreat(ZombieAttackStage.Projectile, target, projectile.transform.position, impact, .15f, travelTime);
        }
        public void ProjectileEnded(SpitProjectile projectile)
        {
            if (ActiveProjectile != projectile) return;
            ActiveProjectile = null;
            if (_health.IsAlive) ReportThreat(ZombieAttackStage.Recovering, Threat.Target, _root.position, Threat.Impact, 0f, .8f);
        }
        public bool TryGetTauntTarget(out Health target)
        {
            target = _tauntedBy != null && Time.time < _tauntUntil && _tauntedBy.HitPoints.IsAlive ? _tauntedBy.HitPoints : null;
            return target != null;
        }
        public Vector3 AimPoint => _capsule != null ? _capsule.bounds.center : _root.position + Vector3.up * _agent.height * .5f;

        public Vector3 HeadAimPoint => _head != null ? _head.position + _head.up * HeadOffset : AimPoint;
        public bool HasHead => _head != null;
        public float HeadOffset = .018f;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _capsule = GetComponent<CapsuleCollider>();
            if (Body != null && Body.isHuman) _head = Body.GetBoneTransform(HumanBodyBones.Head);
            _root = transform; if (Player == null) Player = FindFirstObjectByType<SurvivalPlayer>(); if (Companion == null) Companion = FindFirstObjectByType<ShieldCompanion>();
            TacticalId = "zombie_" + GetEntityId().ToString();
            _home = _root.position;
            _status = CombatStatus.Get(gameObject);
            _health.Configure(CombatTeam.Monster, Kind == ZombieKind.Crawler ? 50f : Kind == ZombieKind.Normal || Kind == ZombieKind.Burrower ? 100f : 80f);
            RefreshAbility();
        }

        public void RefreshAbility()
        {
            _ability = ZombieAbilitySelection.Synchronize(this);
            if (Application.isPlaying && _ability != null) _ability.Initialize(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // OnValidate 중에는 컴포넌트를 변경하지 않고 메인 에디터 루프로 넘긴다.
            UnityEditor.EditorApplication.delayCall -= RefreshEditorAbility;
            UnityEditor.EditorApplication.delayCall += RefreshEditorAbility;
        }
        private void RefreshEditorAbility()
        {
            if (this == null || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorUtility.IsPersistent(this)) return;
            RefreshAbility();
        }
#endif

        public void PushTo(Vector3 position, float stun)
        {
            _swing = null;
            _status.Stun(stun);
            if (_agent.isOnNavMesh && NavMesh.SamplePosition(position, out NavMeshHit hit, 1f, _agent.areaMask) && !NavMesh.Raycast(_root.position, hit.position, out _, _agent.areaMask)) _agent.Warp(hit.position);
        }
        public void ReactToShot(SurvivalPlayer shooter)
        {
            if (!_health.IsAlive || shooter == null) return;
            Player = shooter;
            _target = shooter.Health;
            _tauntedBy = null;
            _swing = null;
            _returning = false;
            _shotAggroUntil = Time.time + ShotAggroDuration;
            _nextPath = 0f;
        }

        public void Taunt(ShieldCompanion source, float seconds)
        {
            if (!_health.IsAlive || !source.HitPoints.IsAlive || source.IsRetreating) return;
            _tauntedBy = source;
            _tauntUntil = Time.time + seconds;
            _target = source.HitPoints;
            if (_swing != null && Time.time < _hitTime)
            {
                _swing = source.HitPoints;
                ReportThreat(ZombieAttackStage.Windup, _swing, AimPoint, _swing.transform.position,
                    AttackRange, _hitTime - Time.time);
            }
            _returning = false;
        }

        public void ReleaseTaunt(ShieldCompanion source)
        {
            if (_tauntedBy != source) return;
            _tauntedBy = null;
            _target = null;
            if (_swing == source.HitPoints) _swing = null;
        }

        private void Update()
        {
            if (Threat.Stage == ZombieAttackStage.Recovering && Time.time >= Threat.Deadline)
                ReportThreat(ZombieAttackStage.Idle, null, _root.position, _root.position, 0f, 0f);
            if (_ability != null && _ability.enabled) _ability.Tick();
            if (!isActiveAndEnabled || !_agent.isOnNavMesh) return;
            if (_status.IsStunned || _ability != null && _ability.enabled && _ability.ControlsMovement)
            {
                _swing = null;
                if (_status.IsStunned) _agent.isStopped = true;
                if (Body != null) Body.SetFloat(SPEED, 0f);
                return;
            }
            if (!_health.IsAlive)
            {
                _agent.isStopped = true;
                Body.SetFloat(SPEED, 0);
                if (DirectionalMovement) DirectionalLocomotion.Update(Body, _root, Vector3.zero, false);
                if (!Body.GetCurrentAnimatorStateInfo(0).IsName("Dead") && !Body.IsInTransition(0))
                    Body.CrossFadeInFixedTime("Dead", .15f, 0);
                return;
            }

            if (_swing != null && Time.time >= _hitTime)
            {
                if (_swing.IsAlive && InRange(_swing, AttackRange + .15f))
                {
                    if (Companion != null && Companion.TryInterceptMelee(this, _swing, Damage)) { }
                    else if (Companion != null && _swing == Companion.HitPoints) Companion.ReceiveHit(Damage, gameObject);
                    else _swing.TakeDamage(Damage, gameObject);
                }
                _swing = null;
                ReportThreat(ZombieAttackStage.Recovering, _target, AimPoint, Threat.Impact, 0f, Mathf.Max(.3f, AttackCooldown - AttackWindup));
            }

            PickTarget();
            if (_target == null)
            {
                if ((_root.position - _home).sqrMagnitude < .3f) { _returning = false; _agent.isStopped = true; }
                else WalkTo(_home, .25f);
            }
            else if (InRange(_target, AttackRange) || _swing != null)
            {
                _agent.isStopped = true;
                Vector3 direction = _target.transform.position - _root.position;
                direction.y = 0;
                if (direction.sqrMagnitude > .01f) _root.rotation = Quaternion.LookRotation(direction);
                if ((Kind == ZombieKind.Normal || Kind == ZombieKind.Crawler) && _swing == null && Time.time >= _nextAttack && InRange(_target, AttackRange))
                {
                    _nextAttack = Time.time + AttackCooldown;
                    _hitTime = Time.time + AttackWindup;
                    _swing = _target;
                    ReportThreat(ZombieAttackStage.Windup, _swing, AimPoint, _swing.transform.position, AttackRange, AttackWindup);
                    Body.CrossFadeInFixedTime("Attack", .08f, 0, 0);
                }
            }
            else
            {
                if (Threat.Stage == ZombieAttackStage.Idle || Threat.Stage == ZombieAttackStage.Approach && Threat.Target != _target)
                    ReportThreat(ZombieAttackStage.Approach, _target, AimPoint, _target.transform.position, AttackRange, 0f);
                WalkTo(_target.transform.position, Mathf.Max(.2f, AttackRange - .25f));
            }

            Body.SetFloat(SPEED, Mathf.Clamp01(_agent.velocity.magnitude / Mathf.Max(.01f, EffectiveMoveSpeed)), .1f, Time.deltaTime);
            if (DirectionalMovement) DirectionalLocomotion.Update(Body, _root, _agent.velocity, _agent.velocity.sqrMagnitude > EffectiveMoveSpeed * EffectiveMoveSpeed * .64f);
        }

        private void PickTarget()
        {
            if ((_root.position - _home).sqrMagnitude > HomeLeash * HomeLeash)
            { _returning = true; _target = null; _tauntedBy = null; }
            if (_returning) return;
            if (_tauntedBy != null)
            {
                if (Time.time < _tauntUntil && _tauntedBy.HitPoints.IsAlive && !_tauntedBy.IsRetreating)
                { _target = _tauntedBy.HitPoints; return; }
                _tauntedBy = null;
                _target = null;
            }
            if (_target != null && _target.IsAlive &&
                (InRange(_target, LoseRange) || Player != null && _target == Player.Health && Time.time < _shotAggroUntil))
                return;
            if (_target != null) { _returning = true; _target = null; return; }
            if (Player != null && Player.Health.IsAlive && InRange(Player.Health, DetectionRange)) _target = Player.Health; if (Companion != null && Companion.HitPoints.IsAlive && InRange(Companion.HitPoints, DetectionRange) && (_target == null || (Companion.transform.position - _root.position).sqrMagnitude < (_target.transform.position - _root.position).sqrMagnitude)) _target = Companion.HitPoints;
        }

        private bool InRange(Health target, float range) => (target.transform.position - _root.position).sqrMagnitude <= range * range;

        private void WalkTo(Vector3 destination, float stop)
        {
            _agent.isStopped = false;
            _agent.speed = EffectiveMoveSpeed;
            _agent.stoppingDistance = stop;
            if (Time.time < _nextPath) return;
            _nextPath = Time.time + RepathInterval;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, _agent.areaMask)) _agent.SetDestination(hit.position);
        }
    }
}











