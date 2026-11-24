using UnityEngine;

namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class BurrowerZombieAbility : ZombieAbility
    {
        public override ZombieKind Kind => ZombieKind.Burrower;
        public override bool ControlsMovement => true;
        [Min(0f)] public float TriggerRange = 2f;
        [Min(0f)] public float BurrowInitialDelay = 3f;
        [Min(.01f)] public float BurrowInterval = 5f;
        [Min(0f)] public float BurrowDamage = 20f;
        [Min(0f)] public float RootDuration = 5f;
        [Min(.05f)] public float EmergeWarning = 1f;
        [Min(.05f)] public float RecoveryDuration = 1.2f;
        public bool BodyRevealed { get; private set; }
        private float _nextSkill, _hitAt, _hideAt;
        private CombatStatus _boundPlayer;
        private Health _victim;
        private GameObject _cue;
        private Renderer[] _renderers;
        private Material _cueMaterial;
        protected override void OnInitialized() { _nextSkill = Time.time + BurrowInitialDelay; }
        private void Start()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _cue = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cue.name = "Visible Burrow Emergence Warning";
            Destroy(_cue.GetComponent<Collider>());
            _cueMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _cueMaterial.SetColor("_BaseColor", new Color(1f, .4f, .05f));
            _cue.GetComponent<Renderer>().sharedMaterial = _cueMaterial;
            _cue.SetActive(false);
            SetBodyVisible(false);
        }
        public override void Tick()
        {
            if (Agent.isOnNavMesh) Agent.isStopped = true;
            if (!HitPoints.IsAlive) { ReleasePlayer(); if (_cue != null) _cue.SetActive(false); return; }
            if (BodyRevealed && Time.time >= _hideAt) SetBodyVisible(false);
            if (Status.IsStunned) { CancelWarning(); return; }
            Vector3 disarmPoint = _victim != null ? Zombie.Threat.CuePosition : transform.position;
            if (Zombie.Companion != null && Zombie.Companion.HitPoints.IsAlive &&
                Vector3.Distance(disarmPoint, Zombie.Companion.transform.position) <= TriggerRange)
            {
                ReleasePlayer(); CancelWarning();
                HitPoints.TakeDamage(HitPoints.Current, Zombie.Companion.gameObject);
                return;
            }
            if (_victim != null)
            {
                if (Time.time < _hitAt) return;
                if (_victim.IsAlive && Vector3.Distance(_victim.transform.position, Zombie.Threat.Impact) <= TriggerRange)
                {
                    CombatStatus.Damage(_victim, BurrowDamage, gameObject);
                    if (_victim.Team == CombatTeam.Player)
                    {
                        _boundPlayer = CombatStatus.Get(_victim.gameObject);
                        _boundPlayer.Root(this, RootDuration);
                    }
                }
                Zombie.ReportThreat(ZombieAttackStage.Recovering, _victim, Zombie.Threat.CuePosition,
                    Zombie.Threat.Impact, TriggerRange, RecoveryDuration, true, Zombie.Threat.CuePosition);
                _victim = null;
                _hideAt = Time.time + RecoveryDuration;
                SetBodyVisible(true);
                if (_cue != null) _cue.SetActive(false);
                return;
            }
            if (Time.time < _nextSkill) return;
            _victim = Nearest(TriggerRange);
            if (_victim == null) return;
            _nextSkill = Time.time + EmergeWarning + BurrowInterval;
            _hitAt = Time.time + EmergeWarning;
            Vector3 point = _victim.transform.position;
            Zombie.ReportThreat(ZombieAttackStage.Emerging, _victim, point, point, TriggerRange, EmergeWarning, true, point);
            if (_cue != null)
            {
                _cue.transform.position = point + Vector3.up * .015f;
                _cue.transform.localScale = new Vector3(TriggerRange * 2f, .01f, TriggerRange * 2f);
                _cue.SetActive(true);
            }
        }
        private void SetBodyVisible(bool visible)
        {
            BodyRevealed = visible;
            if (_renderers == null) return;
            foreach (Renderer renderer in _renderers) if (renderer != null) renderer.enabled = visible;
        }
        private void CancelWarning()
        {
            if (_victim != null) Zombie.ReportThreat(ZombieAttackStage.Idle, null, transform.position, transform.position, 0f, 0f);
            _victim = null;
            if (_cue != null) _cue.SetActive(false);
        }
        private void ReleasePlayer() { if (_boundPlayer != null) _boundPlayer.ReleaseRoot(this); _boundPlayer = null; }
        private void OnDisable() { ReleasePlayer(); CancelWarning(); }
        private void OnDestroy() { if (_cue != null) Destroy(_cue); if (_cueMaterial != null) Destroy(_cueMaterial); }
    }
}
