using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CpcPrototype
{
    public interface IVoiceTranscriber
    {
        IEnumerator Transcribe(VoiceAudio audio, Action<string, string> completed);
        void Cancel();
    }

    [DisallowMultipleComponent]
    public sealed class WhisperLocalStt : MonoBehaviour, IVoiceTranscriber
    {
        public string Endpoint = "http://127.0.0.1:8080/inference";
        public string Language = "ko";
        [Min(1)] public int TimeoutSeconds = 90;
        private UnityWebRequest _request;
        [Serializable] private sealed class Result { public string text; }

        public IEnumerator Transcribe(VoiceAudio audio, Action<string, string> completed)
        {
            Cancel();
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri uri) || !uri.IsLoopback ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                completed(null, "STT 주소는 로컬 HTTP 서버 주소여야 합니다.");
                yield break;
            }
            var fields = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", audio.ToWav(), "speech.wav", "audio/wav"),
                new MultipartFormDataSection("language", Language),
                new MultipartFormDataSection("response_format", "json"),
                new MultipartFormDataSection("translate", "false"),
                new MultipartFormDataSection("temperature", "0.0")
            };
            using (UnityWebRequest request = UnityWebRequest.Post(Endpoint, fields))
            {
                _request = request;
                request.timeout = Mathf.Max(1, TimeoutSeconds);
                yield return request.SendWebRequest();
                if (_request != request) yield break;
                _request = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed(null, "음성 인식 실패: 로컬 whisper-server 실행 및 주소를 확인하세요. " + request.error);
                    yield break;
                }
                string text = null, error = null;
                try
                {
                    Result result = JsonUtility.FromJson<Result>(request.downloadHandler.text);
                    if (result == null || result.text == null) error = "음성 인식 응답 형식이 올바르지 않습니다.";
                    else text = result.text.Trim();
                }
                catch (Exception exception) { error = "음성 인식 결과 오류: " + exception.Message; }
                completed(text, error);
            }
        }
        public void Cancel()
        {
            if (_request == null) return;
            UnityWebRequest request = _request;
            _request = null;
            request.Abort();
            request.Dispose();
        }
        private void OnDisable() { Cancel(); }
    }
}
