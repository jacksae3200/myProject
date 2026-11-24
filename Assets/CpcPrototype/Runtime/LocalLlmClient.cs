using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CpcPrototype
{
    public sealed class LocalLlmClient : MonoBehaviour
    {
        [Serializable] private sealed class Message
        {
            public string role;
            public string content;
            public Message(string messageRole, string text) { role = messageRole; content = text; }
        }
        [Serializable] private sealed class Options { public float temperature; public int num_predict; public int num_ctx; public int num_gpu; }
        [Serializable] private sealed class Request
        {
            public string model;
            public bool stream = false;
            public bool think = false;
            public string keep_alive = "5m";
            public Message[] messages;
            public Options options;
        }
        [Serializable] private sealed class Response
        {
            public Message message;
            public string error;
            // Ollama JSON 키와 일치해야 JsonUtility가 역직렬화할 수 있다.
            public long prompt_eval_count;
            public long eval_count;
            public long eval_duration;
        }
        private sealed class Pending
        {
            public string Text;
            public string Fallback;
            public bool Dialogue;
            public Func<string, string> Decode;
            public string HistoryText;
        }
        [SerializeField] private CpcLlmSettings _settings;
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private readonly Queue<Pending> _dialogueQueue = new Queue<Pending>();
        private readonly List<Message> _history = new List<Message>();
        private UnityWebRequest _activeRequest;
        private Coroutine _worker;
        private float _retryAfter;
        private bool _dialogueActive;
        private bool _cancelEvent;
        private bool _previousBackground;
        private float _requestStarted;
        private string _status = "대화 준비";
        [Tooltip("CUDA 초기화/메모리 오류 때 같은 모델을 CPU로 한 번 재시도합니다.")]
        public bool RetryGpuFailuresOnCpu = true;
        public bool CpuFallbackActive { get; private set; }
        private const int MAX_PENDING = 8;
        private const int MAX_HISTORY = 8;
        private const string STORY_DIALOGUE_RULES =
            "\n[플레이어와의 대화]\n" +
            "동료의 행방, 각자의 과거, 생존자, 피난처, 이 세계에서 겪은 일은 모두 게임 세계에 관한 질문이다. " +
            "이런 질문에는 경계 대사로 화제를 돌리지 말고 제임스가 아는 범위에서 질문에 먼저 답한다. " +
            "확정된 설정이 없다면 앞선 대화와 모순되지 않는 짧은 과거 일화나 단서를 만들어도 된다. " +
            "확인하지 못한 현재 위치나 생사는 마지막으로 본 곳, 추측, 또는 모르는 사실로 표현한다. " +
            "예: 다른 동료들의 행방을 물으면 '대피소로 가던 중에 흩어졌어. 그 뒤로는 연락이 끊겼고.'처럼 답한다.";
        public event Action<string> Replied;
        public string LastReply { get; private set; } = "조심해 주변에 좀비가 있을 수 있어";
        public bool IsBusy => _worker != null;
        public bool HasPendingDialogue => _dialogueActive || _dialogueQueue.Count > 0;
        public CpcLlmSettings Settings => _settings;
        public string Status => _status;
        public float RequestElapsed => _activeRequest != null ? Time.realtimeSinceStartup - _requestStarted : 0f;
        public void Configure(CpcLlmSettings settings) { if (_settings != settings) CpuFallbackActive = false; _settings = settings; }
        public void PublishFeedback(string message) { if (!string.IsNullOrWhiteSpace(message)) Publish(message, false); }

        private void OnEnable()
        {
            _previousBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        public void Enqueue(string text, string fallback, bool dialogue = false, Func<string, string> decode = null, string historyText = null)
        {
            if (!isActiveAndEnabled) return;
            Pending item = new Pending { Text = text, Fallback = fallback, Dialogue = dialogue, Decode = decode, HistoryText = historyText };
            if (dialogue)
            {
                // 실제 대화는 입력 순서대로 처리하고 오래된 사건 대기열은 비운다.
                if (_dialogueQueue.Count >= MAX_PENDING)
                {
                    _status = "대화 요청이 많습니다. 응답을 받은 뒤 다시 전송하세요.";
                    Debug.LogWarning("[CPC/LLM] " + _status, this);
                    return;
                }
                _dialogueQueue.Enqueue(item); _queue.Clear();
                _status = "채팅 전송 대기";
                // 알림 생성에 막혀 플레이어 대화가 오래 대기하지 않도록 알림만 취소한다.
                if (_activeRequest != null && !_dialogueActive)
                { _cancelEvent = true; _activeRequest.Abort(); }
                Debug.Log("[CPC/HTTP] 채팅 요청 접수", this);
            }
            else
            {
                // 즉시 사건 로그는 CompanionCpc가 남긴다. 대화 중에는 알림 생성 요청을 생략한다.
                if (_dialogueActive || _dialogueQueue.Count > 0) return;
                if (_queue.Count >= MAX_PENDING) _queue.Dequeue();
                _queue.Enqueue(item);
            }
            if (_worker == null) _worker = StartCoroutine(ProcessQueue());
        }

        private IEnumerator ProcessQueue()
        {
            // StartCoroutine이 동기 완료되기 전에 핸들이 저장되도록 한 프레임 양보한다.
            yield return null;
            while (_dialogueQueue.Count > 0 || _queue.Count > 0)
            {
                Pending item = _dialogueQueue.Count > 0 ? _dialogueQueue.Dequeue() : _queue.Dequeue();
                _dialogueActive = item.Dialogue;
                if (_settings == null || !_settings.EnableLlm || Time.unscaledTime < _retryAfter)
                {
                    _status = _settings == null ? "모델 설정 없음: 기본 대사" : !_settings.EnableLlm ? "LLM 꺼짐: 기본 대사" : "서버 재연결 대기: 기본 대사";
                    if (!string.IsNullOrEmpty(item.Fallback)) Publish(item.Fallback, false);
                    continue;
                }
                int promptLimit = item.Decode != null ? 8000 : 4000;
                string prompt = item.Text.Length > promptLimit ? item.Text.Substring(0, promptLimit) : item.Text;
                string system = _settings.SystemPrompt;
                if (item.Dialogue && item.Decode == null) system += STORY_DIALOGUE_RULES;
                if (item.Decode != null) system = system.Replace("너의 이동, 점프, 전투 판정은 게임 시스템이 담당하므로 명령이나 코드를 출력하지 않는다.", "행동의 실행과 판정은 Unity가 담당하고, 행동의 선택은 네가 한다.") + "\n이 요청에서는 행동 선택도 네 역할이다. Unity가 선택을 검사한 뒤 실행한다. 출력 형식은 JSON이며 이 형식이 앞의 대사 전용 형식보다 우선한다. speech에 대사를 쓰고, 플레이어가 행동을 부탁하면 action에 허용 목록의 행동을 선택한다.";
                List<Message> messages = new List<Message>(_history.Count + 2) { new Message("system", system) };
                messages.AddRange(_history); messages.Add(new Message("user", prompt));
                Request payload = new Request
                {
                    model = _settings.Model, messages = messages.ToArray(),
                    options = new Options { temperature = _settings.Temperature, num_predict = item.Decode != null ? Mathf.Max(160, _settings.MaxOutputTokens) : _settings.MaxOutputTokens,
                        num_ctx = item.Decode != null ? Mathf.Max(4096, _settings.ContextTokens) : _settings.ContextTokens, num_gpu = CpuFallbackActive ? 0 : _settings.GpuLayers }
                };
                string responseText = null;
                string error = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    using (_activeRequest = new UnityWebRequest(_settings.Endpoint, "POST"))
                    {
                        _requestStarted = Time.realtimeSinceStartup;
                        _status = item.Dialogue ? "제임스가 답변을 생성하고 있습니다" : "주변 상황 대사 생성 중";
                        Debug.Log($"[CPC/HTTP] POST {_settings.Endpoint} / {_settings.Model} / {(item.Dialogue ? "chat" : "event")}", this);
                        string json = JsonUtility.ToJson(payload);
                        if (item.Decode != null) json = json.Insert(json.Length - 1, ",\"format\":\"json\"");
                        _activeRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                        _activeRequest.downloadHandler = new DownloadHandlerBuffer();
                        _activeRequest.SetRequestHeader("Content-Type", "application/json");
                        _activeRequest.timeout = Mathf.Max(5, _settings.TimeoutSeconds);
                        yield return _activeRequest.SendWebRequest();
                        if (_activeRequest.result != UnityWebRequest.Result.Success)
                        {
                            string detail = _activeRequest.downloadHandler.text;
                            if (detail.Length > 600) detail = detail.Substring(0, 600);
                            error = _activeRequest.error + " " + detail;
                        }
                        else responseText = _activeRequest.downloadHandler.text;
                    }
                    _activeRequest = null;
                    bool gpuFailure = error != null &&
                        (error.IndexOf("CUDA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         error.IndexOf("out of memory", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!_cancelEvent && attempt == 0 && RetryGpuFailuresOnCpu && payload.options.num_gpu != 0 && gpuFailure)
                    {
                        CpuFallbackActive = true;
                        payload.options.num_gpu = 0;
                        error = null; responseText = null;
                        Debug.LogWarning("[CPC/LLM] GPU initialization failed; retrying the same model once on CPU.", this);
                        continue;
                    }
                    break;
                }
                if (_cancelEvent)
                { _cancelEvent = false; _dialogueActive = false; continue; }
                string reply = null;
                if (error == null)
                {
                    try
                    {
                        Response response = JsonUtility.FromJson<Response>(responseText);
                        if (response != null && response.message != null && string.IsNullOrEmpty(response.error))
                        {
                            // eval_duration 원본 단위는 나노초이며, 읽기 편하도록 초도 함께 표시한다.
                            Debug.Log($"[CPC/Ollama Metrics] {(item.Dialogue ? "chat" : "event")} / " +
                                $"prompt_eval_count={response.prompt_eval_count}, eval_count={response.eval_count}, " +
                                $"eval_duration={response.eval_duration} ns ({response.eval_duration / 1_000_000_000.0:F3} s)", this);
                        }
                        if (response != null && response.message != null) reply = response.message.content;
                        if (!string.IsNullOrWhiteSpace(reply) && item.Decode != null) reply = item.Decode(reply);
                        if (reply == null || item.Decode == null && string.IsNullOrWhiteSpace(reply)) error = "응답 대사가 비어 있습니다.";
                    }
                    catch (Exception exception) { error = exception.Message; }
                }
                if (error != null)
                {
                    _status = "응답 실패: " + error;
                    Debug.LogWarning($"[CPC/LLM] {error}. 기본 대사를 사용합니다. 주소와 모델명을 확인하세요.", this);
                    _retryAfter = Time.unscaledTime + 10f;
                    if (!string.IsNullOrEmpty(item.Fallback)) Publish(item.Fallback, false);
                }
                else
                {
                    _status = "답변 도착";
                    reply = reply.Trim();
                    if (reply.Length > 1000) reply = reply.Substring(0, 1000);
                    if (item.Decode != null && reply.Length == 0) continue;
                    reply = Publish(reply, true);
                    if (!item.Dialogue && item.Decode != null) continue;
                    _history.Add(new Message("user", item.HistoryText ?? prompt)); _history.Add(new Message("assistant", reply));
                    while (_history.Count > MAX_HISTORY) _history.RemoveRange(0, 2);
                }
                yield return null;
            }
            _dialogueActive = false;
            _worker = null;
        }

        private string Publish(string reply, bool generated)
        {
            reply = CpcResponseFilter.Filter(reply, _settings != null ? _settings.ResponsePolicy : null, out bool blocked);
            if (blocked)
            {
                _status = "표현 필터 적용: 대체 대사";
                Debug.LogWarning("[CPC/Filter] 금지 표현을 감지하여 대사를 교체했습니다.", this);
            }
            LastReply = reply;
            Debug.Log($"[CPC/{(generated ? "Gemma" : "Fallback")}] {reply}", this);
            Replied?.Invoke(reply);
            return reply;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_activeRequest != null) { _activeRequest.Abort(); _activeRequest.Dispose(); _activeRequest = null; }
            _worker = null; _queue.Clear(); _dialogueQueue.Clear();
            _dialogueActive = false; _cancelEvent = false;
            Application.runInBackground = _previousBackground;
        }
    }
}






