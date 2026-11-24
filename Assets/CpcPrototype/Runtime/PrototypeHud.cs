using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private ThirdPersonPlayer _player;
        [SerializeField] private Health _playerHealth;
        [SerializeField] private CompanionCpc _companion;
        [SerializeField] private Camera _camera;
        private CpcChatPanel _chat;
        private GUIStyle _panelStyle;
        private GUIStyle _worldStyle;
        public void Configure(ThirdPersonPlayer player, Health health, CompanionCpc companion, Camera camera)
        { _player = player; _playerHealth = health; _companion = companion; _camera = camera; }
        private void Awake()
        {
            _chat = GetComponent<CpcChatPanel>();
            if (_chat != null) _chat.Configure(_player, _companion);
        }
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (_chat != null && !_chat.IsOpen && keyboard.enterKey.wasPressedThisFrame) _chat.Open();
        }
        private void OnDisable() { if (_player != null) _player.BlockInput(false); }
        private void OnGUI()
        {
            if (_playerHealth == null || _companion == null) return;
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, wordWrap = true };
                _worldStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                SetTextColor(_panelStyle);
                SetTextColor(_worldStyle);
                _panelStyle.padding = new RectOffset(10, 10, 8, 8);
            }
            DrawPanel(new Rect(14, 14, 530, 145),
                $"PICOCHAN + CPC / Gemma local prototype\nPlayer HP: {_playerHealth.Current:0}/{_playerHealth.MaxHealth:0}   CPC HP: {_companion.Health.Current:0}/{_companion.Health.MaxHealth:0}\n" +
                "WASD Move | Space Jump | Shift Run | LMB Melee\nRMB drag: Camera | Enter: Chat | Esc: Close chat\n" +
                (!_playerHealth.IsAlive ? "PLAYER DEAD: Stop / Play to restart" : _companion.Llm.Status + (_companion.Llm.IsBusy ? $" ({_companion.Llm.RequestElapsed:0}초)" : "")), _panelStyle);
            DrawPanel(new Rect(14, Screen.height - 130, Mathf.Min(720, Screen.width - 28), 112), _companion.Llm.LastReply, _panelStyle);
            if (_camera == null) return;
            var monsters = CubeMonsterBrain.ActiveMonsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (monster == null) continue;
                Vector3 point = _camera.WorldToScreenPoint(monster.transform.position + Vector3.up * 1.8f);
                if (point.z <= 0f) continue;
                DrawPanel(new Rect(point.x - 90, Screen.height - point.y, 180, 40),
                    $"{monster.name}: {monster.Health.Current:0} HP\n{monster.State}", _worldStyle);
            }
        }
        private static void DrawPanel(Rect rectangle, string text, GUIStyle style)
        {
            Color previous = GUI.color;
            Color previousContent = GUI.contentColor;
            GUI.color = new Color(0.84f, 0.9f, 0.94f, 0.96f);
            GUI.DrawTexture(rectangle, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.Label(rectangle, text, style);
            GUI.contentColor = previousContent;
            GUI.color = previous;
        }
        private static void SetTextColor(GUIStyle style)
        {
            style.normal.textColor = Color.black;
            style.hover.textColor = Color.black;
            style.active.textColor = Color.black;
            style.focused.textColor = Color.black;
            style.onNormal.textColor = Color.black;
            style.onHover.textColor = Color.black;
            style.onActive.textColor = Color.black;
            style.onFocused.textColor = Color.black;
        }
    }
}
