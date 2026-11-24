#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CpcPrototype.Editor
{
    // 실제 마이크/모델 없이 입력 경계와 Unity 행동 판정을 재현하는 PlayMode 통합 검사.
    public sealed class CpcVoiceFeatureChecks : MonoBehaviour
    {
        public static string Result { get; private set; } = "Not run";
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<string> _passed = new List<string>();
        private CpcVoiceInput _voice;
        private SurvivalPlayer _player;
        private CpcChatPanel _chat;
        private ShieldCompanion _companion;
        private Keyboard _keyboard;
        private IVoiceRecorder _oldRecorder;
        private IVoiceTranscriber _oldTranscriber;
        private Action<string> _oldSend;
        private Action<bool> _oldBlock;
        private FakeRecorder _recorder;
        private FakeTranscriber _stt;
        private int _sent;
        private bool _oldThreat, _oldDamage;
        private float _oldTimeScale;
        private VoiceRecognitionMode _oldMode;
        private bool _oldCsv;
        private Vector3 _playerPosition;
        private readonly List<SurvivalZombie> _disabled = new List<SurvivalZombie>();

        [MenuItem("Tools/CPC/Run Voice Feature Checks (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
            if (FindFirstObjectByType<CpcVoiceFeatureChecks>() != null) throw new InvalidOperationException("Checks already running.");
            Result = "Running";
            new GameObject("CPC Voice Feature Checks").AddComponent<CpcVoiceFeatureChecks>().StartCoroutine("RunChecks");
        }

        private IEnumerator RunChecks()
        {
            IEnumerator checks = Checks();
            while (true)
            {
                object next = null;
                bool running = false;
                try { running = checks.MoveNext(); if (running) next = checks.Current; }
                catch (Exception exception) { Result = "FAIL: " + exception + "\nPassed before failure:\n" + string.Join("\n", _passed); break; }
                if (!running) { Result = "PASS\n" + string.Join("\n", _passed); break; }
                yield return next;
            }
            Cleanup();
            System.IO.File.WriteAllText("Temp/CpcVoiceFeatureChecks.txt", Result);
            Debug.Log("[CPC/VoiceChecks] " + Result);
            Destroy(gameObject);
        }

        private IEnumerator Checks()
        {
            _voice = FindFirstObjectByType<CpcVoiceInput>();
            Check(_voice != null, "scene voice component");
            _player = _voice.Player; _chat = _voice.Chat; _companion = _player.Companion;
            _oldMode = _voice.RecognitionMode; _oldCsv = _voice.WriteComparisonCsv;
            _voice.RecognitionMode = VoiceRecognitionMode.BatchInference; _voice.WriteComparisonCsv = false;
            _oldRecorder = Get<IVoiceRecorder>(_voice, "_recorder");
            _oldTranscriber = Get<IVoiceTranscriber>(_voice, "_transcriber");
            _oldSend = Get<Action<string>>(_chat, "_sendMessage");
            _oldBlock = Get<Action<bool>>(_chat, "_setInputBlocked");
            _recorder = new FakeRecorder(); _stt = new FakeTranscriber();
            _voice.ConfigureServices(_recorder, _stt);
            _chat.Configure(_oldBlock, text => _sent++);
            _voice.SendMessage("OnApplicationFocus", true);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _oldThreat = _companion.Tactics.OnThreat; _oldDamage = _companion.Tactics.OnDamage;
            _companion.Tactics.OnThreat = _companion.Tactics.OnDamage = false;
            _oldTimeScale = Time.timeScale; Time.timeScale = 1f;
            _playerPosition = _player.transform.position;
            foreach (var zombie in SurvivalZombie.Active.ToArray())
                if (zombie.enabled) { _disabled.Add(zombie); zombie.enabled = false; }

            Press(true); yield return null; yield return null;
            Check(_voice.State == VoiceInputState.Recording && _sent == 0 && _stt.Starts == 0, "M held: recording, zero STT/CPC submissions");
            Press(false); yield return null; yield return null; yield return null;
            Check(!_recorder.Active && _voice.State == VoiceInputState.Recognizing && _sent == 0, "M release stops capture before awaiting final");
            _stt.Complete("여기서 기다려"); _stt.Complete("중복 결과");
            Check(_sent == 1, "final callback submitted exactly once through common chat path");
            yield return null;

            _recorder.Silent = true;
            Press(true); yield return null; yield return null;
            Press(false); yield return null; yield return null;
            Check(_sent == 1 && _voice.State == VoiceInputState.Error, "silence rejected");
            _recorder.Silent = false;

            _chat.Open(); int begins = _recorder.Begins;
            Press(true); yield return null; yield return null;
            Check(_recorder.Begins == begins && !_recorder.Active, "typing M in open chat never starts microphone");
            Press(false); _chat.Close(); yield return null;

            Press(true); yield return null; yield return null;
            _voice.SendMessage("OnApplicationFocus", false);
            Check(!_recorder.Active && _voice.State == VoiceInputState.Idle && _sent == 1, "focus loss cancels and releases microphone");
            Press(false); _voice.SendMessage("OnApplicationFocus", true); yield return null;

            Press(true); yield return null; yield return null;
            Press(false); yield return null; yield return null; yield return null;
            Action<string, string> stale = _stt.Callback;
            Press(true); yield return null; yield return null;
            stale("이전 녹음", null);
            Check(_sent == 1 && _voice.State == VoiceInputState.Recording, "rapid repress rejects stale recording result");
            Press(false); yield return null; yield return null; yield return null;
            _stt.Complete("따라와");
            Check(_sent == 2, "new recording final submitted once"); yield return null;

            Press(true); yield return null; yield return null;
            Press(false); yield return null; yield return null; yield return null;
            _stt.Complete("  "); Check(_sent == 2 && _voice.State == VoiceInputState.Error, "empty final rejected"); yield return null;

            Press(true); yield return null; yield return null;
            Press(false); yield return null; yield return null; yield return null;
            _stt.Complete(null, "recognition failed");
            Check(_sent == 2 && _voice.State == VoiceInputState.Error, "STT failure rejected"); yield return null;

            _recorder.Denied = true;
            Press(true); yield return null; yield return null;
            Check(!_recorder.Active && _voice.State == VoiceInputState.Error && _sent == 2, "microphone permission failure rejected");
            Press(false); _recorder.Denied = false; yield return null;

            Press(true); yield return null; yield return null;
            _voice.enabled = false;
            Check(!_recorder.Active && _voice.State == VoiceInputState.Idle, "disable/unload cleanup releases capture");
            _voice.enabled = true; Press(false); yield return null;

            CpcTactics tactics = _companion.Tactics;
            Decode("hold", "여기서 기다릴게.");
            Vector3 held = _companion.transform.position;
            _player.GetComponent<CharacterController>().enabled = false;
            _player.transform.position += Vector3.right * 3f;
            _player.GetComponent<CharacterController>().enabled = true;
            yield return new WaitForSecondsRealtime(6.5f);
            Check(tactics.TryGetOrder(out CpcAction action, out _) && action == CpcAction.Hold &&
                Vector3.Distance(held, _companion.transform.position) < .05f && _companion.GetComponent<NavMeshAgent>().isStopped,
                "accepted Hold stays physically stopped beyond six seconds while player moves");
            string status = Decode("follow", "여기서 기다리는 중이야.", true);
            Check(tactics.IsHolding && status.Contains("기다리는"), "status question preserves Hold even if model proposes follow");
            Decode("follow", "따라갈게."); yield return null; yield return null;
            Check(!tactics.IsHolding && tactics.TryGetOrder(out action, out _) && action == CpcAction.Follow &&
                !_companion.GetComponent<NavMeshAgent>().isStopped, "accepted Follow resumes navigation");
            Decode("hold", "기다릴게.");
            _companion.HitPoints.TakeDamage(150f, _player.gameObject); yield return null; yield return null;
            Check(tactics.IsHolding && _companion.IsRetreating, "emergency retreat suspends persistent Hold");
        }
        private string Decode(string action, string speech, bool preserve = false)
        {
            var tactics = _companion.Tactics;
            string json = "{\"action\":\"" + action + "\",\"targetId\":\"\",\"speech\":\"" + speech + "\"}";
            return (string)typeof(CpcTactics).GetMethod("Decode", PRIVATE).Invoke(tactics,
                new object[] { json, new bool[_companion.Monsters.Length], Get<int>(tactics, "_chatGeneration"), Time.unscaledTime, true, preserve });
        }
        private void Press(bool down) { InputSystem.QueueStateEvent(_keyboard, down ? new KeyboardState(Key.M) : new KeyboardState()); }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, PRIVATE).GetValue(target);
        private void Check(bool passed, string label)
        {
            if (!passed) throw new Exception(label);
            _passed.Add(label);
        }
        private void Cleanup()
        {
            if (_voice != null)
            {
                _voice.ConfigureServices(_oldRecorder, _oldTranscriber);
                _voice.RecognitionMode = _oldMode; _voice.WriteComparisonCsv = _oldCsv;
                _voice.SendMessage("OnApplicationFocus", Application.isFocused);
            }
            if (_chat != null) _chat.Configure(_oldBlock, _oldSend);
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            foreach (var zombie in _disabled) if (zombie != null) zombie.enabled = true;
            if (_player != null)
            {
                _player.GetComponent<CharacterController>().enabled = false;
                _player.transform.position = _playerPosition;
                _player.GetComponent<CharacterController>().enabled = true;
            }
            if (_companion != null)
            {
                _companion.Tactics.OnThreat = _oldThreat; _companion.Tactics.OnDamage = _oldDamage;
                _companion.Tactics.ReleasePlayerMovementOrder();
                _companion.HitPoints.Configure(CombatTeam.Companion, 200f);
            }
            Time.timeScale = _oldTimeScale;
        }
        private sealed class FakeRecorder : IVoiceRecorder
        {
            public bool Active, Silent, Denied;
            public int Begins;
            public bool HasStarted => Active;
            public bool Begin(string device, int maxSeconds, out string error)
            {
                Begins++; Active = !Denied; error = Denied ? "microphone permission denied" : null; return Active;
            }
            public VoiceAudio Stop()
            {
                Active = false;
                var data = new float[8000];
                if (!Silent) for (int i = 0; i < data.Length; i++) data[i] = .1f;
                return new VoiceAudio(data, 16000);
            }
            public void Cancel() { Active = false; }
        }
        private sealed class FakeTranscriber : IVoiceTranscriber
        {
            public int Starts;
            public Action<string, string> Callback;
            private bool _finished;
            public IEnumerator Transcribe(VoiceAudio audio, Action<string, string> completed)
            {
                Starts++; Callback = completed; _finished = false;
                while (!_finished) yield return null;
            }
            public void Complete(string text, string error = null) { Callback(text, error); _finished = true; }
            public void Cancel() { _finished = true; }
        }
    }
}

#endif
