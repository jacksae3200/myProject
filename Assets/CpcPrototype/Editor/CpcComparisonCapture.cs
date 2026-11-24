using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;
using CpcPrototype;

[InitializeOnLoad]
internal static class CpcComparisonCapture
{
    private static readonly string[] MODELS = { "gemma4:26b", "qwen3.5:4b", "gemma4:12b" };
    private const string KEY = "CpcComparison.Index";
    private const string OUTPUT = "CpcCleanVideos";
    static CpcComparisonCapture()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(KEY, -1) >= 0)
                new GameObject("CPC comparison overlay").AddComponent<CpcCaptureOverlay>();
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetInt(KEY, -1) >= 0)
            {
                int next = SessionState.GetInt(KEY, -1) + 1;
                SessionState.SetInt(KEY, next < MODELS.Length ? next : -1);
                if (next < MODELS.Length) SessionState.SetBool(KEY + ".Pending", true);
                else
                {
                    if (SessionState.GetBool(KEY + ".Switched", false)) EditorSceneManager.RestoreSceneManagerSetup(_setup);
                    File.WriteAllText(Path.Combine(OUTPUT, "complete.txt"), "Finished");
                }
            }
        };
    }

    private static SceneSetup[] _setup
    {
        get => JsonUtility.FromJson<SceneSetupList>(SessionState.GetString(KEY + ".Scenes", "{}")).Scenes;
        set => SessionState.SetString(KEY + ".Scenes", JsonUtility.ToJson(new SceneSetupList { Scenes = value }));
    }

    [Serializable]
    private class SceneSetupList { public SceneSetup[] Scenes; }

    private static void Update()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(KEY + ".Pending", false))
        {
            SessionState.SetBool(KEY + ".Pending", false);
            EditorApplication.isPlaying = true;
            return;
        }
        if (!File.Exists("CpcCapture.start")) return;
        int.TryParse(File.ReadAllText("CpcCapture.start"), out int start);
        File.Delete("CpcCapture.start");
        bool switched = EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KayKit_DepthSurvival.unity";
        SessionState.SetBool(KEY + ".Switched", switched);
        if (switched && Enumerable.Range(0, EditorSceneManager.sceneCount).Any(i => EditorSceneManager.GetSceneAt(i).isDirty))
        {
            File.WriteAllText(Path.Combine(OUTPUT, "blocked.txt"), "Open scene has unsaved changes.");
            return;
        }
        _setup = EditorSceneManager.GetSceneManagerSetup();
        if (switched) EditorSceneManager.OpenScene("Assets/Scenes/KayKit_DepthSurvival.unity");
        SessionState.SetInt(KEY, Mathf.Clamp(start, 0, MODELS.Length - 1));
        EditorApplication.isPlaying = true;
    }

    internal static string Model => MODELS[SessionState.GetInt(KEY, 0)];
    internal static string Output => Path.GetFullPath(Path.Combine(OUTPUT, Model.Replace(':', '-')));
}

internal sealed class CpcCaptureOverlay : MonoBehaviour
{
    private static readonly string[] INPUTS = { "여기서 기다려.", "나를 따라와.", "지금 어디로 가는 게 좋을까?" };
    private ShieldCompanion _companion;
    private LocalLlmClient _client;
    private RecorderController _recorder;
    private Font _font;
    private float _sent;
    private float _next;
    private float _latency;
    private float _frame;
    private int _index;
    private float _walkUntil;
    private Vector3 _walkDirection;
    private bool _waiting;
    private string _reply = "—";
    private string _action = "—";
    private string _result = "—";
    private string _input = "—";

    private void Start()
    {
        Application.runInBackground = true;
        _companion = FindObjectsByType<ShieldCompanion>(FindObjectsSortMode.None).FirstOrDefault(x => x.Llm != null && x.Tactics != null);
        if (_companion == null)
        {
            File.AppendAllText("CpcCleanVideos/blocked.txt", "Tactical companion not found.\n");
            EditorApplication.isPlaying = false;
            return;
        }
        _companion.Player.HideControlsHud = true;
        _client = _companion.Llm;
        var settings = Instantiate(_client.Settings);
        settings.Model = CpcComparisonCapture.Model;
        _client.Configure(settings);
        _companion.Tactics.OnThreat = false;
        _companion.Tactics.OnDamage = false;
        _client.Replied += OnReply;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30;
        controllerSettings.CapFrameRate = false;
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.Enabled = true;
        movie.CaptureAudio = false;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1280, OutputHeight = 720 };
        movie.OutputFile = CpcComparisonCapture.Output;
        controllerSettings.AddRecorderSettings(movie);
        _recorder = new RecorderController(controllerSettings);
        _recorder.PrepareRecording();
        _recorder.StartRecording();
        _font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 19);
        _next = Time.realtimeSinceStartup + 2;
    }

    private void Update()
    {
        if (_recorder == null) return;
        float now = Time.realtimeSinceStartup;
        _frame = Mathf.Lerp(_frame, Time.unscaledDeltaTime * 1000f, .06f);
        if (now < _walkUntil)
        {
            var controller = _companion.Player.GetComponent<CharacterController>();
            if (controller != null) controller.Move(_walkDirection * (2.5f * Time.unscaledDeltaTime));
        }
        if (_waiting || now < _next) return;
        if (_index == INPUTS.Length)
        {
            _recorder.StopRecording();
            _recorder = null;
            EditorApplication.isPlaying = false;
            return;
        }
        _input = INPUTS[_index++];
        _reply = "…";
        _result = "요청 중";
        _action = "—";
        _waiting = true;
        _sent = now;
        _companion.Tactics.Chat(_input, "지금은 경계 중이야. 내 뒤를 봐 줘!");
    }

    private void OnReply(string reply)
    {
        if (!_waiting) return;
        _waiting = false;
        _latency = Time.realtimeSinceStartup - _sent;
        _reply = reply;
        _result = _client.Status;
        _action = _companion.Tactics.TryGetOrder(out var action, out _) ? action.ToString() : "—";
        _next = Time.realtimeSinceStartup + 3;
        if (_index <= 2)
        {
            _walkDirection = _companion.Player.transform.right * (_index == 1 ? 1 : -1);
            _walkUntil = Time.realtimeSinceStartup + 1.2f;
        }
        File.AppendAllText(CpcComparisonCapture.Output + ".txt",
            $"{_index}\t{_latency:F2}s\t{_result}\t{_action}\t{_input}\t{reply}\n");
    }

    private void OnGUI()
    {
        if (_client == null) return;
        float width = Mathf.Min(Screen.width - 32, 760);
        GUI.Box(new Rect(16, 16, width, 208), "");
        var style = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 19, wordWrap = true, richText = false };
        GUI.Label(new Rect(30, 22, width - 28, 196),
            $"{CpcComparisonCapture.Model}   |   CPC 비교 {_index}/{INPUTS.Length}\n" +
            $"프레임 {_frame:F1} ms (이동 평균)   |   대사 지연 {(_waiting ? Time.realtimeSinceStartup - _sent : _latency):F2} s\n" +
            $"상태: {_result}   |   승인된 행동: {_action}   |   거리: {Vector3.Distance(_companion.transform.position, _companion.Player.transform.position):F1} m\n입력: {_input}\n제임스: {_reply}", style);
    }

    private void OnDestroy()
    {
        if (_client != null) _client.Replied -= OnReply;
        _recorder?.StopRecording();
    }
}




