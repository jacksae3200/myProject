using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CpcPrototype
{
    public sealed class VoiceAudio
    {
        public readonly float[] Samples;
        public readonly int SampleRate;
        public VoiceAudio(float[] samples, int sampleRate) { Samples = samples; SampleRate = sampleRate; }
        public bool HasSpeech(float minimumSeconds, float minimumRms)
        {
            if (Samples == null || SampleRate <= 0 || Samples.Length < SampleRate * minimumSeconds) return false;
            double energy = 0;
            for (int i = 0; i < Samples.Length; i++) energy += Samples[i] * Samples[i];
            return Math.Sqrt(energy / Samples.Length) >= minimumRms;
        }
        public byte[] ToWav()
        {
            using (var stream = new MemoryStream(44 + Samples.Length * 2))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + Samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
                writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(Samples.Length * 2);
                for (int i = 0; i < Samples.Length; i++) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(Samples[i], -1f, 1f) * 32767f));
                return stream.ToArray();
            }
        }
    }

    public interface IVoiceRecorder
    {
        bool Begin(string device, int maxSeconds, out string error);
        bool HasStarted { get; }
        VoiceAudio Stop();
        void Cancel();
    }

    public interface ILiveVoiceRecorder : IVoiceRecorder
    {
        int SampleRate { get; }
        // 고정 크기 모노 버퍼에 아직 읽지 않은 샘플을 채운다. 데이터가 부족하면 0.
        int ReadAvailable(float[] destination);
    }

    public sealed class UnityVoiceRecorder : ILiveVoiceRecorder
    {
        private AudioClip _clip;
        private string _device;
        private int _readCursor;
        private float[] _liveInterleaved;
        public int SampleRate => _clip != null ? _clip.frequency : 16000;
        public bool HasStarted => _clip != null && Microphone.GetPosition(_device) > 0;
        public bool Begin(string device, int maxSeconds, out string error)
        {
            Cancel();
            error = null;
            if (Microphone.devices.Length == 0) { error = "마이크 없음 또는 접근 권한 거부. Windows 마이크 설정을 확인하세요."; return false; }
            _device = string.IsNullOrWhiteSpace(device) ? Microphone.devices[0] : device;
            if (Array.IndexOf(Microphone.devices, _device) < 0) { error = "지정한 마이크를 찾을 수 없습니다."; return false; }
            try
            {
                _clip = Microphone.Start(_device, false, Mathf.Max(1, maxSeconds), 16000);
                if (_clip != null)
                {
                    _readCursor = 0;
                    _liveInterleaved = new float[320 * _clip.channels];
                    return true;
                }
                error = "마이크를 시작하지 못했습니다. Windows의 데스크톱 앱 마이크 권한을 확인하세요.";
            }
            catch (Exception exception) { error = "마이크 접근 실패: " + exception.Message; }
            Cancel();
            return false;
        }
        public VoiceAudio Stop()
        {
            if (_clip == null) return null;
            try
            {
                int frames = Microphone.GetPosition(_device);
                // 키를 놓는 즉시 장치를 먼저 해제하고, 저장된 오디오만 읽는다.
                Microphone.End(_device);
                if (frames <= 0) return null;
                var interleaved = new float[frames * _clip.channels];
                if (!_clip.GetData(interleaved, 0)) return null;
                var mono = new float[frames];
                for (int frame = 0; frame < frames; frame++)
                {
                    float value = 0f;
                    for (int channel = 0; channel < _clip.channels; channel++) value += interleaved[frame * _clip.channels + channel];
                    mono[frame] = value / _clip.channels;
                }
                return new VoiceAudio(mono, _clip.frequency);
            }
            finally { Cancel(); }
        }
        public int ReadAvailable(float[] destination)
        {
            if (_clip == null || destination == null || destination.Length != 320) return 0;
            int position = Microphone.GetPosition(_device);
            if (position - _readCursor < destination.Length) return 0;
            if (!_clip.GetData(_liveInterleaved, _readCursor)) return 0;
            for (int i = 0; i < destination.Length; i++)
            {
                float value = 0f;
                for (int channel = 0; channel < _clip.channels; channel++) value += _liveInterleaved[i * _clip.channels + channel];
                destination[i] = value / _clip.channels;
            }
            _readCursor += destination.Length;
            return destination.Length;
        }
        public void Cancel()
        {
            if (_clip != null)
            {
                Microphone.End(_device);
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }
            _readCursor = 0;
            _liveInterleaved = null;
        }
    }
}
