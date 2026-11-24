using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace AnimationPreview.Editor
{
    /// <summary>Records the existing three-character animation sequence at four texture resolutions.</summary>
    [InitializeOnLoad]
    public static class PixelComparisonRecorder
    {
        private const string ARMED_KEY = "PixelComparisonRecorder.Armed";
        private const int FRAME_RATE = 30;
        private static readonly int[] RESOLUTIONS = { 32, 64, 128, 256 };
        private static readonly List<RenderTexture> TEXTURES = new List<RenderTexture>();
        private static readonly List<string> CHAPTERS = new List<string>();
        private static RecorderController _controller;
        private static Animator[] _animators;
        private static int _stage;
        private static int _lastFrame = -1;
        private static float _finishTime = -1f;
        private static float _recordStart;
        private static double _nextStatus;
        private static string _outputDirectory;
        private static string _outputName;
        public static string Status { get; private set; } = "Idle";

        static PixelComparisonRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(ARMED_KEY, false) && EditorApplication.isPlaying)
                EditorApplication.delayCall += Begin;
        }

        public static void Arm()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Start outside Play mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
                throw new InvalidOperationException("Open SampleScene first.");
            SessionState.SetBool(ARMED_KEY, true);
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ARMED_KEY, false))
                EditorApplication.delayCall += Begin;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Tick;
                _controller?.StopRecording();
                _controller = null;
                SessionState.SetBool(ARMED_KEY, false);
            }
        }

        private static void Begin()
        {
            if (_controller != null || !EditorApplication.isPlaying) return;
            try
            {
                Application.runInBackground = true;
                _outputDirectory = Path.GetFullPath("Recordings/CharacterPixelComparison");
                Directory.CreateDirectory(_outputDirectory);
                _outputName = "SampleScene_Green_3D_and_Pixel_32_64_128_256_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _animators = UnityEngine.Object.FindObjectsByType<Animator>()
                    .Where(a => a.runtimeAnimatorController != null).ToArray();
                if (_animators.Length != 3) throw new InvalidOperationException("Expected three character animators.");
                SetupGreenComparison();

                // Clone render targets and Quad materials in Play mode; never resize asset textures.
                var replacements = new Dictionary<Texture, RenderTexture>();
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>()
                    .Where(c => c.name.StartsWith("CharacterCamera") && c.targetTexture != null))
                {
                    var source = camera.targetTexture;
                    var descriptor = source.descriptor;
                    descriptor.width = descriptor.height = RESOLUTIONS[0];
                    var texture = new RenderTexture(descriptor) { filterMode = FilterMode.Point, name = "Recording_" + source.name };
                    texture.Create();
                    replacements.Add(source, texture);
                    TEXTURES.Add(texture);
                    camera.targetTexture = texture;
                }
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
                {
                    var source = renderer.sharedMaterial;
                    if (source == null || source.mainTexture == null || !replacements.ContainsKey(source.mainTexture)) continue;
                    var material = new Material(source);
                    material.mainTexture = replacements[source.mainTexture];
                    renderer.sharedMaterial = material;
                }

                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                settings.FrameRate = FRAME_RATE;
                settings.CapFrameRate = false;
                settings.FrameRatePlayback = FrameRatePlayback.Constant;
                settings.SetRecordModeToManual();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "Character Pixel Comparison";
                movie.Enabled = true;
                movie.CaptureAudio = false;
                movie.CaptureAlpha = false;
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
                };
                movie.OutputFile = Path.Combine(_outputDirectory, _outputName);
                settings.AddRecorderSettings(movie);
                _controller = new RecorderController(settings);
                _controller.PrepareRecording();
                _recordStart = Time.time;
                SetStage(0);
                if (!_controller.StartRecording()) throw new InvalidOperationException("Recorder could not start.");
                EditorApplication.isPaused = false;
                EditorApplication.update += Tick;
                WriteStatus("Recording");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                WriteStatus("Failed: " + exception.Message);
                _controller?.StopRecording();
                _controller = null;
                SessionState.SetBool(ARMED_KEY, false);
            }
        }

        private static void SetStage(int stage)
        {
            _stage = stage;
            _finishTime = -1f;
            foreach (var texture in TEXTURES)
            {
                texture.Release();
                texture.width = texture.height = RESOLUTIONS[stage];
                texture.Create();
            }
            foreach (var animator in _animators)
            {
                animator.Rebind();
                animator.Play("Base Layer.01_BlockingLoop", 0, 0f);
                animator.Update(0f);
            }
            CHAPTERS.Add((Time.time - _recordStart).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " seconds: " + RESOLUTIONS[stage] + "x" + RESOLUTIONS[stage]);
            File.WriteAllLines(Path.Combine(_outputDirectory, _outputName + "_chapters.txt"), CHAPTERS);
        }

        private static void SetupGreenComparison()
        {
            var originals = _animators.OrderBy(a => a.transform.position.x).ToArray();
            var all = new List<Animator>(originals);
            var quads = UnityEngine.Object.FindObjectsByType<MeshRenderer>()
                .Where(r => r.name.StartsWith("Quad")).OrderBy(r => r.transform.position.x).ToArray();
            // The texture camera covers 2 * 3 * tan(30 degrees) world units vertically.
            // Match the live models to the same scale as that image on each 6.22-unit Quad.
            float displayScale = 6.22f / (6f * Mathf.Tan(30f * Mathf.Deg2Rad));
            for (int i = 0; i < originals.Length; i++)
            {
                var clone = UnityEngine.Object.Instantiate(originals[i].gameObject);
                clone.name = originals[i].name + "_Live3DComparison";
                foreach (var child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 0;
                clone.transform.position = new Vector3((i - 1) * 4f, 0.7f, 3f);
                clone.transform.localScale = originals[i].transform.localScale * displayScale;
                all.Add(clone.GetComponent<Animator>());
                quads[i].transform.position = new Vector3((i - 1) * 4f, -2.2f, 3f);
            }
            _animators = all.ToArray();
            var main = Camera.main;
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = new Color(0f, 1f, 0f, 1f);
            main.orthographic = true;
            main.orthographicSize = 4.7f;
            main.transform.SetPositionAndRotation(new Vector3(0f, 0.25f, -10f), Quaternion.identity);
            main.cullingMask = 1;
            var cameraData = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            // The character renderer has no background pixel pass, so the live 3D models stay sharp.
            var pipeline = new SerializedObject(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
            var renderers = pipeline.FindProperty("m_RendererDataList");
            for (int i = 0; i < renderers.arraySize; i++)
                if (AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(i).objectReferenceValue)
                    == "Assets/Settings/Character_Renderer.asset") cameraData.SetRenderer(i);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || _controller == null) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup >= _nextStatus)
            {
                _nextStatus = EditorApplication.timeSinceStartup + 2;
                WriteStatus("Recording");
            }
            // Wait for the actual terminal state on every character, rather than guessing duration.
            bool finished = true;
            foreach (var animator in _animators)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (!state.IsName("Base Layer.32_StunnedLoop") || state.normalizedTime < 1f)
                    finished = false;
            }
            if (!finished) return;
            if (_finishTime < 0f) _finishTime = Time.time + 0.3f;
            if (Time.time < _finishTime) return;
            if (_stage < RESOLUTIONS.Length - 1)
            {
                SetStage(_stage + 1);
                return;
            }
            _controller.StopRecording();
            _controller = null;
            EditorApplication.update -= Tick;
            SessionState.SetBool(ARMED_KEY, false);
            WriteStatus("Completed");
            EditorApplication.isPlaying = false;
        }

        private static void WriteStatus(string state)
        {
            Status = state + "; resolution=" + RESOLUTIONS[_stage] + "; frame=" + Time.frameCount
                + "; elapsed=" + (Time.time - _recordStart).ToString("F2") + "; file=" + _outputName;
            if (!string.IsNullOrEmpty(_outputDirectory))
                File.WriteAllText(Path.Combine(_outputDirectory, "status.txt"), Status);
        }
    }
}
