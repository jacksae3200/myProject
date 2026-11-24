using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CpcPrototype
{
    public enum VoiceRecognitionMode { BatchInference = 0, SlidingWindow = 1 }

    [Serializable]
    public sealed class VoiceRecognitionMetrics
    {
        public VoiceRecognitionMode Mode;
        public int Session;
        public int Utterance;
        public float CaptureStarted;
        public float SpeechStarted = -1f;
        public float SpeechEnded = -1f;
        public float KeyReleased = -1f;
        public float FirstRequest = -1f;
        public float FirstPartial = -1f;
        public float FinalRecognized = -1f;
        public float Submitted = -1f;
        public int Requests;
        public float AudioSecondsSent;
        public bool ReusedPartial;
        public bool FullUtteranceFinal;
        public string Text = "";
        public float SpeechEndToSubmitMs => Submitted >= 0f && SpeechEnded >= 0f ? (Submitted - SpeechEnded) * 1000f : -1f;

        public void WriteCsv(string directory, string label, string endpoint)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "voice-recognition.csv");
            if (!File.Exists(path)) File.WriteAllText(path,
                "utc,label,endpoint,mode,session,utterance,capture_s,speech_start_s,speech_end_s,key_release_s,first_request_s,first_partial_s,final_text_s,submit_s,speech_end_to_submit_ms,requests,audio_seconds_sent,reused_partial,full_utterance_final,text\n", Encoding.UTF8);
            string row = string.Join(",", Quote(DateTime.UtcNow.ToString("O")), Quote(label), Quote(endpoint), Mode.ToString(),
                Session.ToString(CultureInfo.InvariantCulture), Utterance.ToString(CultureInfo.InvariantCulture),
                Number(CaptureStarted), Number(SpeechStarted), Number(SpeechEnded), Number(KeyReleased), Number(FirstRequest),
                Number(FirstPartial), Number(FinalRecognized), Number(Submitted), Number(SpeechEndToSubmitMs),
                Requests.ToString(CultureInfo.InvariantCulture), Number(AudioSecondsSent), ReusedPartial.ToString(), FullUtteranceFinal.ToString(), Quote(Text));
            File.AppendAllText(path, row + "\n", Encoding.UTF8);
        }
        private static string Number(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
