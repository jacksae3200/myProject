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
    public sealed class CpcFollowProtectionChecks : MonoBehaviour
    {
        private const BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;
        public static string Result { get; private set; } = "Not run";
        private readonly List<string> _passed = new List<string>();
        private bool _real;
        private SurvivalPlayer _player;
        private ShieldCompanion _cpc;
        private CpcTactics _tactics;
        private CpcLlmSettings _original, _settings;
        private SurvivalZombie _spitter, _crawler;
        private Vector3 _playerStart, _cpcStart;
        [MenuItem("Tools/CPC/Check Follow Protection (Play Mode)")]
        public static void RunUnity() { Run(false); }
        [MenuItem("Tools/CPC/Check Follow Protection With Ollama (Play Mode)")]
        public static void RunModel() { Run(true); }
        private static void Run(bool real)
        {
            if (!Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/KayKit_TacticsTest.unity")
                throw new Exception("Enter Play in KayKit_TacticsTest first.");
            var check = new GameObject("Follow Protection Checks").AddComponent<CpcFollowProtectionChecks>();
            check._real = real;
            check.StartCoroutine(check.Execute());
        }
        private IEnumerator Execute()
        {
            Result = "Running";
            IEnumerator routine = Checks();
            while (true)
            {
                bool more; object next = null;
                try { more = routine.MoveNext(); if (more) next = routine.Current; }
                catch (Exception exception) { Result = "FAIL: " + exception; break; }
                if (!more) { Result = "PASS"; break; }
                yield return next;
            }
            if (_cpc != null) _cpc.Llm.Configure(_original);
            if (_settings != null) Destroy(_settings);
            Result += "\n" + string.Join("\n", _passed);
            System.IO.Directory.CreateDirectory("Tools/Tactics");
            System.IO.File.WriteAllText(_real ? "Tools/Tactics/FollowProtectionModel.txt" : "Tools/Tactics/FollowProtectionUnity.txt", Result);
            Debug.Log("[CPC/FollowChecks] " + Result);
            Destroy(gameObject);
        }
        private IEnumerator Checks()
        {
            _player = FindFirstObjectByType<SurvivalPlayer>(); _cpc = _player.Companion; _tactics = _cpc.Tactics;
            _playerStart = _player.transform.position; _cpcStart = _cpc.transform.position;
            _cpc.Llm.enabled = false; _cpc.Llm.enabled = true;
            _original = _cpc.Llm.Settings; _settings = Instantiate(_original); _settings.EnableLlm = _real;
            _cpc.Llm.Configure(_settings);
            _tactics.OnThreat = _tactics.OnDamage = false; _tactics.UseAutonomousFallback = false;
            _tactics.ReleasePlayerMovementOrder(); _tactics.Motor.ImmediateResponseSeconds = .001f;
            _player.Health.Configure(CombatTeam.Player, 100f); _cpc.HitPoints.Configure(CombatTeam.Companion, 200f);
            foreach (var z in FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (z.name == "Tactics Test Spitter") _spitter = z;
                if (z.name == "Tactics Test Crawler") _crawler = z;
                z.gameObject.SetActive(false);
            }
            yield return null;
            if (_real)
            {
                Submit("여기서 기다려 줘"); yield return null; yield return null; yield return null;
                float until = Time.realtimeSinceStartup + 90f;
                while (_cpc.Llm.HasPendingDialogue && Time.realtimeSinceStartup < until) yield return null;
                Check(_tactics.IsHolding, "REAL Hold accepted");
                Submit("이제 다시 따라와"); yield return null; yield return null; yield return null;
                until = Time.realtimeSinceStartup + 90f;
                while (_cpc.Llm.HasPendingDialogue && Time.realtimeSinceStartup < until) yield return null;
                Check(_tactics.CanProtectWhileFollowing, "REAL Follow defaults to protect mode");
                Activate(_spitter, _playerStart + Vector3.right * 1f);
                _spitter.ReportThreat(ZombieAttackStage.Aiming, _player.Health, _spitter.AimPoint, _playerStart, .1f, 60f);
                _tactics.Sensor.Refresh(true);
                int accepted = _tactics.AcceptedDecisionCount;
                _tactics.OnThreat = true;
                _tactics.Observe(_spitter);
                yield return null;
                until = Time.realtimeSinceStartup + 90f;
                while (_cpc.Llm.IsBusy && Time.realtimeSinceStartup < until) yield return null;
                _tactics.OnThreat = false;
                Check(_tactics.AcceptedDecisionCount > accepted && _tactics.PlayerAction == CpcAction.Follow &&
                    _tactics.LastDecisionOrigin == CpcDecisionOrigin.SituationLlm && _tactics.LastSelectedTarget == _spitter,
                    "REAL autonomous protection accepted while Follow stays active: " + _tactics.LastSelectedAction);
                _tactics.ReleasePlayerMovementOrder(); _cpc.CancelPendingMelee(); _tactics.Motor.Clear();
                _cpc.GetComponent<NavMeshAgent>().Warp(_cpcStart);
                Activate(_crawler, _playerStart + new Vector3(.3f, 0f, .3f));
                _crawler.ReportThreat(ZombieAttackStage.Approach, _cpc.HitPoints, _crawler.AimPoint, _cpcStart, .2f, 0f);
                ((System.Collections.IList)typeof(LocalLlmClient).GetField("_history", PRIVATE).GetValue(_cpc.Llm)).Clear();
                Submit("주변 좀비 좀 알아서 처리해 줘"); yield return null; yield return null; yield return null;
                until = Time.realtimeSinceStartup + 90f;
                while (_cpc.Llm.HasPendingDialogue && Time.realtimeSinceStartup < until) yield return null;
                Check(_tactics.LastSelectedTarget != null && _tactics.PlayerAction == CpcAction.Attack,
                    "REAL underspecified combat request selects target instead of asking: " + _cpc.Llm.LastReply);
                yield break;
            }
            Decode("hold"); Check(_tactics.IsHolding, "Hold accepted");
            Decode("follow"); Check(_tactics.CanProtectWhileFollowing, "Follow releases Hold and enables protection");
            Activate(_spitter, _playerStart + Vector3.right * .7f);
            _spitter.ReportThreat(ZombieAttackStage.Aiming, _player.Health, _spitter.AimPoint, _playerStart, .1f, 10f);
            _tactics.Sensor.Refresh(true);
            Decode("attack", _spitter.TacticalId, chat: false);
            CpcOrder order = default;
            Check(_tactics.PlayerAction == CpcAction.Follow && _tactics.TryGetExecutionOrder(out order) &&
                order.Action == CpcAction.Attack && order.Origin == CpcDecisionOrigin.SituationLlm, "Follow permits autonomous combat without losing baseline directive");
            _tactics.Complete(order.Serial);
            _tactics.UseAutonomousFallback = true;
            yield return null; yield return null;
            Check(_tactics.PlayerAction == CpcAction.Follow &&
                typeof(ShieldCompanion).GetField("_target", PRIVATE).GetValue(_cpc) == _spitter, "existing melee/taunt/shield fallback can protect while following");
            WarpPlayer(_playerStart + Vector3.left * 4f); yield return null; yield return null;
            Check(_tactics.TryGetExecutionOrder(out order) && order.Action == CpcAction.Follow &&
                !_tactics.TryGetFollowProtectionThreat(out _), "player moves away: disengage and rejoin rather than chase");
            WarpPlayer(_playerStart); _cpc.GetComponent<NavMeshAgent>().Warp(_cpcStart);
            Decode("follow"); _tactics.UseAutonomousFallback = false;
            _spitter.HitPoints.TakeDamage(10000f, _player.gameObject); yield return null;
            Check(_tactics.PlayerAction == CpcAction.Follow && _tactics.TryGetExecutionOrder(out order) && order.Action == CpcAction.Follow, "target death returns to Follow");
            Activate(_spitter, _playerStart + Vector3.right * 1f);
            Activate(_crawler, _playerStart + new Vector3(.25f, 0f, .2f));
            _spitter.ReportThreat(ZombieAttackStage.Aiming, _player.Health, _spitter.AimPoint, _playerStart, .1f, 10f);
            _crawler.ReportThreat(ZombieAttackStage.Approach, _cpc.HitPoints, _crawler.AimPoint, _cpcStart, .2f, 0f);
            Decode("attack", "auto");
            Check(_tactics.LastSelectedTarget == _spitter, "automatic priority prefers special zombie threatening player over nearer non-player threat");
            Decode("attack", "auto", extra: ",\"targetPolicy\":\"nearest\"");
            Check(_tactics.LastSelectedTarget == _crawler, "explicit nearest policy selects closest player-relative candidate");
            Decode("attack", "auto", extra: ",\"targetType\":\"Crawler\"");
            Check(_tactics.LastSelectedTarget == _crawler, "explicit type filter preserved");
            Decode("follow", extra: ",\"followMode\":\"passive\"");
            int decisions = _tactics.AcceptedDecisionCount;
            Decode("attack", _spitter.TacticalId, chat: false);
            Check(_tactics.PassiveFollow && _tactics.AcceptedDecisionCount == decisions, "explicit passive Follow still blocks ordinary autonomous combat");
            Decode("hold"); decisions = _tactics.AcceptedDecisionCount;
            Decode("attack", _spitter.TacticalId, chat: false);
            Check(_tactics.IsHolding && _tactics.AcceptedDecisionCount == decisions, "Hold still stays put");
            _spitter.gameObject.SetActive(false); _crawler.gameObject.SetActive(false);
            decisions = _tactics.AcceptedDecisionCount;
            string reply = Decode("attack", "auto");
            Check(_tactics.AcceptedDecisionCount == decisions && !reply.Contains("유형이나 위치"), "no observed candidate fails safely without repeatedly demanding target details");
        }
        private void Activate(SurvivalZombie z, Vector3 point)
        {
            z.gameObject.SetActive(true); z.HitPoints.Configure(CombatTeam.Monster, z.Kind == ZombieKind.Crawler ? 50f : 80f);
            z.MoveSpeed = 0f; z.Damage = 0f; z.AttackRange = 0f;
            foreach (var a in z.GetComponents<ZombieAbility>()) a.enabled = false;
            z.GetComponent<NavMeshAgent>().Warp(point);
            z.ReportThreat(ZombieAttackStage.Idle, null, point, point, 0f, 0f);
        }
        private string Decode(string action, string target = "", string extra = "", bool chat = true)
        {
            _tactics.Sensor.Refresh(true);
            int generation = (int)typeof(CpcTactics).GetField("_chatGeneration", PRIVATE).GetValue(_tactics);
            string json = "{\"action\":\"" + action + "\",\"targetId\":\"" + target + "\",\"speech\":\"알겠어.\",\"intent\":\"command\"" + extra + "}";
            return (string)typeof(CpcTactics).GetMethod("Decode", PRIVATE).Invoke(_tactics, new object[] { json, new bool[0], generation, Time.unscaledTime, chat, false });
        }
        private void Submit(string text)
        {
            _player.Chat.Open(); _player.Chat.GetComponent<UIDocument>().rootVisualElement.Q<TextField>("cpc-chat-input").value = text;
            _player.Chat.SendMessage("RequestSubmit");
        }
        private void WarpPlayer(Vector3 point)
        {
            var controller = _player.GetComponent<CharacterController>(); controller.enabled = false;
            _player.transform.position = point; controller.enabled = true;
        }
        private void Check(bool condition, string text)
        {
            if (!condition) throw new Exception(text + " | " + _tactics.LastRejection + " | " + _cpc.Llm.Status);
            _passed.Add(text); Result = "Running\n" + string.Join("\n", _passed);
        }
    }
}
#endif

