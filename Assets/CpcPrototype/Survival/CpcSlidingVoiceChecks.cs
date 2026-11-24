#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CpcPrototype.Editor
{
    public sealed class CpcSlidingVoiceChecks : MonoBehaviour
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Result { get; private set; } = "Not run";
        private readonly List<string> _passed = new List<string>();
        private readonly List<string> _sent = new List<string>();
        private CpcVoiceInput _voice;
        private SlidingWindowVoiceInput _sliding;
        private CpcChatPanel _chat;
        private Keyboard _keyboard;
        private IVoiceRecorder _oldRecorder;
        private IVoiceTranscriber _oldStt;
        private Action<string> _oldSend;
        private Action<bool> _oldBlock;
        private VoiceRecognitionMode _oldMode;
        private bool _oldCsv;
        private LiveRecorder _recorder;
        private DeferredStt _stt;
        [MenuItem("Tools/CPC/Run Sliding Voice Checks (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
            new GameObject("Sliding Voice Checks").AddComponent<CpcSlidingVoiceChecks>().StartCoroutine("Execute");
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
            if (_voice != null)
            {
                _voice.ConfigureServices(_oldRecorder, _oldStt);
                _voice.RecognitionMode = _oldMode; _voice.WriteComparisonCsv = _oldCsv;
                _voice.SendMessage("OnApplicationFocus", Application.isFocused);
            }
            if (_chat != null) _chat.Configure(_oldBlock, _oldSend);
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            Result += "\n" + string.Join("\n", _passed);
            System.IO.Directory.CreateDirectory("Tools/Voice");
            System.IO.File.WriteAllText("Tools/Voice/SlidingWindowChecks.txt", Result);
            Debug.Log("[CPC/SlidingChecks] " + Result);
            Destroy(gameObject);
        }
        private IEnumerator Checks()
        {
            _voice = FindFirstObjectByType<CpcVoiceInput>(); _chat = _voice.Chat;
            _sliding = _voice.GetComponent<SlidingWindowVoiceInput>();
            _oldRecorder = Get<IVoiceRecorder>(_voice, "_recorder"); _oldStt = Get<IVoiceTranscriber>(_voice, "_transcriber");
            _oldSend = Get<Action<string>>(_chat, "_sendMessage"); _oldBlock = Get<Action<bool>>(_chat, "_setInputBlocked"); _oldMode = _voice.RecognitionMode; _oldCsv = _voice.WriteComparisonCsv;
            _recorder = new LiveRecorder(); _stt = new DeferredStt();
            _voice.ConfigureServices(_recorder, _stt);
            _voice.RecognitionMode = VoiceRecognitionMode.SlidingWindow; _voice.WriteComparisonCsv = false;
            _sliding.WindowSeconds = 1.5f; _sliding.MinimumWindowSeconds = .3f; _sliding.StepSeconds = .1f;
            _sliding.SilenceSeconds = .2f; _sliding.PreRollSeconds = .1f;
            _voice.SendMessage("OnApplicationFocus", true);
            _chat.Configure(_oldBlock, text => _sent.Add(text));
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Press(true); yield return null; yield return null;
            _recorder.Push(.6f, 0f); yield return new WaitForSecondsRealtime(.06f);
            Check(_stt.Starts == 0 && _sent.Count == 0, "silence does not start sliding inference");
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.08f);
            Check(_stt.Starts == 1 && _voice.IsCapturing && _sent.Count == 0, "speech starts STT while M is still held");
            _stt.Complete("여기서"); yield return null;
            Check(_voice.StatusText.Contains("여기서") && _sent.Count == 0, "partial text is UI-only");
            _recorder.Push(.36f, .1f); yield return new WaitForSecondsRealtime(.15f);
            _stt.Complete("여기서 기다려"); yield return null;
            _recorder.Push(.3f, 0f); yield return new WaitForSecondsRealtime(.06f);
            Check(_sent.Count == 1 && _sent[0] == "여기서 기다려" && _voice.IsCapturing && _recorder.Active,
                "VAD final sends once before M release and keeps listening");
            Check(_voice.LastMetrics.Mode == VoiceRecognitionMode.SlidingWindow && _voice.LastMetrics.ReusedPartial,
                "complete short-window result is reused without final retranscription");
            _stt.Complete("duplicate callback");
            Check(_sent.Count == 1, "duplicate callback cannot resubmit final");
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.15f);
            _stt.Complete("여기서 기다려"); yield return null;
            _recorder.Push(.3f, 0f); yield return new WaitForSecondsRealtime(.06f);
            Check(_sent.Count == 2, "separate utterances with identical words are not incorrectly deduplicated");
            Press(false); yield return null; yield return null; yield return null;
            Check(!_recorder.Active && _sent.Count == 2, "M release frees microphone without duplicate dispatch");

            _sliding.WindowSeconds = .5f;
            Press(true); yield return null; yield return null;
            _recorder.Push(2f, .1f); yield return new WaitForSecondsRealtime(.12f);
            Check(_stt.CurrentSamples <= 8000 && _stt.Active == 1, "rolling request is bounded by window length");
            _recorder.Push(.3f, 0f); yield return new WaitForSecondsRealtime(.06f);
            int requests = _stt.Starts;
            yield return new WaitForSecondsRealtime(.3f);
            Check(_stt.Starts == requests && _stt.MaxActive == 1, "slow STT cannot create a concurrent or unbounded request backlog");
            _stt.Complete("마지막 구간"); yield return new WaitForSecondsRealtime(.08f);
            Check(_stt.CurrentSamples > 8000 && _sent.Count == 2, "long utterance final rechecks full audio instead of submitting a truncated tail");
            _stt.Complete("처음부터 끝까지 긴 지시"); yield return null;
            Check(_sent.Count == 3 && _sent[2] == "처음부터 끝까지 긴 지시" && _voice.LastMetrics.FullUtteranceFinal,
                "long utterance final preserves full command and records comparison metrics");
            Press(false); yield return null; yield return null;

            Press(true); yield return null; yield return null;
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.08f);
            Action<string, string> stale = _stt.Callback;
            Press(false); yield return null; yield return null;
            Press(true); yield return null; yield return null;
            stale("이전 녹음", null);
            Check(_sent.Count == 3 && _voice.IsCapturing, "rapid repress invalidates previous session callbacks");
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.08f);
            stale = _stt.Callback;
            _voice.SendMessage("OnApplicationFocus", false);
            stale("포커스 잃은 결과", null);
            Check(!_recorder.Active && !_sliding.IsRunning && _sent.Count == 3, "focus loss cancels partial/final work and releases capture");
            Press(false); _voice.SendMessage("OnApplicationFocus", true); yield return null;
            _chat.Open(); int begins = _recorder.Begins;
            Press(true); yield return null; yield return null;
            Check(_recorder.Begins == begins, "typing M in chat cannot start sliding capture");
            Press(false); _chat.Close(); yield return null;

            Press(true); yield return null; yield return null;
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.08f);
            stale = _stt.Callback;
            _voice.SetRecognitionMode(VoiceRecognitionMode.BatchInference);
            stale("전환 이전 결과", null);
            Check(!_recorder.Active && !_sliding.IsRunning && _sent.Count == 3, "mode switch cancels previous mode without mixing results");
            Press(false); yield return null;
            Press(true); yield return null; yield return null;
            int beforeBatch = _stt.Starts;
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.15f);
            Check(_stt.Starts == beforeBatch, "batch comparison mode still waits for M release");
            Press(false); yield return null; yield return null; yield return null;
            _stt.Complete("따라와"); yield return null;
            Check(_sent.Count == 4 && _voice.LastMetrics.Mode == VoiceRecognitionMode.BatchInference && _voice.LastMetrics.Requests == 1,
                "batch final path preserved with separately labeled metrics");

            _voice.SetRecognitionMode(VoiceRecognitionMode.SlidingWindow);
            Press(true); yield return null; yield return null;
            _recorder.Push(.6f, .1f); yield return new WaitForSecondsRealtime(.08f);
            _stt.Complete(null, "STT failed"); yield return null;
            Check(_voice.State == VoiceInputState.Error && !_recorder.Active && _sent.Count == 4,
                "sliding STT failure is visible and cannot send partial text");
            Press(false); yield return null;
        }
        private void Check(bool value, string text)
        {
            if (!value) throw new Exception(text + " state=" + _voice.State + " status=" + _voice.StatusText + " requests=" + _stt.Starts + " sent=" + _sent.Count);
            _passed.Add(text); Result = "Running\n" + string.Join("\n", _passed);
        }
        private void Press(bool down) { InputSystem.QueueStateEvent(_keyboard, down ? new KeyboardState(Key.M) : new KeyboardState()); }
        private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, PRIVATE).GetValue(obj);
        private sealed class LiveRecorder : ILiveVoiceRecorder
        {
            private readonly float[] _samples = new float[16000 * 30];
            private int _written, _read;
            public bool Active;
            public int Begins;
            public int SampleRate => 16000;
            public bool HasStarted => Active;
            public bool Begin(string device, int seconds, out string error) { Begins++; _written = _read = 0; Active = true; error = null; return true; }
            public void Push(float seconds, float level)
            { int count = Mathf.RoundToInt(seconds * SampleRate); for (int i = 0; i < count; i++) _samples[_written++] = level; }
            public int ReadAvailable(float[] target)
            {
                if (!Active || _written - _read < target.Length) return 0;
                Array.Copy(_samples, _read, target, 0, target.Length); _read += target.Length; return target.Length;
            }
            public VoiceAudio Stop() { Active = false; var data = new float[_written]; Array.Copy(_samples, data, _written); return new VoiceAudio(data, SampleRate); }
            public void Cancel() { Active = false; }
        }
        private sealed class DeferredStt : IVoiceTranscriber
        {
            public int Starts, Active, MaxActive, CurrentSamples;
            public Action<string, string> Callback;
            private bool _done;
            public IEnumerator Transcribe(VoiceAudio audio, Action<string, string> completed)
            {
                Starts++; Active++; MaxActive = Mathf.Max(MaxActive, Active); CurrentSamples = audio.Samples.Length;
                Callback = completed; _done = false;
                while (!_done) yield return null;
                Active = Mathf.Max(0, Active - 1);
            }
            public void Complete(string text, string error = null) { Callback(text, error); _done = true; }
            public void Cancel() { _done = true; Active = 0; }
        }
    }
}
#endif
