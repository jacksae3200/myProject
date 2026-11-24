using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public enum VoiceInputState { Idle, Recording, Recognizing, WaitingForCpc, Error }

    [DisallowMultipleComponent, RequireComponent(typeof(WhisperLocalStt), typeof(SlidingWindowVoiceInput))]
    public sealed class CpcVoiceInput : MonoBehaviour
    {
        public SurvivalPlayer Player;
        public CpcChatPanel Chat;
        public VoiceRecognitionMode RecognitionMode = VoiceRecognitionMode.SlidingWindow;
        public string MicrophoneDevice = "";
        [Min(1)] public int MaxRecordingSeconds = 30;
        [Min(.01f)] public float MinimumSpeechSeconds = .2f;
        [Min(0f)] public float SilenceRmsThreshold = .008f;
        [Min(.1f)] public float MicrophoneStartTimeout = 3f;
        public bool WriteComparisonCsv = true;
        public string ComparisonLabel = "whisper-local";
        public VoiceInputState State { get; private set; }
        public bool IsCapturing => _captureActive;
        public string LastTranscript { get; private set; } = "";
        public string StatusText { get; private set; } = "M 누르는 동안 말하기";
        public VoiceRecognitionMetrics LastMetrics { get; private set; }
        public event Action<string> FinalTextSubmitted;
        public event Action<VoiceRecognitionMetrics> MetricsRecorded;
        private IVoiceRecorder _recorder;
        private IVoiceTranscriber _transcriber;
        private SlidingWindowVoiceInput _sliding;
        private Coroutine _recognition;
        private VoiceRecognitionMetrics _batchMetrics;
        private VoiceRecognitionMode _captureMode;
        private int _generation;
        private float _recordingStarted, _keyReleased;
        private bool _focused, _microphoneStarted, _captureActive;

        private void Awake()
        {
            if (Player == null) Player = GetComponent<SurvivalPlayer>();
            if (Chat == null && Player != null) Chat = Player.Chat;
            _recorder = new UnityVoiceRecorder();
            _transcriber = GetComponent<WhisperLocalStt>();
            _sliding = GetComponent<SlidingWindowVoiceInput>();
            if (_sliding == null) _sliding = gameObject.AddComponent<SlidingWindowVoiceInput>();
            _sliding.SpeechStarted += OnSpeechStarted;
            _sliding.PartialRecognized += OnPartialRecognized;
            _sliding.FinalRecognized += OnSlidingFinal;
            _sliding.Failed += FailCapture;
            _sliding.Completed += OnSlidingCompleted;
            _focused = Application.isFocused;
        }
        private void Start() { SetState(VoiceInputState.Idle, ReadyText()); }
        public void ConfigureServices(IVoiceRecorder recorder, IVoiceTranscriber transcriber)
        {
            CancelCapture(); _recorder = recorder; _transcriber = transcriber;
        }
        public void SetRecognitionMode(VoiceRecognitionMode mode)
        {
            CancelCapture(); RecognitionMode = mode;
            SetState(VoiceInputState.Idle, ReadyText());
        }
        private string ReadyText() => RecognitionMode == VoiceRecognitionMode.SlidingWindow ?
            "M 누르는 동안 말하기 · 슬라이딩 윈도우" : "M 누르고 말한 뒤 놓기 · 일괄 inference";
        private bool InputBlocked => !_focused || Player == null || Player.Health == null || !Player.Health.IsAlive ||
            Player.IsChatOpen || Chat == null || Chat.IsOpen;
        private void Update()
        {
            if (_captureMode != RecognitionMode && (_captureActive || _recognition != null || _sliding.IsRunning))
            { CancelCapture(); return; }
            if (InputBlocked)
            {
                if (_captureActive || _recognition != null || _sliding.IsRunning) CancelCapture();
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) { if (_captureActive) CancelCapture(); return; }
            if (keyboard.mKey.wasPressedThisFrame) BeginCapture();
            if (_captureActive && !keyboard.mKey.isPressed) EndCapture();
            if (_captureActive)
            {
                _microphoneStarted |= _recorder.HasStarted;
                float elapsed = Time.realtimeSinceStartup - _recordingStarted;
                if (!_microphoneStarted && elapsed > MicrophoneStartTimeout)
                    FailCapture("마이크 입력이 없습니다. 마이크 권한과 장치를 확인하세요.");
                else if (elapsed >= MaxRecordingSeconds)
                    FailCapture("녹음 제한 시간 초과. M을 놓고 다시 눌러 주세요.");
            }
            if (State == VoiceInputState.WaitingForCpc && Player.Companion != null && !Player.Companion.Llm.HasPendingDialogue)
                SetState(_captureActive ? VoiceInputState.Recording : VoiceInputState.Idle,
                    _captureActive ? "음성 대기 중 · M 녹음 켜짐" : ReadyText() + " · 인식: " + LastTranscript);
        }
        public bool BeginCapture()
        {
            if (InputBlocked || !isActiveAndEnabled) return false;
            CancelPending();
            _captureMode = RecognitionMode;
            LastTranscript = ""; LastMetrics = null; _keyReleased = -1f;
            if (_captureMode == VoiceRecognitionMode.SlidingWindow && !(_recorder is ILiveVoiceRecorder))
            { SetState(VoiceInputState.Error, "슬라이딩 윈도우에는 실시간 읽기가 가능한 녹음기가 필요합니다."); return false; }
            if (!_recorder.Begin(MicrophoneDevice, MaxRecordingSeconds, out string error))
            { SetState(VoiceInputState.Error, error); return false; }
            _recordingStarted = Time.realtimeSinceStartup;
            _microphoneStarted = false; _captureActive = true;
            if (_captureMode == VoiceRecognitionMode.SlidingWindow)
            {
                SetState(VoiceInputState.Recording, "음성 대기 중 · 슬라이딩 윈도우 · M 녹음 켜짐");
                _sliding.Begin((ILiveVoiceRecorder)_recorder, _transcriber, MaxRecordingSeconds,
                    MinimumSpeechSeconds, SilenceRmsThreshold, _generation, _recordingStarted);
            }
            else
            {
                _batchMetrics = new VoiceRecognitionMetrics { Mode = VoiceRecognitionMode.BatchInference,
                    Session = _generation, Utterance = 1, CaptureStarted = _recordingStarted };
                SetState(VoiceInputState.Recording, "녹음 중 · M을 놓으면 일괄 인식합니다");
            }
            return true;
        }
        public void EndCapture()
        {
            if (!_captureActive) return;
            if (InputBlocked) { CancelCapture(); return; }
            _captureActive = false; _keyReleased = Time.realtimeSinceStartup;
            VoiceAudio audio;
            try { audio = _recorder.Stop(); }
            catch (Exception exception) { FailCapture("녹음 종료 실패: " + exception.Message); return; }
            if (_captureMode == VoiceRecognitionMode.SlidingWindow)
            {
                SetState(VoiceInputState.Recognizing, "최종 인식 확인 중 · 마이크 꺼짐");
                _sliding.EndCapture(audio);
                return;
            }
            if (audio == null || !audio.HasSpeech(MinimumSpeechSeconds, SilenceRmsThreshold))
            { SetState(VoiceInputState.Error, "무음 또는 너무 짧은 음성 · CPC에 전송하지 않았습니다"); return; }
            _batchMetrics.KeyReleased = _keyReleased;
            FindBatchSpeechBounds(audio, _batchMetrics);
            SetState(VoiceInputState.Recognizing, "일괄 음성 인식 중 · 마이크 꺼짐");
            _recognition = StartCoroutine(RecognizeBatch(audio, _generation));
        }
        private IEnumerator RecognizeBatch(VoiceAudio audio, int generation)
        {
            yield return null;
            bool completed = false;
            VoiceRecognitionMetrics metrics = _batchMetrics;
            metrics.FirstRequest = Time.realtimeSinceStartup;
            metrics.Requests = 1; metrics.AudioSecondsSent = (float)audio.Samples.Length / audio.SampleRate;
            metrics.FullUtteranceFinal = true;
            yield return _transcriber.Transcribe(audio, (text, error) =>
            {
                if (completed || generation != _generation || State != VoiceInputState.Recognizing || InputBlocked) return;
                completed = true;
                if (!string.IsNullOrEmpty(error)) { SetState(VoiceInputState.Error, error); return; }
                metrics.Text = text;
                metrics.FinalRecognized = Time.realtimeSinceStartup;
                SubmitFinal(metrics);
            });
            if (generation != _generation) yield break;
            _recognition = null;
            if (!completed && State == VoiceInputState.Recognizing) SetState(VoiceInputState.Error, "음성 인식이 완료되지 않았습니다");
        }
        private void OnSpeechStarted()
        {
            if (InputBlocked || !_captureActive) return;
            SetState(VoiceInputState.Recording, "발화 감지 · 녹음 중 실시간 인식합니다");
        }
        private void OnPartialRecognized(string text)
        {
            if (InputBlocked || !_captureActive) return;
            string waiting = Player.Companion != null && Player.Companion.Llm.HasPendingDialogue ? " · CPC 응답 대기 중" : "";
            SetState(VoiceInputState.Recording, "중간 인식(미전송): " + text + waiting);
        }
        private void OnSlidingFinal(VoiceRecognitionMetrics metrics)
        {
            if (InputBlocked || !isActiveAndEnabled || _captureMode != VoiceRecognitionMode.SlidingWindow) return;
            if (!_captureActive) metrics.KeyReleased = _keyReleased;
            SubmitFinal(metrics);
        }
        private void OnSlidingCompleted(int count)
        {
            if (State == VoiceInputState.Error) return;
            if (count == 0) SetState(VoiceInputState.Error, "무음 또는 너무 짧은 음성 · CPC에 전송하지 않았습니다");
            else SetState(VoiceInputState.WaitingForCpc, "최종 음성 전달 완료 · CPC 응답 대기 중 · " + LastTranscript);
        }
        private void SubmitFinal(VoiceRecognitionMetrics metrics)
        {
            if (string.IsNullOrWhiteSpace(metrics.Text))
            { SetState(VoiceInputState.Error, "인식된 음성이 없습니다 · CPC에 전송하지 않았습니다"); return; }
            LastTranscript = metrics.Text.Trim();
            if (LastTranscript.Length > 500) LastTranscript = LastTranscript.Substring(0, 500);
            if (!Chat.TrySendToCompanion(LastTranscript)) { SetState(VoiceInputState.Error, "CPC 입력 연결이 없습니다"); return; }
            metrics.Text = LastTranscript; metrics.Submitted = Time.realtimeSinceStartup;
            LastMetrics = metrics;
            string continued = _captureActive ? " · M 녹음 계속" : "";
            SetState(VoiceInputState.WaitingForCpc, "CPC 응답 대기 중" + continued + " · 인식: " + LastTranscript);
            FinalTextSubmitted?.Invoke(LastTranscript);
            MetricsRecorded?.Invoke(metrics);
            Debug.Log($"[CPC/Voice/Metrics] mode={metrics.Mode} utterance={metrics.Utterance} requests={metrics.Requests} end_to_submit_ms={metrics.SpeechEndToSubmitMs:F0}", this);
            if (!WriteComparisonCsv) return;
            try
            {
#if UNITY_EDITOR
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/Voice/Measurements"));
#else
                string directory = Path.Combine(Application.persistentDataPath, "VoiceMeasurements");
#endif
                string endpoint = _transcriber is WhisperLocalStt whisper ? whisper.Endpoint : "injected-test-backend";
                metrics.WriteCsv(directory, ComparisonLabel, endpoint);
            }
            catch (Exception exception) { Debug.LogWarning("[CPC/Voice/Metrics] CSV 기록 실패: " + exception.Message, this); }
        }
        private void FindBatchSpeechBounds(VoiceAudio audio, VoiceRecognitionMetrics metrics)
        {
            for (int offset = 0; offset < audio.Samples.Length; offset += 320)
            {
                int count = Mathf.Min(320, audio.Samples.Length - offset);
                double energy = 0;
                for (int i = offset; i < offset + count; i++) energy += audio.Samples[i] * audio.Samples[i];
                double rms = Math.Sqrt(energy / count);
                if (metrics.SpeechStarted < 0f && rms >= SilenceRmsThreshold)
                    metrics.SpeechStarted = _recordingStarted + (float)offset / audio.SampleRate;
                if (metrics.SpeechStarted >= 0f && rms >= SilenceRmsThreshold * .7f)
                    metrics.SpeechEnded = _recordingStarted + (float)(offset + count) / audio.SampleRate;
            }
        }
        private void CancelPending()
        {
            _generation++; _captureActive = false;
            _sliding?.Cancel(); _recorder?.Cancel(); _transcriber?.Cancel();
            if (_recognition != null) { StopCoroutine(_recognition); _recognition = null; }
        }
        public void CancelCapture() { CancelPending(); SetState(VoiceInputState.Idle, "녹음 취소 · " + ReadyText()); }
        private void FailCapture(string error) { CancelPending(); SetState(VoiceInputState.Error, error); }
        private void SetState(VoiceInputState state, string text)
        {
            State = state; StatusText = text;
            if (Chat != null) Chat.SetVoiceStatus(text);
        }
        private void OnApplicationFocus(bool focused) { _focused = focused; if (!focused) CancelCapture(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelCapture(); }
        private void OnDisable() { CancelCapture(); }
        private void OnDestroy()
        {
            if (_sliding == null) return;
            _sliding.SpeechStarted -= OnSpeechStarted; _sliding.PartialRecognized -= OnPartialRecognized;
            _sliding.FinalRecognized -= OnSlidingFinal; _sliding.Failed -= FailCapture; _sliding.Completed -= OnSlidingCompleted;
        }
    }
}
