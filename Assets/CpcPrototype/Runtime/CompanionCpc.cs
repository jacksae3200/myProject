using System.Collections.Generic;
using UnityEngine;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterMotor), typeof(Health), typeof(LocalLlmClient))]
    public sealed class CompanionCpc : MonoBehaviour
    {
        [SerializeField] private ThirdPersonPlayer _player;
        [SerializeField] private Health _playerHealth;
        [SerializeField] private float _followDistance = 1.8f;
        [SerializeField] private float _followSpeed = 5.7f;
        [SerializeField] private float _discoverRange = 12f;
        [SerializeField] private float _nearRange = 4f;
        [SerializeField] private float _scanInterval = 0.25f;
        private CharacterMotor _motor;
        private Health _health;
        private LocalLlmClient _llm;
        private Transform _cachedTransform;
        private readonly HashSet<CubeMonsterBrain> _discovered = new HashSet<CubeMonsterBrain>();
        private readonly HashSet<CubeMonsterBrain> _nearby = new HashSet<CubeMonsterBrain>();
        private float _nextScan;
        private float _nextDamageMessage;
        private float _nearCooldown;
        private bool _jumpSubscribed;
        public LocalLlmClient Llm => _llm;
        public Health Health => _health;

        public void Configure(ThirdPersonPlayer player, Health playerHealth) { _player = player; _playerHealth = playerHealth; }
        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>(); _health = GetComponent<Health>();
            _llm = GetComponent<LocalLlmClient>(); _cachedTransform = transform;
        }
        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
            if (_playerHealth != null) _playerHealth.Damaged += OnDamaged;
            SubscribeJump();
        }
        private void Start() { SubscribeJump(); }
        private void SubscribeJump()
        {
            if (!_jumpSubscribed && _player != null && _player.Motor != null)
            { _player.Motor.Jumped += OnPlayerJumped; _jumpSubscribed = true; }
        }
        private void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            if (_playerHealth != null) _playerHealth.Damaged -= OnDamaged;
            if (_player != null && _player.Motor != null) _player.Motor.Jumped -= OnPlayerJumped;
            _jumpSubscribed = false;
            _motor.SetMotion(Vector3.zero);
        }

        private void FixedUpdate()
        {
            if (_player == null || !_health.IsAlive || !_playerHealth.IsAlive) { _motor.SetMotion(Vector3.zero); return; }
            Vector3 delta = _player.transform.position - _cachedTransform.position; delta.y = 0f;
            float distance = delta.magnitude;
            // 정지 거리를 넘겨 왕복하지 않도록 가까이에서는 속도를 제한한다.
            float speed = Mathf.Min(_followSpeed, Mathf.Max(0f, distance - _followDistance) / Time.fixedDeltaTime);
            _motor.SetMotion(delta.normalized * speed);
            if (Time.time >= _nextScan) { _nextScan = Time.time + _scanInterval; ScanMonsters(); }
        }

        private void OnPlayerJumped()
        {
            if (!_health.IsAlive) return;
            _motor.RequestJump();
            Debug.Log("[CPC/Event] 플레이어 점프 → 동료도 점프", this);
        }

        private void ScanMonsters()
        {
            IReadOnlyList<CubeMonsterBrain> monsters = CubeMonsterBrain.ActiveMonsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                CubeMonsterBrain monster = monsters[i];
                if (monster == null || monster.Health == null || !monster.Health.IsAlive) continue;
                float distanceSquared = (monster.transform.position - _cachedTransform.position).sqrMagnitude;
                if (distanceSquared <= _discoverRange * _discoverRange && _discovered.Add(monster))
                    ReportEvent("몬스터 발견: " + monster.name, "저기 몬스터가 있어요. 조심해서 가요!");
                if (distanceSquared <= _nearRange * _nearRange)
                {
                    if (_nearby.Add(monster) && Time.time >= _nearCooldown)
                    {
                        _nearCooldown = Time.time + 5f;
                        ReportEvent("몬스터가 가까이 접근: " + monster.name, "몬스터가 가까워요! 피하거나 공격해요!");
                    }
                }
                else if (distanceSquared > (_nearRange + 1f) * (_nearRange + 1f)) _nearby.Remove(monster);
            }
        }

        private void OnDamaged(Health victim, float damage, GameObject source)
        {
            string subject = victim == _health ? "동료 제임스" : "플레이어";
            string description = subject + " 피격 / 피해 " + damage.ToString("0") + " / 공격자 " + (source != null ? source.name : "알 수 없음");
            // 모든 피격은 즉시 로그에 남기되, 모델 요청은 묶어서 과도한 호출을 방지한다.
            Debug.Log("[CPC/Event] " + description, this);
            if (Time.time < _nextDamageMessage || !_health.IsAlive) return;
            _nextDamageMessage = Time.time + 5f;
            SendEvent(description, victim == _health ? "앗, 맞았어요! 조금 물러나요!" : "괜찮아요? 몬스터의 공격을 조심해요!");
        }

        private void ReportEvent(string description, string fallback)
        {
            Debug.Log("[CPC/Event] " + description, this);
            SendEvent(description, fallback);
        }
        private void SendEvent(string description, string fallback)
        {
            CpcLlmSettings settings = _llm.Settings;
            string template = settings != null ? settings.EventPrompt : "게임 사건: {event}";
            _llm.Enqueue(FillState(template).Replace("{event}", description), fallback);
        }
        public void SayToCompanion(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || !_health.IsAlive) return;
            if (message.Length > 500) message = message.Substring(0, 500);
            Debug.Log("[Player/Dialogue] " + message, this);
            CpcLlmSettings settings = _llm.Settings;
            string template = settings != null ? settings.DialoguePrompt : "플레이어 발언: {message}";
            _llm.Enqueue(FillState(template).Replace("{message}", message), "지금은 대답이 잘 안 나오네요. 그래도 곁에 있을게요!", true);
        }
        private string FillState(string template)
        {
            return template.Replace("{playerHealth}", _playerHealth.Current.ToString("0"))
                .Replace("{companionHealth}", _health.Current.ToString("0"));
        }
    }
}
