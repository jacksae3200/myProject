using System;
using UnityEngine;

namespace CpcPrototype
{
    // 시간축은 오디오 샘플 기준이다. 네트워크가 느려져도 VAD와 녹음을 계속 처리한다.
    public sealed class SlidingVoiceBuffer
    {
        public sealed class Utterance
        {
            public int Id, Start, SpeechStart, SpeechEnd, End, VoicedSamples;
            public bool Ended, Sent;
            public string LastText;
            public int LastWindowStart = -1, LastWindowEnd = -1;
            public VoiceRecognitionMetrics Metrics;
        }
        private readonly float[] _samples;
        private readonly int _rate, _preRoll, _silence, _tail, _minimumVoiced;
        private readonly float _startThreshold, _stopThreshold;
        private int _nextId;
        public int Count { get; private set; }
        public int SampleRate => _rate;
        public Utterance Active { get; private set; }
        public event Action<Utterance> Started;
        public event Action<Utterance> Ended;
        public SlidingVoiceBuffer(int sampleRate, int maxSeconds, float preRoll, float silence, float minimumVoiced, float rmsThreshold)
        {
            _rate = sampleRate;
            _samples = new float[Mathf.Max(1, maxSeconds) * sampleRate];
            _preRoll = Mathf.RoundToInt(preRoll * sampleRate);
            _silence = Mathf.Max(1, Mathf.RoundToInt(silence * sampleRate));
            _tail = Mathf.Min(_silence, Mathf.RoundToInt(.1f * sampleRate));
            _minimumVoiced = Mathf.Max(1, Mathf.RoundToInt(minimumVoiced * sampleRate));
            _startThreshold = Mathf.Max(.000001f, rmsThreshold);
            _stopThreshold = _startThreshold * .7f;
        }
        public void Push(float[] samples, int offset, int count)
        {
            if (count <= 0) return;
            if (Count + count > _samples.Length) throw new InvalidOperationException("녹음 버퍼 제한을 초과했습니다.");
            Array.Copy(samples, offset, _samples, Count, count);
            int before = Count;
            Count += count;
            double energy = 0;
            for (int i = offset; i < offset + count; i++) energy += samples[i] * samples[i];
            float rms = (float)Math.Sqrt(energy / count);
            if (Active == null && rms >= _startThreshold)
            {
                Active = new Utterance { Id = ++_nextId, Start = Mathf.Max(0, before - _preRoll), SpeechStart = before, SpeechEnd = Count };
                Started?.Invoke(Active);
            }
            if (Active == null) return;
            if (rms >= _stopThreshold) { Active.SpeechEnd = Count; Active.VoicedSamples += count; }
            else if (Count - Active.SpeechEnd >= _silence) Close();
        }
        public void Close()
        {
            if (Active == null) return;
            Utterance utterance = Active;
            Active = null;
            utterance.End = Mathf.Min(Count, utterance.SpeechEnd + _tail);
            utterance.Ended = true;
            if (utterance.VoicedSamples >= _minimumVoiced) Ended?.Invoke(utterance);
        }
        public VoiceAudio Copy(int start, int end)
        {
            start = Mathf.Clamp(start, 0, Count);
            end = Mathf.Clamp(end, start, Count);
            var copy = new float[end - start];
            Array.Copy(_samples, start, copy, 0, copy.Length);
            return new VoiceAudio(copy, _rate);
        }
    }
}
