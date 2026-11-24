#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace CpcPrototype.Editor
{
    public sealed class CpcExtendedTacticsChecks : MonoBehaviour
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Result { get; private set; } = "Not run";
        private readonly List<string> _results = new List<string>();
        private readonly List<string> _logs = new List<string>();
        private bool _realModel;
        private SurvivalPlayer _player;
        private ShieldCompanion _companion;
        private CpcTactics _tactics;
        private CpcTacticalMotor _motor;
        private CpcLlmSettings _original, _settings;
        private SurvivalZombie[] _zombies;
        private Vector3 _startPlayer, _startCompanion;
        private int _accepted, _executions;
        private CpcAction _expected;

        [MenuItem("Tools/CPC/Run Extended Tactics Checks (Play Mode)")]
        public static void RunDeterministic() { Run(false); }
        [MenuItem("Tools/CPC/Run Real Ollama Tactics Checks (Play Mode)")]
        public static void RunRealModel() { Run(true); }
        private static void Run(bool realModel)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode in KayKit_TacticsTest first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/KayKit_TacticsTest.unity")
                throw new InvalidOperationException("Use the isolated KayKit_TacticsTest scene.");
            if (FindFirstObjectByType<CpcExtendedTacticsChecks>() != null) throw new InvalidOperationException("Checks already running.");
            var runner = new GameObject("Extended Tactics Checks").AddComponent<CpcExtendedTacticsChecks>();
            runner._realModel = realModel;
            runner.StartCoroutine(runner.RunChecks());
        }
        private IEnumerator RunChecks()
        {
            Result = "Running";
            Application.logMessageReceived += CaptureLog;
            IEnumerator checks = Checks();
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = checks.MoveNext(); if (more) current = checks.Current; }
                catch (Exception exception) { Result = "FAIL: " + exception; break; }
                if (!more) { Result = "PASS"; break; }
                yield return current;
            }
            Application.logMessageReceived -= CaptureLog;
            if (_companion != null) _companion.Llm.Configure(_original);
            if (_settings != null) Destroy(_settings);
            Result += "\n" + string.Join("\n", _results);
            System.IO.Directory.CreateDirectory("Tools/Tactics");
            string file = _realModel ? "Tools/Tactics/RealModelChecks.txt" : "Tools/Tactics/UnityChecks.txt";
            System.IO.File.WriteAllText(file, Result + "\n\n" + string.Join("\n", _logs));
            Debug.Log("[CPC/TacticsChecks] " + Result);
            Destroy(gameObject);
        }
        private IEnumerator Checks()
        {
            _player = FindFirstObjectByType<SurvivalPlayer>();
            _companion = _player.Companion; _tactics = _companion.Tactics; _motor = _tactics.Motor;
            _startPlayer = _player.transform.position; _startCompanion = _companion.transform.position;
            _original = _companion.Llm.Settings;
            _settings = Instantiate(_original);
            _settings.EnableLlm = _realModel;
            _settings.TimeoutSeconds = 150;
            _companion.Llm.enabled = false; _companion.Llm.enabled = true;
            _companion.Llm.Configure(_settings);
            _tactics.OnThreat = _tactics.OnDamage = false;
            _tactics.UseAutonomousFallback = false;
            _tactics.MaxEventResponseAge = _realModel ? 150f : 12f;
            _tactics.MaxChatResponseAge = _realModel ? 150f : 45f;
            _tactics.EventInterval = .1f;
            _zombies = FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            ResetActors();
            yield return null;
            Check(_companion.GetComponent<NavMeshAgent>().isOnNavMesh, "scene wiring and CPC NavMesh");
            if (_realModel)
            {
                IEnumerator model = ModelChecks();
                while (model.MoveNext()) yield return model.Current;
                yield break;
            }
            SubmitUi("여기서 기다려 줘"); yield return null; yield return null; yield return null;
            Decode(CpcAction.Hold);
            Vector3 held = _companion.transform.position;
            WarpPlayer(_startPlayer + Vector3.right * 1.2f);
            yield return new WaitForSecondsRealtime(6.5f);
            Check(_tactics.IsHolding && Vector3.Distance(held, _companion.transform.position) < .08f, "chat path -> persistent Hold survives six seconds");
            Decode(CpcAction.None, null, "question");
            Check(_tactics.IsHolding, "question keeps player directive");
            SubmitUi("이제 다시 따라와"); yield return null; yield return null; yield return null;
            Decode(CpcAction.Follow); yield return null; yield return null;
            Check(_tactics.PlayerAction == CpcAction.Follow && !_companion.GetComponent<NavMeshAgent>().isStopped, "Follow replaces Hold and resumes navigation");
            _tactics.ReleasePlayerMovementOrder(); ResetActors();

            SurvivalZombie spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            spit.ReportThreat(ZombieAttackStage.Aiming, _player.Health, spit.AimPoint, _player.transform.position, .1f, 10f);
            _tactics.Sensor.Refresh(true);
            SubmitUi("내 앞에서 침 좀 막아줘"); yield return null; yield return null; yield return null;
            string accepted = Decode(CpcAction.Intercept, spit);
            yield return new WaitForSecondsRealtime(.8f);
            Check(_tactics.PlayerAction == CpcAction.Intercept && _companion.IsBlocking && _motor.CurrentAction == CpcAction.Intercept,
                "intercept intent -> planned movement and shield");
            _tactics.ReleasePlayerMovementOrder();

            _companion.enabled = false;
            _companion.StopForPlayerHold();
            _companion.transform.rotation = Quaternion.LookRotation(Vector3.right);
            Vector3 origin = _companion.transform.position + Vector3.right * .6f + Vector3.up * .2f;
            _companion.SetTacticalBlock(true, origin);
            float beforeBlock = _companion.HitPoints.Current;
            SpitProjectile projectile = SpitProjectile.Launch(origin, origin - Vector3.right * 2f, spit.gameObject, 20f, 3f, 3f, .05f, 3f, 0f);
            yield return new WaitForSecondsRealtime(.4f);
            Check(projectile == null && Mathf.Abs(beforeBlock - _companion.HitPoints.Current - 4f) < .01f,
                "physical projectile/trigger interception applies existing 80% shield reduction");
            _companion.enabled = true;

            _tactics.ReleasePlayerMovementOrder(); ResetActors();
            SurvivalZombie crawler = Activate(ZombieKind.Crawler, _companion.transform.position + Vector3.right * .35f);
            crawler.ReportThreat(ZombieAttackStage.Recovering, _player.Health, crawler.AimPoint, _player.transform.position, 0f, 5f);
            _tactics.Sensor.Refresh(true);
            float beforeMelee = crawler.HitPoints.Current;
            Decode(CpcAction.Counterattack, crawler);
            yield return new WaitForSecondsRealtime(_companion.MeleeMotionTime + .2f);
            Check(crawler.HitPoints.Current <= beforeMelee - _companion.MeleeDamage, "counterattack reuses existing timed melee damage");
            typeof(ShieldCompanion).GetField("_nextTaunt", PRIVATE).SetValue(_companion, 0f);
            Check(_companion.TryBeginTaunt(crawler) && crawler.TryGetTauntTarget(out Health taunted) && taunted == _companion.HitPoints,
                "existing taunt still redirects zombie target");

            _tactics.ReleasePlayerMovementOrder(); ResetActors();
            SurvivalZombie burrow = Activate(ZombieKind.Burrower, _startPlayer + Vector3.right * .4f);
            _tactics.Sensor.Refresh(true);
            Check(_tactics.Sensor.Count == 0, "dormant hidden burrow omitted from sensor");
            var burrowAbility = burrow.GetComponent<BurrowerZombieAbility>();
            burrowAbility.enabled = true; burrowAbility.TriggerRange = .55f; burrowAbility.EmergeWarning = 10f;
            typeof(BurrowerZombieAbility).GetField("_nextSkill", PRIVATE).SetValue(burrowAbility, Time.time);
            yield return null; yield return null;
            _tactics.Sensor.Refresh(true);
            Check(_tactics.Sensor.TryObserve(burrow, out ObservedZombie observed) && observed.CueOnly &&
                observed.Position == burrow.Threat.CuePosition && observed.Position != burrow.transform.position,
                "native emergence warning exposes cue position, not hidden body position");
            ResetActors();
            spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            spit.ReportThreat(ZombieAttackStage.Exploding, _companion.HitPoints, _companion.transform.position,
                _companion.transform.position, .25f, 8f, true, _companion.transform.position);
            _tactics.Sensor.Refresh(true);
            Decode(CpcAction.Evade, spit);
            Vector3 danger = _companion.transform.position;
            yield return new WaitForSecondsRealtime(.8f);
            Check(Vector3.Distance(danger, _companion.transform.position) > .4f, "evade leaves danger radius using NavMesh");

            _tactics.ReleasePlayerMovementOrder(); ResetActors();
            spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            spit.ReportThreat(ZombieAttackStage.Aiming, _player.Health, spit.AimPoint, _player.transform.position, .1f, .8f);
            _tactics.Sensor.Refresh(true);
            Check(_motor.TryImmediateSafety(out CpcOrder safety) && safety.Origin == CpcDecisionOrigin.Safety, "short warning triggers safety without waiting for LLM");
            _motor.Tick(safety);
            Check(_motor.CurrentOrigin == CpcDecisionOrigin.Safety, "safety execution logged separately from model choice");

            _tactics.ReleasePlayerMovementOrder(); ResetActors();
            Decode(CpcAction.Hold);
            _companion.HitPoints.TakeDamage(150f, _player.gameObject); yield return null; yield return null;
            Check(_companion.IsRetreating && _tactics.IsHolding, "low-health retreat suspends rather than deletes player Hold");
            _companion.HitPoints.Heal(200f); yield return new WaitForSecondsRealtime(1f);
            Check(!_companion.IsRetreating && _tactics.IsHolding, "valid player directive resumes after recovery");

            string invalid = RawDecode("not json");
            Check(!invalid.Contains("공격할게") && _tactics.IsHolding, "malformed JSON rejected without changing directive");
            int rejected = _tactics.RejectedDecisionCount;
            RawDecode("{\"action\":\"follow\",\"targetId\":\"\",\"speech\":\"따라갈게\"}", Time.unscaledTime - 100f);
            Check(_tactics.RejectedDecisionCount > rejected && _tactics.IsHolding, "expired response rejected");
            ResetActors(); spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            spit.ReportThreat(ZombieAttackStage.Aiming, _player.Health, spit.AimPoint, _player.transform.position, .1f, 10f);
            _tactics.Sensor.Refresh(true);
            object frame = typeof(CpcTactics).GetMethod("Capture", PRIVATE).Invoke(_tactics, new object[] { false });
            spit.ReportThreat(ZombieAttackStage.Recovering, _player.Health, spit.AimPoint, _player.transform.position, 0f, 3f);
            string stale = (string)typeof(CpcTactics).GetMethod("DecodeFrame", PRIVATE).Invoke(_tactics,
                new object[] { Json(CpcAction.Intercept, spit), frame, false });
            Check(stale == string.Empty, "changed attack phase invalidates in-flight model response");
            spit.HitPoints.TakeDamage(10000f, _player.gameObject);
            string dead = Decode(CpcAction.Attack, spit);
            Check(dead != "공격할게.", "dead target rejected");

            ResetActors(); spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            _companion.GetComponent<NavMeshAgent>().enabled = false;
            string noPath = Decode(CpcAction.Attack, spit);
            Check(noPath != "공격할게.", "missing navigation rejected before acceptance speech");
            _companion.GetComponent<NavMeshAgent>().enabled = true;
            _companion.GetComponent<NavMeshAgent>().Warp(_startCompanion);
            yield return null; yield return null;
            _tactics.ReleasePlayerMovementOrder();
            _tactics.OnThreat = true;
            _tactics.Sensor.Refresh(true); _tactics.Observe(spit);
            yield return new WaitForSecondsRealtime(.2f);
            int requests = _tactics.RequestsSent;
            for (int i = 0; i < 20; i++) _tactics.Observe(spit);
            Check(_tactics.RequestsSent == requests, "unchanged signal does not flood model requests");
            _tactics.OnThreat = false;
            _settings.EnableLlm = true; _settings.Endpoint = "http://127.0.0.1:1/api/chat";
            _settings.TimeoutSeconds = 5;
            Decode(CpcAction.Hold);
            int frameCount = Time.frameCount;
            SubmitUi("어떤 놈이 제일 위험해?");
            yield return null; yield return null; yield return null;
            float failureDeadline = Time.realtimeSinceStartup + 8f;
            while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < failureDeadline) yield return null;
            Check(Time.frameCount > frameCount && _tactics.IsHolding && !_companion.Llm.HasPendingDialogue,
                "local model connection failure keeps game ticking and existing instruction");
        }

        private IEnumerator ModelChecks()
        {
            _motor.ImmediateResponseSeconds = .001f;
            foreach (ZombieKind kind in new[] { ZombieKind.Spitter, ZombieKind.Burrower, ZombieKind.Crawler })
            {
                ResetActors();
                SurvivalZombie zombie = Activate(kind, _startPlayer + Vector3.right * (kind == ZombieKind.Spitter ? 1f : .4f));
                if (kind == ZombieKind.Spitter)
                {
                    var ability = zombie.GetComponent<SpitterZombieAbility>(); ability.enabled = true;
                    ability.AimDuration = 150f; ability.SpitRange = 5f;
                    typeof(SpitterZombieAbility).GetField("_nextSkill", PRIVATE).SetValue(ability, Time.time);
                }
                else if (kind == ZombieKind.Burrower)
                {
                    var ability = zombie.GetComponent<BurrowerZombieAbility>(); ability.enabled = true;
                    ability.TriggerRange = .55f; ability.EmergeWarning = 150f;
                    typeof(BurrowerZombieAbility).GetField("_nextSkill", PRIVATE).SetValue(ability, Time.time);
                }
                else
                {
                    zombie.AttackRange = .5f; zombie.AttackWindup = .1f; zombie.AttackCooldown = 150f;
                }
                yield return new WaitForSecondsRealtime(.3f);
                _tactics.Sensor.Refresh(true);
                _accepted = _tactics.AcceptedDecisionCount; _executions = _motor.ExecutionCount;
                _tactics.OnThreat = true;
                _tactics.Observe(zombie);
                float deadline = Time.realtimeSinceStartup + 180f;
                while (_tactics.AcceptedDecisionCount == _accepted && Time.realtimeSinceStartup < deadline && _companion.Llm.IsBusy) yield return null;
                _tactics.OnThreat = false;
                yield return new WaitForSecondsRealtime(.5f);
                Check(_tactics.AcceptedDecisionCount > _accepted && _motor.ExecutionCount > _executions &&
                    _tactics.LastDecisionOrigin == CpcDecisionOrigin.SituationLlm,
                    "REAL " + kind + " native phase -> local model " + _tactics.LastSelectedAction + " -> Unity execution; " + _companion.Llm.LastReply);
            }
            ResetActors();
            foreach (var pair in new[] { ("여기서 기다려 줘", CpcAction.Hold), ("이제 다시 따라와", CpcAction.Follow) })
            {
                SubmitUi(pair.Item1); _expected = pair.Item2;
                yield return null; yield return null; yield return null;
                float until = Time.realtimeSinceStartup + 180f;
                while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < until) yield return null;
                Check(_tactics.PlayerAction == _expected, "REAL chat UI " + pair.Item1 + " -> " + _expected + "; " + _companion.Llm.LastReply);
                if (_expected == CpcAction.Hold)
                {
                    yield return new WaitForSecondsRealtime(6.5f);
                    Check(_tactics.IsHolding && _companion.GetComponent<NavMeshAgent>().isStopped, "REAL model Hold persists beyond six seconds");
                    int decisions = _tactics.AcceptedDecisionCount;
                    SubmitUi("지금 뭐 하고 있어?"); yield return null; yield return null; yield return null;
                    float questionUntil = Time.realtimeSinceStartup + 180f;
                    while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < questionUntil) yield return null;
                    Check(_tactics.IsHolding && _tactics.AcceptedDecisionCount == decisions,
                        "REAL status question keeps exact Hold directive; " + _companion.Llm.LastReply);
                }
            }
            SurvivalZombie spit = Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            spit.ReportThreat(ZombieAttackStage.Aiming, _player.Health, spit.AimPoint, _player.transform.position, .1f, 75f);
            foreach (var pair in new[] { ("저기 침 뱉는 놈부터 잡아", CpcAction.Attack), ("내 앞에서 침 좀 막아줘", CpcAction.Intercept) })
            {
                SubmitUi(pair.Item1); yield return null; yield return null; yield return null;
                float until = Time.realtimeSinceStartup + 180f;
                while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < until) yield return null;
                Check(_tactics.PlayerAction == pair.Item2, "REAL targeted chat " + pair.Item1 + " -> " + _tactics.PlayerAction + "; " + _companion.Llm.LastReply);
            }
            CpcAction preserved = _tactics.PlayerAction;
            int beforeQuestion = _tactics.AcceptedDecisionCount;
            SubmitUi("어떤 놈이 제일 위험해?"); yield return null; yield return null; yield return null;
            float stop = Time.realtimeSinceStartup + 180f;
            while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < stop) yield return null;
            Check(_tactics.PlayerAction == preserved && _tactics.AcceptedDecisionCount == beforeQuestion,
                "REAL danger question keeps exact instruction; " + _companion.Llm.LastReply);
            // 직전 대화에서 이미 Spitter를 지목했으므로, 지목 단서가 없는 새 대화로 모호성을 검사한다.
            ResetActors();
            ((System.Collections.IList)typeof(LocalLlmClient).GetField("_history", PRIVATE).GetValue(_companion.Llm)).Clear();
            Activate(ZombieKind.Spitter, _startPlayer + Vector3.right * 1f);
            var nearbyCrawler = Activate(ZombieKind.Crawler, _startPlayer + new Vector3(.7f, 0f, .4f));
            preserved = _tactics.PlayerAction;
            int beforeAmbiguous = _tactics.AcceptedDecisionCount;
            SubmitUi("저놈부터 잡아"); yield return null; yield return null; yield return null;
            stop = Time.realtimeSinceStartup + 180f;
            while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < stop) yield return null;
            Check(_tactics.AcceptedDecisionCount > beforeAmbiguous && _tactics.LastSelectedTarget != null,
                "REAL underspecified combat request selects an observed target automatically; " + _companion.Llm.LastReply);
            int beforeCrawler = _tactics.AcceptedDecisionCount;
            SubmitUi("기어오는 놈 좀 떼어줘"); yield return null; yield return null; yield return null;
            stop = Time.realtimeSinceStartup + 180f;
            while (_companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < stop) yield return null;
            Check(_tactics.AcceptedDecisionCount > beforeCrawler &&
                (_tactics.LastSelectedAction == CpcAction.Taunt || _tactics.LastSelectedAction == CpcAction.Attack),
                "REAL Crawler chat selects taunt/attack; " + _companion.Llm.LastReply);
        }
        private void ResetActors()
        {
            _tactics.OnThreat = false;
            _tactics.ReleasePlayerMovementOrder(); _motor.Clear();
            _companion.CancelPendingMelee();
            _player.Health.Configure(CombatTeam.Player, 100f); _companion.HitPoints.Configure(CombatTeam.Companion, 200f);
            WarpPlayer(_startPlayer);
            _companion.GetComponent<NavMeshAgent>().Warp(_startCompanion);
            _companion.StopForPlayerHold();
            foreach (var zombie in _zombies) zombie.gameObject.SetActive(false);
            _tactics.Sensor.Refresh(true);
        }
        private SurvivalZombie Activate(ZombieKind kind, Vector3 point)
        {
            SurvivalZombie selected = null;
            foreach (var zombie in _zombies) if (zombie.name == "Tactics Test " + kind) { selected = zombie; break; }
            if (selected == null) throw new Exception("Missing test zombie: " + kind);
            selected.gameObject.SetActive(true);
            selected.HitPoints.Configure(CombatTeam.Monster, kind == ZombieKind.Crawler ? 50f : kind == ZombieKind.Burrower ? 100f : 80f);
            selected.MoveSpeed = 0f; selected.Damage = 0f; selected.AttackRange = 0f;
            selected.GetComponent<NavMeshAgent>().Warp(point);
            foreach (var ability in selected.GetComponents<ZombieAbility>()) ability.enabled = false;
            selected.ReportThreat(ZombieAttackStage.Idle, null, point, point, 0f, 0f);
            _tactics.Sensor.Refresh(true);
            return selected;
        }
        private void WarpPlayer(Vector3 point)
        {
            var controller = _player.GetComponent<CharacterController>(); controller.enabled = false;
            _player.transform.position = point; controller.enabled = true;
        }
        private void SubmitUi(string text)
        {
            _player.Chat.Open();
            _player.Chat.GetComponent<UIDocument>().rootVisualElement.Q<TextField>("cpc-chat-input").value = text;
            _player.Chat.SendMessage("RequestSubmit");
        }
        private string Decode(CpcAction action, SurvivalZombie target = null, string intent = "command") => RawDecode(Json(action, target, intent));
        private string RawDecode(string json, float started = float.NaN)
        {
            int generation = (int)typeof(CpcTactics).GetField("_chatGeneration", PRIVATE).GetValue(_tactics);
            return (string)typeof(CpcTactics).GetMethod("Decode", PRIVATE).Invoke(_tactics,
                new object[] { json, new bool[0], generation, float.IsNaN(started) ? Time.unscaledTime : started, true, false });
        }
        private static string Json(CpcAction action, SurvivalZombie target, string intent = "command") =>
            "{\"action\":\"" + action.ToString().ToLowerInvariant() + "\",\"targetId\":\"" +
            (target != null ? target.TacticalId : "") + "\",\"speech\":\"공격할게.\",\"intent\":\"" + intent + "\"}";
        private void Check(bool passed, string message)
        {
            if (!passed) throw new Exception(message + " | reject=" + _tactics.LastRejection + " | status=" + _companion.Llm.Status);
            _results.Add(message); Result = "Running\n" + string.Join("\n", _results);
        }
        private void CaptureLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("[CPC/", StringComparison.Ordinal) && !message.StartsWith("[CPC/TacticsChecks]", StringComparison.Ordinal)) _logs.Add(message);
        }
    }
}
#endif



