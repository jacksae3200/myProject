using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CpcPrototype
{
    [Flags]
    public enum CpcAction
    {
        None = 0, Follow = 1, Hold = 2, Guard = 4, Taunt = 8, Attack = 16, Retreat = 32,
        Intercept = 64, Evade = 128, Counterattack = 256, ProtectPlayer = 512
    }

    [RequireComponent(typeof(CpcThreatSensor), typeof(CpcTacticalMotor))]
    public sealed class CpcTactics : MonoBehaviour
    {
        public ShieldCompanion Companion;
        public CpcAction AllowedActions = CpcAction.Follow | CpcAction.Hold | CpcAction.Guard |
            CpcAction.Taunt | CpcAction.Attack | CpcAction.Retreat | CpcAction.Intercept | CpcAction.Evade | CpcAction.Counterattack;
        public bool OnThreat = true;
        public bool OnDamage = true;
        public bool UseAutonomousFallback = true;
        [Min(.1f)] public float MaxPlayerDistance = 4f;
        [Min(1f)] public float DecisionDuration = 6f;
        [Min(1f)] public float MaxEventResponseAge = 12f;
        [Min(1f)] public float MaxChatResponseAge = 45f;
        [Min(.1f)] public float EventInterval = .5f;
        [Min(.1f), Tooltip("따라오며 보호할 때 주변 적과 교전할 플레이어 기준 반경")]
        public float FollowCombatRadius = 2f;
        [Min(.1f), Tooltip("플레이어와 이 거리 이상 벌어지면 교전보다 추종을 우선")]
        public float FollowRejoinDistance = 2.5f;
        [TextArea] public string TacticalPrompt = "플레이어 보호를 우선한다. 체력이 낮으면 후퇴하고, 위험이 크면 공격보다 방어를 우선한다.";
        [Serializable] private sealed class Decision
        {
            public string action;
            public string targetId;
            public string speech;
            public string intent;
            public string followMode;
            public string targetType;
            public string targetPolicy;
            public string goal;
        }
        private sealed class RequestFrame
        {
            public ObservedZombie[] Enemies;
            public int Generation;
            public float Started;
            public bool Chat;
            public string PriorityId;
        }
        private CpcOrder _playerOrder, _autonomousOrder;
        private CpcThreatSensor _sensor;
        private CpcTacticalMotor _motor;
        private int _chatGeneration, _serial, _sentSignal;
        private float _nextEvent;
        private bool _wasHit;
        private bool _passiveFollow, _rejoining;
        public bool IsHolding => _playerOrder.Action == CpcAction.Hold;
        public bool HasPlayerDirective => _playerOrder.Action != CpcAction.None;
        public CpcAction PlayerAction => _playerOrder.Action;
        public bool CanProtectWhileFollowing => PlayerAction == CpcAction.Follow && !_passiveFollow;
        public bool PassiveFollow => PlayerAction == CpcAction.Follow && _passiveFollow;
        public Vector3 HoldPosition => _playerOrder.HoldPosition;
        public string LastPrompt { get; private set; }
        public string LastRejection { get; private set; }
        public int RequestsSent { get; private set; }
        public int AcceptedDecisionCount { get; private set; }
        public int RejectedDecisionCount { get; private set; }
        public CpcAction LastSelectedAction { get; private set; }
        public CpcDecisionOrigin LastDecisionOrigin { get; private set; }
        public SurvivalZombie LastSelectedTarget { get; private set; }
        public CpcThreatSensor Sensor => _sensor;
        public CpcTacticalMotor Motor => _motor;

        private void Awake()
        {
            if (Companion == null) Companion = GetComponent<ShieldCompanion>();
            _sensor = GetComponent<CpcThreatSensor>();
            _motor = GetComponent<CpcTacticalMotor>();
            _sensor.Companion = Companion;
            _motor.Companion = Companion;
            _motor.Sensor = _sensor;
        }
        public void NotifyDamage() => _wasHit = true;
        public void ReleasePlayerMovementOrder()
        {
            _playerOrder = _autonomousOrder = default;
            _passiveFollow = _rejoining = false;
            _chatGeneration++;
        }
        public void SuspendForRetreat()
        {
            _autonomousOrder = default;
            _chatGeneration++;
            Debug.Log("[CPC/Safety] emergency retreat; player directive suspended, not erased", this);
        }
        public void Observe(SurvivalZombie ignored)
        {
            _sensor.Refresh();
            bool changed = _sensor.SignalVersion != _sentSignal;
            if ((!OnThreat || !changed) && (!OnDamage || !_wasHit)) return;
            if (Time.unscaledTime < _nextEvent || Companion.Llm.IsBusy) return;
            if (_sensor.Count == 0 && !_wasHit) { _sentSignal = _sensor.SignalVersion; return; }
            string context = _wasHit ? "CPC가 피격됨. 현재 관측을 바탕으로 대응하라." : "관측된 적, 공격 단계, 공격 대상 또는 우선 위협이 바뀜.";
            _sentSignal = _sensor.SignalVersion;
            _wasHit = false;
            _nextEvent = Time.unscaledTime + EventInterval;
            Request(context, null, false);
        }
        public void Chat(string message, string fallback)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _chatGeneration++;
            _sensor.Refresh(true);
            Request(message.Trim(), fallback, true);
        }
        public bool TryGetOrder(out CpcAction action, out SurvivalZombie target)
        {
            bool valid = TryGetExecutionOrder(out CpcOrder order);
            action = order.Action; target = order.Target;
            return valid;
        }
        public bool TryGetExecutionOrder(out CpcOrder order)
        {
            order = default;
            if (!isActiveAndEnabled || Companion.HitPoints == null || !Companion.HitPoints.IsAlive) return false;
            if (HasPlayerDirective)
            {
                if (CanProtectWhileFollowing && !MustRejoinPlayer() && IsAutonomousOrderValid() &&
                    IsProtectionAction(_autonomousOrder.Action) && IsProtectionTarget(_autonomousOrder.Target, _autonomousOrder.Action))
                { order = _autonomousOrder; return true; }
                if (!NeedsTarget(_playerOrder.Action) || IsValidTarget(_playerOrder.Target))
                { order = _playerOrder; return IsAllowed(order.Action); }
                Debug.Log("[CPC/Directive] target completed or no longer observable; directive ended", this);
                _playerOrder = default;
            }
            if (!IsAutonomousOrderValid()) return false;
            order = _autonomousOrder;
            return true;
        }
        private bool IsAutonomousOrderValid() => Time.unscaledTime < _autonomousOrder.Expires && IsAllowed(_autonomousOrder.Action) &&
            (!NeedsTarget(_autonomousOrder.Action) || IsValidTarget(_autonomousOrder.Target));

        private bool MustRejoinPlayer()
        {
            float distance = Vector3.Distance(Companion.transform.position, Companion.Player.transform.position);
            if (distance > Mathf.Min(FollowRejoinDistance, MaxPlayerDistance))
            {
                if (!_rejoining) Debug.Log("[CPC/Follow] disengage and rejoin player", this);
                _rejoining = true;
                _autonomousOrder = default;
            }
            if (_rejoining && distance <= Companion.FollowDistance + Companion.FollowResumeMargin) _rejoining = false;
            return _rejoining;
        }
        private bool IsProtectionTarget(SurvivalZombie target, CpcAction action)
        {
            if (!_sensor.TryObserve(target, out ObservedZombie observed)) return false;
            float distance = Vector3.Distance(observed.Position, Companion.Player.transform.position);
            return distance <= FollowCombatRadius || action == CpcAction.Intercept && observed.Threat.Target == Companion.Player.Health;
        }
        public bool TryGetFollowProtectionThreat(out SurvivalZombie target)
        {
            target = null;
            if (!CanProtectWhileFollowing || MustRejoinPlayer()) return false;
            float best = float.MinValue;
            for (int i = 0; i < _sensor.Count; i++)
            {
                ObservedZombie observed = _sensor.Get(i);
                if (!IsValidTarget(observed.Source) || !IsProtectionTarget(observed.Source, CpcAction.Attack)) continue;
                float score = _sensor.Score(observed);
                if (score <= best) continue;
                best = score; target = observed.Source;
            }
            return target != null;
        }
        private static bool IsProtectionAction(CpcAction action) => action == CpcAction.Guard || action == CpcAction.Taunt ||
            action == CpcAction.Attack || action == CpcAction.Intercept || action == CpcAction.Evade || action == CpcAction.Counterattack;
        public bool IsAllowed(CpcAction action) => action != CpcAction.None && (AllowedActions & action) == action;
        private bool IsValidTarget(SurvivalZombie target) => target != null && target.HitPoints != null &&
            target.HitPoints.IsAlive && _sensor.TryObserve(target, out _);
        public void Complete(int serial)
        {
            if (_playerOrder.Serial == serial && HasPlayerDirective) _playerOrder = default;
            if (_autonomousOrder.Serial == serial) _autonomousOrder = default;
        }
        public void RejectExecution(CpcOrder order, string reason)
        {
            LastRejection = reason;
            RejectedDecisionCount++;
            Complete(order.Serial);
            Debug.Log($"[CPC/Reject/{order.Origin}] {order.Action}: {reason}", this);
            if (order.Origin == CpcDecisionOrigin.PlayerCommand) Companion.Llm.PublishFeedback(reason);
        }
        private RequestFrame Capture(bool chat)
        {
            int count = Mathf.Min(8, _sensor.Count);
            var enemies = new ObservedZombie[count];
            int next = 0;
            for (int i = 0; i < _sensor.Count && next < count; i++)
                if (_sensor.Get(i).Source == _sensor.Priority) enemies[next++] = _sensor.Get(i);
            for (int i = 0; i < _sensor.Count && next < count; i++)
                if (_sensor.Get(i).Source != _sensor.Priority) enemies[next++] = _sensor.Get(i);
            return new RequestFrame { Enemies = enemies, Chat = chat, Generation = _chatGeneration,
                Started = Time.unscaledTime, PriorityId = _sensor.Priority != null ? _sensor.Priority.TacticalId : "" };
        }
        private void Request(string context, string fallback, bool chat)
        {
            RequestFrame frame = Capture(chat);
            var prompt = new StringBuilder(3500);
            prompt.AppendLine("Unity 전술 JSON: action,targetId,speech,intent. 선택 필드: followMode(protect/passive), targetType(좀비 enum), targetPolicy(priority/nearest), goal(combat/protect/movement/conversation). speech는 한국어.");
            prompt.Append("입력 종류=").AppendLine(chat ? "플레이어 지시/질문" : "상황 변화");
            prompt.Append("플레이어 발언/사건: ").AppendLine(context.Substring(0, Mathf.Min(context.Length, 800)));
            prompt.AppendLine("자연어의 의도와 유형을 해석한다. 좌표나 코드는 출력하지 않는다. 질문은 question/none으로 기존 지시를 유지한다. 전투 부탁에 '저놈'처럼 대상이 생략되어도 관측 후보가 있으면 되묻지 말고 플레이어를 위협하는 특수 좀비, 플레이어 가까운 좀비 순으로 스스로 고른다. targetId=auto로 Unity에 우선순위 선택을 맡길 수도 있다.");
            prompt.AppendLine("follow=추종, hold=현재 위치 대기, guard=대상 경계/방패, taunt=도발, attack=대상 처치, retreat=후퇴, intercept=플레이어를 겨눈 침 가로막기, evade=위험 범위 이탈, counterattack=Recovering 단계의 빈틈에 근접 반격.");
            prompt.AppendLine("대상 행동은 관측 ID 또는 auto를 사용한다. 유형을 지정한 부탁은 targetType에 Spitter/Crawler/Burrower/Normal/Boomer/Jumper를 적는다. '가까운 놈'은 targetPolicy=nearest, 나머지는 priority. evade의 대상은 피할 공격을 낸 좀비다.");
            prompt.AppendLine("Spitter=침 뱉는 좀비, Crawler=기어오는 좀비, Burrower=버로우. 침을 막으라는 부탁은 intercept, 특정 유형부터 잡으라는 부탁은 해당 유형 ID의 attack, 플레이어에게 붙은 Crawler는 taunt 또는 attack을 고려한다.");
            prompt.AppendLine("Burrower의 Emerging은 지면 범위 공격이며 intercept로 막을 수 없다. 범위 안이면 evade, 흔적으로 접근하여 해체할 수 있으면 attack을 고려한다. Crawler의 Recovering은 counterattack 기회다. Spitter의 player 대상 Aiming/Projectile만 intercept 후보이다. 각 적의 feasible 목록에 있는 행동만 선택한다.");
            prompt.AppendLine("대기/추종은 새 지시까지 지속한다. 대상 지시는 그 대상에만 적용한다. 새 플레이어 지시는 이전 지시를 대체한다. 질문/경고는 none. 수락 대사를 쓰면 일치하는 action과 관측 목록의 targetId를 반드시 선택한다.");
            prompt.Append("실제 CPC 행동: ").AppendLine(Companion.CurrentActionDescription);
            prompt.Append("유지 중인 플레이어 지시: ").Append(_playerOrder.Action).Append(" target=")
                .AppendLine(_playerOrder.Target != null ? _playerOrder.Target.TacticalId : "");
            prompt.Append("추종 모드: ").AppendLine(PassiveFollow ? "passive(일반 교전 금지)" : "protect(따라오며 주변 보호)");
            prompt.AppendLine("일반 '따라와'는 followMode=protect다. 추종 지시를 유지하면서 플레이어 가까운 위협에는 guard/taunt/attack/intercept/evade/counterattack을 선택할 수 있다. 교전 후 추종으로 돌아간다. '싸우지 말고 나만 따라와'처럼 명시한 경우에만 passive다. hold나 지정 대상 명령은 일반 자율 판단으로 덮어쓰지 않는다.");
            prompt.Append("성향: ").AppendLine(TacticalPrompt);
            prompt.Append("허용: none");
            foreach (CpcAction action in Enum.GetValues(typeof(CpcAction))) if (IsAllowed(action)) prompt.Append(',').Append(action.ToString().ToLowerInvariant());
            prompt.AppendLine();
            prompt.Append("player pos="); AppendVector(prompt, Companion.Player.transform.position);
            prompt.Append(" HP=").Append(Companion.Player.Health.Current).Append('/').AppendLine(Companion.Player.Health.MaxHealth.ToString());
            prompt.Append("CPC pos="); AppendVector(prompt, Companion.transform.position);
            prompt.Append(" HP=").Append(Companion.HitPoints.Current).Append('/').AppendLine(Companion.HitPoints.MaxHealth.ToString());
            prompt.Append("priority=").AppendLine(frame.PriorityId);
            foreach (ObservedZombie enemy in frame.Enemies)
            {
                ZombieThreat threat = enemy.Threat;
                prompt.Append(enemy.Source.TacticalId).Append(" type=").Append(enemy.Source.Kind)
                    .Append(" phase=").Append(threat.Stage).Append(" revision=").Append(threat.Revision)
                    .Append(" target=").Append(threat.Target == Companion.Player.Health ? "player" : threat.Target == Companion.HitPoints ? "CPC" : "none")
                    .Append(" remaining=").Append(threat.Remaining.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" cueOnly=").Append(enemy.CueOnly).Append(" observedPos=");
                AppendVector(prompt, enemy.Position);
                prompt.Append(" impact="); AppendVector(prompt, threat.Impact);
                prompt.Append(" direction="); AppendVector(prompt, threat.Impact - (enemy.CueOnly ? enemy.Position : threat.Origin));
                prompt.Append(" radius=").Append(threat.Radius.ToString("F2", CultureInfo.InvariantCulture));
                prompt.Append(" playerDistance=").Append(Vector3.Distance(enemy.Position, Companion.Player.transform.position).ToString("F2", CultureInfo.InvariantCulture));
                if (!enemy.CueOnly) prompt.Append(" HP=").Append(enemy.Source.HitPoints.Current);
                prompt.Append(" feasible=");
                foreach (CpcAction action in Enum.GetValues(typeof(CpcAction)))
                    if (NeedsTarget(action) && IsAllowed(action) && _motor.Validate(action, enemy.Source, chat, out _))
                        prompt.Append(action.ToString().ToLowerInvariant()).Append(',');
                prompt.AppendLine();
            }
            prompt.AppendLine("출력 예: {\"action\":\"hold\",\"targetId\":\"\",\"speech\":\"여기서 기다릴게.\",\"intent\":\"command\"}");
            if (!string.IsNullOrEmpty(frame.PriorityId))
                prompt.Append("회피 형식 예: {\"action\":\"evade\",\"targetId\":\"").Append(frame.PriorityId)
                    .AppendLine("\",\"speech\":\"땅이 흔들린다. 위험 범위에서 벗어날게.\",\"intent\":\"command\"}");
            if (chat)
            {
                prompt.AppendLine("플레이어의 질문을 새로운 명령으로 바꾸지 않는다. 예: '어떤 놈이 제일 위험해?'는 위험한 적을 설명하는 question/none이다. '지금 뭐 하고 있어?'는 현재 수행 중인 행동을 설명하는 question/none이다. 질문에 답하며 지시를 다시 수락하거나 새로운 intercept/hold를 고르지 않는다.");
                prompt.AppendLine("'막아 줘', '처리해 줘'는 goal=protect/combat의 command다. 여러 후보여도 적의 위치나 유형을 다시 묻지 말고 자동 선택하여 행동한다. 위치/유형을 이미 알 수 있는데 clarify를 반환하지 않는다. 관측 후보가 없거나 이동 지시 자체를 안전하게 해석할 수 없을 때만 확인한다.");
                prompt.Append("지금 답할 최신 플레이어 발언: ").AppendLine(context);
                prompt.AppendLine("먼저 이 발언의 intent를 판단한다. question/clarify면 action은 none, targetId는 빈 문자열이고 speech에 답만 작성한다.");
            }
            LastPrompt = prompt.ToString(); RequestsSent++;
            Debug.Log($"[CPC/Request/{(chat ? "PlayerCommand" : "SituationLlm")}] generation={frame.Generation} observations={frame.Enemies.Length}", this);
            Companion.Llm.Enqueue(LastPrompt, fallback, chat, reply => DecodeFrame(reply, frame, false), context);
        }
        private string DecodeFrame(string reply, RequestFrame frame, bool preserve)
        {
            Decision decision;
            try { decision = JsonUtility.FromJson<Decision>(reply); }
            catch (Exception) { return Reject(frame, "전술 응답 형식이 잘못되어 행동을 유지할게."); }
            if (decision == null || decision.action == null || decision.speech == null) return Reject(frame, "전술 응답에 필요한 항목이 없어.");
            Debug.Log($"[CPC/Proposed/{(frame.Chat ? "PlayerCommand" : "SituationLlm")}] {decision.action} {decision.targetId} intent={decision.intent}", this);
            if (frame.Generation != _chatGeneration || Time.unscaledTime - frame.Started > (frame.Chat ? MaxChatResponseAge : MaxEventResponseAge))
                return Reject(frame, "오래된 전술 응답은 실행하지 않을게.");
            string name = decision.action.Trim();
            if (!Enum.TryParse(name, true, out CpcAction action) || name.IndexOf(',') >= 0 ||
                !Enum.IsDefined(typeof(CpcAction), action)) return Reject(frame, "알 수 없는 행동이라 실행하지 않을게.");
            if (preserve) return decision.speech;
            if (decision.intent == "question") return action == CpcAction.None ? decision.speech : Companion.CurrentActionDescription + ".";
            bool delegatedCombat = decision.intent == "clarify" && frame.Enemies.Length > 0 &&
                (decision.goal == "combat" || decision.goal == "protect");
            if (delegatedCombat)
            {
                action = decision.goal == "protect" ? CpcAction.Guard : CpcAction.Attack;
                decision.targetId = "auto";
            }
            else if (decision.intent == "clarify") return frame.Enemies.Length == 0 ? "지금은 관측할 수 있는 적이 없어." : decision.speech;
            if (action == CpcAction.None)
                return decision.intent == "command" ? Reject(frame, "실행할 행동을 정하지 못했어. 조금 더 구체적으로 말해 줘.") : decision.speech;
            if (!IsAllowed(action)) return Reject(frame, "그 행동은 지금 허용되지 않아.");
            if (!frame.Chat && HasPlayerDirective && (!CanProtectWhileFollowing || !IsProtectionAction(action)))
                return Reject(frame, "위협은 확인했어. 네 지시는 유지할게.");
            SurvivalZombie target = null;
            ObservedZombie captured = default;
            bool autoTarget = false;
            if (NeedsTarget(action))
            {
                if (!TryResolveTarget(decision, frame, action, out captured, out autoTarget))
                    return Reject(frame, "지금 관측한 적 중에는 그 행동을 안전하게 할 대상이 없어.");
                target = captured.Source;
                if (!frame.Chat && IsTimed(action) && target.Threat.Revision != captured.Threat.Revision)
                    return Reject(frame, "공격 단계가 바뀌어 이전 전술은 실행하지 않을게.");
                if (!frame.Chat && CanProtectWhileFollowing && (MustRejoinPlayer() || !IsProtectionTarget(target, action)))
                    return Reject(frame, "플레이어와 너무 멀어져서 교전보다 추종을 우선할게.");
            }
            if (!_motor.Validate(action, target, frame.Chat, out string reason)) return Reject(frame, reason);
            var order = new CpcOrder { Serial = ++_serial, Action = action, Target = target,
                ThreatRevision = target != null ? target.Threat.Revision : 0, HoldPosition = Companion.transform.position,
                Expires = Time.unscaledTime + DecisionDuration,
                Origin = frame.Chat ? CpcDecisionOrigin.PlayerCommand : CpcDecisionOrigin.SituationLlm };
            if (frame.Chat)
            {
                _playerOrder = order; _autonomousOrder = default;
                _passiveFollow = action == CpcAction.Follow && string.Equals(decision.followMode, "passive", StringComparison.OrdinalIgnoreCase);
                _rejoining = false;
                if (CanProtectWhileFollowing) { _sentSignal = int.MinValue; _nextEvent = 0f; }
            }
            else _autonomousOrder = order;
            if (action == CpcAction.Hold && !Companion.IsRetreating) Companion.StopForPlayerHold();
            AcceptedDecisionCount++; LastSelectedAction = action; LastDecisionOrigin = order.Origin; LastSelectedTarget = target;
            Debug.Log($"[CPC/Selected/{order.Origin}] {action} {(target != null ? target.TacticalId : "")}", this);
            string speech = autoTarget || delegatedCombat ? DescribeAutomaticChoice(action, target) : decision.speech;
            return Companion.IsRetreating ? "지금은 긴급 후퇴 중이야. 안전해지면 새 지시를 따를게." : speech;
        }
        private bool TryResolveTarget(Decision decision, RequestFrame frame, CpcAction action,
            out ObservedZombie selected, out bool automatic)
        {
            selected = default;
            automatic = false;
            bool typed = !string.IsNullOrWhiteSpace(decision.targetType);
            ZombieKind kind = default;
            if (typed && (!Enum.TryParse(decision.targetType, true, out kind) || !Enum.IsDefined(typeof(ZombieKind), kind))) return false;
            foreach (ObservedZombie enemy in frame.Enemies)
            {
                if (enemy.Source == null || enemy.Source.TacticalId != decision.targetId) continue;
                if (!typed || enemy.Source.Kind == kind)
                { selected = enemy; return IsValidTarget(enemy.Source); }
            }
            automatic = true;
            float best = float.MinValue;
            foreach (ObservedZombie enemy in frame.Enemies)
            {
                if (enemy.Source == null || typed && enemy.Source.Kind != kind || !IsValidTarget(enemy.Source) ||
                    !_motor.Validate(action, enemy.Source, frame.Chat, out _)) continue;
                float score = decision.targetPolicy == "nearest" ? -Vector3.Distance(enemy.Position, Companion.Player.transform.position) : _sensor.Score(enemy);
                if (score <= best) continue;
                best = score; selected = enemy;
            }
            if (selected.Source != null) Debug.Log($"[CPC/Target] auto {selected.Source.TacticalId} {selected.Source.Kind} policy={decision.targetPolicy}", this);
            return selected.Source != null;
        }
        private static string DescribeAutomaticChoice(CpcAction action, SurvivalZombie target)
        {
            string type = target == null ? "위협" : target.Kind switch
            {
                ZombieKind.Spitter => "침 뱉는 좀비", ZombieKind.Crawler => "기어오는 좀비",
                ZombieKind.Burrower => "버로우 흔적", ZombieKind.Boomer => "부머", ZombieKind.Jumper => "점프 좀비", _ => "가까운 좀비"
            };
            return action switch
            {
                CpcAction.Attack => type + "부터 처리할게.", CpcAction.Taunt => type + "의 주의를 끌게.",
                CpcAction.Intercept => type + "의 침을 막을게.", CpcAction.Evade => type + "의 위험 범위에서 벗어날게.",
                CpcAction.Counterattack => type + "의 빈틈에 반격할게.", _ => type + "를 경계하며 보호할게."
            };
        }
        private string Reject(RequestFrame frame, string reason)
        {
            LastRejection = reason; RejectedDecisionCount++;
            Debug.Log($"[CPC/Reject/{(frame.Chat ? "PlayerCommand" : "SituationLlm")}] {reason}", this);
            return frame.Chat ? reason : string.Empty;
        }
        // 기존 음성 회귀 검사에서 동일한 검증기를 사용한다.
        private string Decode(string reply, bool[] ignored, int generation, float started, bool chat, bool preserveOrder = false)
        {
            _sensor.Refresh(true);
            RequestFrame frame = Capture(chat); frame.Generation = generation; frame.Started = started;
            return DecodeFrame(reply, frame, preserveOrder);
        }
        private static bool NeedsTarget(CpcAction action) => action == CpcAction.Guard || action == CpcAction.Taunt ||
            action == CpcAction.Attack || action == CpcAction.Intercept || action == CpcAction.Evade || action == CpcAction.Counterattack;
        private static bool IsTimed(CpcAction action) => action == CpcAction.Intercept || action == CpcAction.Evade || action == CpcAction.Counterattack;
        private static void AppendVector(StringBuilder builder, Vector3 point)
        {
            builder.Append('(').Append(point.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.z.ToString("F2", CultureInfo.InvariantCulture)).Append(')');
        }
    }
}
