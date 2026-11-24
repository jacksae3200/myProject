using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace CpcPrototype
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class CpcChatPanel : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;
        private UIDocument _document;
        private System.Action<bool> _setInputBlocked;
        private System.Action<string> _sendMessage;
        private VisualElement _panel;
        private TextField _input;
        private Label _hint;
        private Label _voiceStatus;
        private string _voiceStatusText = "M 누르는 동안 말하기";
        private Keyboard _keyboard;
        private Font _font;
        private string _composition = "";
        private int _compositionEndedFrame = -10;
        private int _openedFrame;
        private Coroutine _submitRoutine;
        public bool IsOpen { get; private set; }
        public string Draft => _input != null ? _input.value : "";

        public void Configure(ThirdPersonPlayer player, CompanionCpc companion)
        { Configure(player.BlockInput, companion.SayToCompanion); }
        public void Configure(System.Action<bool> setInputBlocked, System.Action<string> sendMessage)
        { _setInputBlocked = setInputBlocked; _sendMessage = sendMessage; }
        public void ConfigurePanel(PanelSettings settings) { _panelSettings = settings; }
        public bool TrySendToCompanion(string message)
        {
            if (!isActiveAndEnabled || _sendMessage == null || string.IsNullOrWhiteSpace(message)) return false;
            _sendMessage(message.Trim());
            return true;
        }
        public void SetVoiceStatus(string status)
        {
            _voiceStatusText = status;
            if (_voiceStatus != null) _voiceStatus.text = status;
        }

        private void Start()
        {
            _document = GetComponent<UIDocument>();
            if (_panelSettings == null)
            {
                Debug.LogError("[CPC/Chat] ChatPanelSettings 에셋을 할당하세요.", this);
                enabled = false; return;
            }
            _document.panelSettings = _panelSettings;
            VisualElement root = _document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans CJK KR", "Arial" }, 18);
            root.style.unityFont = _font;
            _voiceStatus = new Label(_voiceStatusText) { name = "cpc-voice-status", pickingMode = PickingMode.Ignore };
            _voiceStatus.style.position = Position.Absolute;
            _voiceStatus.style.left = 14; _voiceStatus.style.bottom = 105;
            _voiceStatus.style.maxWidth = Length.Percent(85);
            _voiceStatus.style.whiteSpace = WhiteSpace.Normal;
            _voiceStatus.style.fontSize = 18;
            _voiceStatus.style.color = Color.white;
            _voiceStatus.style.backgroundColor = new Color(.08f, .12f, .19f, .9f);
            _voiceStatus.style.paddingLeft = 8; _voiceStatus.style.paddingRight = 8;
            root.Add(_voiceStatus);
            _panel = new VisualElement { name = "cpc-chat-panel" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = 14; _panel.style.bottom = 145;
            _panel.style.width = Length.Percent(65); _panel.style.maxWidth = 720;
            _panel.style.paddingLeft = 12; _panel.style.paddingRight = 12;
            _panel.style.paddingTop = 10; _panel.style.paddingBottom = 10;
            _panel.style.backgroundColor = new Color(0.08f, 0.12f, 0.19f, 0.98f);
            _hint = new Label("제임스에게 말하기 · Enter 전송 / Esc 닫기");
            _hint.style.color = Color.white; _hint.style.fontSize = 16;
            _panel.Add(_hint);
            VisualElement row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            _input = new TextField { name = "cpc-chat-input", maxLength = 500, multiline = false, isDelayed = false, selectAllOnFocus = false };
            _input.style.flexGrow = 1; _input.style.minWidth = 0; _input.style.height = 38;
            _input.style.fontSize = 18;
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            row.Add(_input);
            Button send = new Button(RequestSubmit) { text = "전송", name = "cpc-chat-send" };
            send.style.width = 80; send.style.height = 38; send.style.fontSize = 18;
            row.Add(send); _panel.Add(row); root.Add(_panel);
            _panel.style.display = DisplayStyle.None;
        }

        public void Open()
        {
            if (_input == null || !isActiveAndEnabled) return;
            IsOpen = true; _openedFrame = Time.frameCount;
            _setInputBlocked?.Invoke(true);
            _panel.style.display = DisplayStyle.Flex;
            BindKeyboard();
            // 창을 여는 Enter가 같은 프레임에 전송 이벤트로 전달되지 않게 한다.
            _input.schedule.Execute(() => { if (IsOpen) _input.Focus(); }).ExecuteLater(1);
        }

        public void Close()
        {
            IsOpen = false;
            if (_input != null) _input.Blur();
            if (_panel != null) _panel.style.display = DisplayStyle.None;
            UnbindKeyboard();
            _setInputBlocked?.Invoke(false);
        }

        private void BindKeyboard()
        {
            if (_keyboard == Keyboard.current) return;
            UnbindKeyboard(); _keyboard = Keyboard.current;
            if (_keyboard == null) return;
            _keyboard.onIMECompositionChange += OnCompositionChanged;
            _keyboard.SetIMEEnabled(true);
        }
        private void UnbindKeyboard()
        {
            if (_keyboard != null)
            {
                _keyboard.onIMECompositionChange -= OnCompositionChanged;
                if (_keyboard.added) _keyboard.SetIMEEnabled(false);
            }
            _keyboard = null; _composition = "";
        }
        private void OnCompositionChanged(IMECompositionString composition)
        {
            string next = composition.ToString();
            if (_composition.Length > 0 && next.Length == 0) _compositionEndedFrame = Time.frameCount;
            _composition = next;
        }
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            { evt.StopImmediatePropagation(); evt.PreventDefault(); Close(); return; }
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            // IME 조합 확정 Enter는 네이티브 TextField가 처리하며 전송하지 않는다.
            if (_composition.Length > 0 || Time.frameCount <= _compositionEndedFrame + 1) return;
            evt.StopImmediatePropagation(); evt.PreventDefault();
            if (Time.frameCount > _openedFrame + 1) RequestSubmit();
        }

        private void Update()
        {
            if (!IsOpen) return;
            BindKeyboard();
            if (_keyboard != null && _keyboard.escapeKey.wasPressedThisFrame) Close();
        }
        private void RequestSubmit()
        {
            if (!IsOpen || _submitRoutine != null) return;
            _submitRoutine = StartCoroutine(SubmitAfterCommit());
        }
        private IEnumerator SubmitAfterCommit()
        {
            // 버튼 클릭에 따른 포커스 이동과 마지막 한글 음절 확정을 먼저 반영한다.
            yield return null; yield return null;
            if (IsOpen)
            {
                if (_composition.Length > 0)
                {
                    _hint.text = "한글 조합을 확정한 뒤 Enter 또는 전송을 눌러 주세요.";
                    _input.Focus();
                }
                else if (!string.IsNullOrWhiteSpace(_input.value))
                {
                    string message = _input.value.Trim();
                    TrySendToCompanion(message);
                    _input.value = "";
                    _hint.text = "제임스에게 말하기 · Enter 전송 / Esc 닫기";
                    Close();
                }
            }
            _submitRoutine = null;
        }
        private void OnApplicationFocus(bool focused)
        {
            if (focused && IsOpen && _input != null) { BindKeyboard(); _input.Focus(); }
        }
        private void OnDisable()
        {
            StopAllCoroutines(); _submitRoutine = null; Close();
        }
        private void OnDestroy() { if (_font != null) Destroy(_font); }
    }
}
