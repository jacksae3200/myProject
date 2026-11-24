#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CpcPrototype.Editor
{
    public sealed class CpcVoiceReplayComparison : MonoBehaviour
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Result { get; private set; } = "Not run";
        private readonly List<string> _results = new List<string>();
        private readonly List<string> _texts = new List<string>();
        private CpcVoiceInput _voice;
        private CpcChatPanel _chat;
        private Keyboard _keyboard;
        private IVoiceRecorder _oldRecorder;
        private IVoiceTranscriber _oldStt;
        private Action<string> _oldSend;
        private Action<bool> _oldBlock;
        private VoiceRecognitionMode _oldMode;
        private bool _oldCsv;
        private string _oldLabel, _path;
        private int _oldMaxSeconds;
        private ReplayedRecorder _recorder;

        [MenuItem("Tools/CPC/Compare Voice Modes With Test WAV (Play Mode)")]
        public static void Run() { RunWithFile("Tools/Voice/ComparisonSamples/ko-hold.wav"); }
        [MenuItem("Tools/CPC/Compare Voice Modes With Selected WAV (Play Mode)")]
        public static void SelectFile()
        {
            string path = EditorUtility.OpenFilePanel("PCM16 mono 16kHz WAV", "", "wav");
            if (!string.IsNullOrEmpty(path)) RunWithFile(path);
        }
        public static void RunWithFile(string path)
        {
            if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
            if (!File.Exists(path)) throw new FileNotFoundException("Comparison WAV not found", path);
            var runner = new GameObject("Voice Replay Comparison").AddComponent<CpcVoiceReplayComparison>();
            runner._path = path;
            runner.StartCoroutine(runner.Execute());
        }
        private IEnumerator Execute()
        {
            Result = "Running";
            IEnumerator checks = Checks();
            while (true)
            {
                bool more; object next = null;
                try { more = checks.MoveNext(); if (more) next = checks.Current; }
                catch (Exception exception) { Result = "FAIL: " + exception; break; }
                if (!more) { Result = "PASS"; break; }
                yield return next;
            }
            if (_voice != null)
            {
                _voice.ConfigureServices(_oldRecorder, _oldStt);
                _voice.RecognitionMode = _oldMode; _voice.WriteComparisonCsv = _oldCsv;
                _voice.ComparisonLabel = _oldLabel; _voice.MaxRecordingSeconds = _oldMaxSeconds;
                _voice.SendMessage("OnApplicationFocus", Application.isFocused);
            }
            if (_chat != null) _chat.Configure(_oldBlock, _oldSend);
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            Result += "\n" + string.Join("\n", _results);
            Directory.CreateDirectory("Tools/Voice");
            File.WriteAllText("Tools/Voice/RealSttComparison.txt", Result);
            Debug.Log("[CPC/VoiceReplay] " + Result);
            Destroy(gameObject);
        }
        private IEnumerator Checks()
        {
            _voice = FindFirstObjectByType<CpcVoiceInput>(); _chat = _voice.Chat;
            _oldRecorder = Get<IVoiceRecorder>(_voice, "_recorder"); _oldStt = Get<IVoiceTranscriber>(_voice, "_transcriber");
            _oldSend = Get<Action<string>>(_chat, "_sendMessage"); _oldBlock = Get<Action<bool>>(_chat, "_setInputBlocked"); _oldMode = _voice.RecognitionMode;
            _oldCsv = _voice.WriteComparisonCsv; _oldLabel = _voice.ComparisonLabel; _oldMaxSeconds = _voice.MaxRecordingSeconds;
            _recorder = new ReplayedRecorder(ReadPcm16(_path));
            _voice.ConfigureServices(_recorder, _voice.GetComponent<WhisperLocalStt>());
            _voice.WriteComparisonCsv = true; _voice.ComparisonLabel = "wav-replay:" + Path.GetFileName(_path);
            _voice.MaxRecordingSeconds = 90;
            _voice.SendMessage("OnApplicationFocus", true);
            _chat.Configure(_oldBlock, text => { _texts.Add(text); _oldSend(text); });
            _voice.Player.Companion.Tactics.OnThreat = _voice.Player.Companion.Tactics.OnDamage = false;
            _voice.Player.Companion.Llm.enabled = false; _voice.Player.Companion.Llm.enabled = true;
            foreach (var zombie in SurvivalZombie.Active.ToArray()) zombie.gameObject.SetActive(false);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            var sliding = _voice.GetComponent<SlidingWindowVoiceInput>();
            sliding.WindowSeconds = 4f; sliding.MinimumWindowSeconds = 1f; sliding.StepSeconds = .5f;
            sliding.SilenceSeconds = .65f; sliding.PreRollSeconds = .2f;
            foreach (VoiceRecognitionMode mode in new[] { VoiceRecognitionMode.BatchInference, VoiceRecognitionMode.SlidingWindow })
            {
                float llmDeadline = Time.realtimeSinceStartup + 90f;
                while (_voice.Player.Companion.Llm.HasPendingDialogue && Time.realtimeSinceStartup < llmDeadline) yield return null;
                _voice.SetRecognitionMode(mode);
                int before = _texts.Count;
                int beforeRequests = _voice.Player.Companion.Tactics.RequestsSent;
                Press(true); yield return null; yield return null;
                if (mode == VoiceRecognitionMode.BatchInference)
                {
                    yield return new WaitForSecondsRealtime(_recorder.SourceSeconds + .2f);
                    Check(_texts.Count == before, "batch: no CPC request before release");
                    Press(false);
                }
                float deadline = Time.realtimeSinceStartup + 90f;
                while (_texts.Count == before && _voice.State != VoiceInputState.Error && Time.realtimeSinceStartup < deadline) yield return null;
                Check(_texts.Count == before + 1, mode + ": real STT final reaches existing CPC input once; " + _voice.StatusText);
                VoiceRecognitionMetrics metrics = _voice.LastMetrics;
                Check(metrics != null && metrics.Mode == mode && _voice.Player.Companion.Tactics.RequestsSent == beforeRequests + 1,
                    mode + ": single final handed to CpcTactics/LocalLlmClient");
                if (mode == VoiceRecognitionMode.SlidingWindow)
                {
                    Check(_voice.IsCapturing && metrics.FirstRequest < metrics.SpeechEnded,
                        "sliding: real /inference started before speech end; final submitted while M held");
                    Press(false); yield return null; yield return null; yield return null;
                    Check(_texts.Count == before + 1, "sliding: release after VAD final does not duplicate CPC request");
                }
                _results.Add($"MEASURE {mode} text={metrics.Text} requests={metrics.Requests} end_to_submit_ms={metrics.SpeechEndToSubmitMs:F0} reused={metrics.ReusedPartial} full_final={metrics.FullUtteranceFinal}");
                Result = "Running\n" + string.Join("\n", _results);
            }
        }
        private static VoiceAudio ReadPcm16(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new Exception("WAV RIFF required");
                reader.ReadInt32(); if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new Exception("WAVE required");
                int rate = 0, channels = 0, bits = 0, format = 0; byte[] data = null;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4)); int length = reader.ReadInt32();
                    long next = reader.BaseStream.Position + length + (length & 1);
                    if (length < 0 || next > reader.BaseStream.Length) throw new Exception("Invalid WAV chunk");
                    if (chunk == "fmt ")
                    {
                        format = reader.ReadInt16(); channels = reader.ReadInt16(); rate = reader.ReadInt32();
                        reader.ReadInt32(); reader.ReadInt16(); bits = reader.ReadInt16();
                    }
                    else if (chunk == "data") data = reader.ReadBytes(length);
                    reader.BaseStream.Position = next;
                }
                if (format != 1 || rate != 16000 || channels != 1 || bits != 16 || data == null || data.Length == 0)
                    throw new Exception("Use PCM16 mono 16000Hz WAV");
                var samples = new float[6400 + data.Length / 2 + 16000];
                for (int i = 0; i < data.Length / 2; i++) samples[6400 + i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                return new VoiceAudio(samples, rate);
            }
        }
        private void Check(bool value, string text) { if (!value) throw new Exception(text); _results.Add(text); Result = "Running\n" + string.Join("\n", _results); }
        private void Press(bool down) { InputSystem.QueueStateEvent(_keyboard, down ? new KeyboardState(Key.M) : new KeyboardState()); }
        private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, PRIVATE).GetValue(obj);
        private sealed class ReplayedRecorder : ILiveVoiceRecorder
        {
            private readonly VoiceAudio _source;
            private int _read, _capacity;
            private float _started;
            private bool _active;
            public float SourceSeconds => (float)_source.Samples.Length / _source.SampleRate;
            public int SampleRate => _source.SampleRate;
            public bool HasStarted => _active && Written > 0;
            private int Written => Mathf.Min(_capacity, Mathf.Max(0, Mathf.FloorToInt((Time.realtimeSinceStartup - _started) * SampleRate)));
            public ReplayedRecorder(VoiceAudio source) { _source = source; }
            public bool Begin(string device, int seconds, out string error) { _read = 0; _capacity = seconds * SampleRate; _started = Time.realtimeSinceStartup; _active = true; error = null; return true; }
            public int ReadAvailable(float[] target)
            {
                if (!_active || Written - _read < target.Length) return 0;
                for (int i = 0; i < target.Length; i++) target[i] = _read + i < _source.Samples.Length ? _source.Samples[_read + i] : 0f;
                _read += target.Length; return target.Length;
            }
            public VoiceAudio Stop()
            {
                var samples = new float[Written]; Array.Copy(_source.Samples, samples, Mathf.Min(samples.Length, _source.Samples.Length));
                _active = false; return new VoiceAudio(samples, SampleRate);
            }
            public void Cancel() { _active = false; }
        }
    }
}
#endif
