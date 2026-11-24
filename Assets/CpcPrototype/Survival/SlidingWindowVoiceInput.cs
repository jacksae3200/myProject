using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class SlidingWindowVoiceInput : MonoBehaviour
    {
        [Min(.5f)] public float WindowSeconds = 4f;
        [Min(.1f)] public float StepSeconds = .5f;
        [Min(.2f)] public float MinimumWindowSeconds = 1f;
        [Min(.15f)] public float SilenceSeconds = .65f;
        [Range(0f, 1f)] public float PreRollSeconds = .2f;
        [Range(1, 8)] public int MaxPendingUtterances = 3;
        public bool IsRunning { get; private set; }
        public bool IsCapturing { get; private set; }
        public bool IsRecognizing { get; private set; }
        public string PartialTranscript { get; private set; } = "";
        public int RequestsSent { get; private set; }
        public int FinalCount { get; private set; }
        public int CapturedSamples => _buffer != null ? _buffer.Count : 0;
        public event Action SpeechStarted;
        public event Action<string> PartialRecognized;
        public event Action<VoiceRecognitionMetrics> FinalRecognized;
        public event Action<string> Failed;
        public event Action<int> Completed;
        private readonly float[] _read = new float[320];
        private readonly Queue<SlidingVoiceBuffer.Utterance> _finals = new Queue<SlidingVoiceBuffer.Utterance>();
        private ILiveVoiceRecorder _recorder;
        private IVoiceTranscriber _transcriber;
        private SlidingVoiceBuffer _buffer;
        private int _epoch, _session;
        private int _lastPartialUtterance, _lastPartialEnd;
        private float _captureStarted, _nextPartial;

        public void Begin(ILiveVoiceRecorder recorder, IVoiceTranscriber transcriber, int maxSeconds,
            float minimumSpeech, float threshold, int session, float captureStarted)
        {
            Cancel();
            _recorder = recorder; _transcriber = transcriber;
            _session = session; _captureStarted = captureStarted;
            _buffer = new SlidingVoiceBuffer(recorder.SampleRate, maxSeconds, PreRollSeconds, SilenceSeconds, minimumSpeech, threshold);
            _buffer.Started += OnSpeechStarted;
            _buffer.Ended += OnSpeechEnded;
            _nextPartial = 0f;
            _lastPartialUtterance = _lastPartialEnd = 0;
            IsRunning = IsCapturing = true;
            RequestsSent = FinalCount = 0;
            PartialTranscript = "";
            StartCoroutine(Poll());
        }
        private void OnSpeechStarted(SlidingVoiceBuffer.Utterance utterance)
        {
            utterance.Metrics = new VoiceRecognitionMetrics { Mode = VoiceRecognitionMode.SlidingWindow,
                Session = _session, Utterance = utterance.Id, CaptureStarted = _captureStarted,
                SpeechStarted = _captureStarted + (float)utterance.SpeechStart / _buffer.SampleRate };
            PartialTranscript = "";
            SpeechStarted?.Invoke();
        }
        private void OnSpeechEnded(SlidingVoiceBuffer.Utterance utterance)
        {
            utterance.Metrics.SpeechEnded = _captureStarted + (float)utterance.SpeechEnd / _buffer.SampleRate;
            if (_finals.Count >= MaxPendingUtterances) { Fail("연속 발화 처리 대기 한도를 넘었습니다. 잠시 후 M을 다시 눌러 주세요."); return; }
            _finals.Enqueue(utterance);
            if (CoversUtterance(utterance)) FinalizeUtterance(utterance, true, false);
        }
        private IEnumerator Poll()
        {
            var pause = new WaitForSecondsRealtime(.02f);
            yield return null;
            while (IsRunning)
            {
                if (IsCapturing)
                {
                    string error = null;
                    try
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int count = _recorder.ReadAvailable(_read);
                            if (count <= 0) break;
                            _buffer.Push(_read, 0, count);
                            if (!IsRunning) break;
                        }
                    }
                    catch (Exception exception) { error = "실시간 녹음 실패: " + exception.Message; }
                    if (error != null) { Fail(error); yield break; }
                }
                if (!IsRunning) yield break;
                Schedule();
                if (!IsCapturing && !IsRecognizing && _finals.Count == 0)
                {
                    IsRunning = false;
                    Completed?.Invoke(FinalCount);
                    yield break;
                }
                yield return pause;
            }
        }
        private void Schedule()
        {
            if (IsRecognizing || !IsRunning) return;
            while (_finals.Count > 0 && _finals.Peek().Sent) _finals.Dequeue();
            if (_finals.Count > 0)
            {
                var final = _finals.Peek();
                if (CoversUtterance(final)) { FinalizeUtterance(final, true, false); return; }
                StartRequest(final, final.Start, final.End, true);
                return;
            }
            var active = _buffer.Active;
            if (!IsCapturing || active == null || Time.realtimeSinceStartup < _nextPartial ||
                _buffer.Count - active.Start < MinimumWindowSeconds * _buffer.SampleRate) return;
            int end = _buffer.Count;
            if (_lastPartialUtterance == active.Id && end <= _lastPartialEnd) return;
            int start = Mathf.Max(active.Start, end - Mathf.RoundToInt(Mathf.Max(MinimumWindowSeconds, WindowSeconds) * _buffer.SampleRate));
            _nextPartial = Time.realtimeSinceStartup + Mathf.Max(.1f, StepSeconds);
            _lastPartialUtterance = active.Id; _lastPartialEnd = end;
            StartRequest(active, start, end, false);
        }
        private void StartRequest(SlidingVoiceBuffer.Utterance utterance, int start, int end, bool final)
        {
            IsRecognizing = true;
            RequestsSent++;
            utterance.Metrics.Requests++;
            utterance.Metrics.AudioSecondsSent += (float)(end - start) / _buffer.SampleRate;
            if (utterance.Metrics.FirstRequest < 0f) utterance.Metrics.FirstRequest = Time.realtimeSinceStartup;
            StartCoroutine(Request(utterance, start, end, final, _epoch));
        }
        private IEnumerator Request(SlidingVoiceBuffer.Utterance utterance, int start, int end, bool final, int epoch)
        {
            bool handled = false;
            IEnumerator routine = _transcriber.Transcribe(_buffer.Copy(start, end), (text, error) =>
            {
                if (handled || epoch != _epoch || !IsRunning || utterance.Sent) return;
                handled = true;
                if (!string.IsNullOrEmpty(error)) { Fail(error); return; }
                utterance.LastText = text != null ? text.Trim() : "";
                utterance.LastWindowStart = start; utterance.LastWindowEnd = end;
                if (final || utterance.Ended && CoversUtterance(utterance))
                { FinalizeUtterance(utterance, !final, final); return; }
                if (ReferenceEquals(_buffer.Active, utterance) && !string.IsNullOrEmpty(utterance.LastText))
                {
                    if (utterance.Metrics.FirstPartial < 0f) utterance.Metrics.FirstPartial = Time.realtimeSinceStartup;
                    PartialTranscript = utterance.LastText;
                    PartialRecognized?.Invoke(PartialTranscript);
                }
            });
            while (epoch == _epoch && IsRunning)
            {
                bool more = false; object next = null; string failure = null;
                try { more = routine.MoveNext(); if (more) next = routine.Current; }
                catch (Exception exception) { failure = "음성 인식 처리 오류: " + exception.Message; }
                if (failure != null) { Fail(failure); yield break; }
                if (!more) break;
                yield return next;
            }
            (routine as IDisposable)?.Dispose();
            if (epoch != _epoch) yield break;
            IsRecognizing = false;
            if (!handled && IsRunning) Fail("실시간 음성 인식이 완료되지 않았습니다.");
        }
        private bool CoversUtterance(SlidingVoiceBuffer.Utterance utterance) => utterance.Ended &&
            !string.IsNullOrWhiteSpace(utterance.LastText) && utterance.LastWindowStart <= utterance.Start &&
            utterance.LastWindowStart >= 0 && utterance.LastWindowEnd >= utterance.SpeechEnd;
        private void FinalizeUtterance(SlidingVoiceBuffer.Utterance utterance, bool reused, bool fullFinal)
        {
            if (utterance.Sent || !utterance.Ended) return;
            if (string.IsNullOrWhiteSpace(utterance.LastText)) { Fail("최종 인식 결과가 없습니다. CPC에 전송하지 않았습니다."); return; }
            utterance.Sent = true;
            utterance.Metrics.Text = utterance.LastText;
            utterance.Metrics.FinalRecognized = Time.realtimeSinceStartup;
            utterance.Metrics.ReusedPartial = reused;
            utterance.Metrics.FullUtteranceFinal = fullFinal;
            FinalCount++;
            FinalRecognized?.Invoke(utterance.Metrics);
        }
        public void EndCapture(VoiceAudio finalAudio)
        {
            if (!IsRunning || !IsCapturing) return;
            IsCapturing = false;
            if (finalAudio != null)
            {
                if (finalAudio.SampleRate != _buffer.SampleRate) { Fail("녹음 중 샘플레이트가 바뀌었습니다."); return; }
                for (int offset = _buffer.Count; offset < finalAudio.Samples.Length; offset += 320)
                {
                    _buffer.Push(finalAudio.Samples, offset, Mathf.Min(320, finalAudio.Samples.Length - offset));
                    if (!IsRunning) return;
                }
            }
            _buffer.Close();
            foreach (var utterance in _finals) utterance.Metrics.KeyReleased = Time.realtimeSinceStartup;
        }
        private void Fail(string message)
        {
            if (!IsRunning) return;
            IsRunning = IsCapturing = false;
            Failed?.Invoke(message);
        }
        public void Cancel()
        {
            _epoch++;
            IsRunning = IsCapturing = IsRecognizing = false;
            StopAllCoroutines();
            _transcriber?.Cancel();
            _finals.Clear();
            _buffer = null;
        }
        private void OnDisable() { Cancel(); }
    }
}
