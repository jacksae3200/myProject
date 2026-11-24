using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AnimationPreview.Editor
{
    /// <summary>Records configured environment scenes while leaving their saved arrangements intact.</summary>
    [InitializeOnLoad]
    public static class EnvironmentComparisonRecorder
    {
        private const string ACTIVE_KEY = "EnvironmentComparison.Active";
        private const string INDEX_KEY = "EnvironmentComparison.Index";
        private const string NEXT_KEY = "EnvironmentComparison.Next";
        private const string RUN_KEY = "EnvironmentComparison.Run";
        private const int QUAD_LAYER = 31;
        private const int CHARACTER_MASK = (1 << 6) | (1 << 7) | (1 << 8);
        private static readonly string[] SCENES =
        {
            "Assets/Scenes/SimpleNaturePack_Pixelized.unity",
            "Assets/Scenes/FantasyEnvironment_Pixelized.unity",
            "Assets/Scenes/VikingVillage_Pixelized.unity"
        };
        private static readonly Vector2Int[] RESOLUTIONS =
        {
            new Vector2Int(160, 90), new Vector2Int(320, 180),
            new Vector2Int(480, 270), new Vector2Int(640, 360),
            new Vector2Int(960, 540), new Vector2Int(1920, 1080)
        };
        private static readonly List<string> CHAPTERS = new List<string>();
        private static readonly List<RenderTexture> TEXTURES = new List<RenderTexture>();
        private static RecorderController _controller;
        private static Animator[] _animators;
        private static FullScreenPassRendererFeature _feature;
        private static Material _savedMaterial;
        private static Material _runtimeMaterial;
        private static string _directory;
        private static string _name;
        private static int _stage;
        private static int _lastFrame = -1;
        private static float _start;
        private static float _stageStart;
        private static float _finish = -1f;
        private static bool _preview;
        private static double _nextStatus;
        public static string Status { get; private set; } = "Idle";

        static EnvironmentComparisonRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void ArmBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before recording.");
            PrepareRendererAssets();
            SessionState.SetString(RUN_KEY, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            SessionState.SetInt(INDEX_KEY, 0);
            SessionState.SetBool(ACTIVE_KEY, true);
            OpenNextScene();
        }

        private static string RendererPath(int index)
        {
            return "Assets/Settings/Recording/" + Path.GetFileNameWithoutExtension(SCENES[index]) + "_RecordingRenderer.asset";
        }

        private static void PrepareRendererAssets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings/Recording"))
                AssetDatabase.CreateFolder("Assets/Settings", "Recording");
            var pipeline = new SerializedObject(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
            var list = pipeline.FindProperty("m_RendererDataList");
            int fallback = pipeline.FindProperty("m_DefaultRendererIndex").intValue;
            for (int sceneIndex = 0; sceneIndex < SCENES.Length; sceneIndex++)
            {
                EditorSceneManager.OpenScene(SCENES[sceneIndex]);
                var camera = UnityEngine.Object.FindObjectsByType<Camera>().First(c => c.enabled && c.targetTexture == null);
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                int sourceIndex = data == null ? fallback : new SerializedObject(data).FindProperty("m_RendererIndex").intValue;
                if (sourceIndex < 0 || sourceIndex >= list.arraySize) sourceIndex = fallback;
                string path = RendererPath(sceneIndex);
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (renderer == null)
                {
                    if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(sourceIndex).objectReferenceValue), path))
                        throw new InvalidOperationException("Could not copy the renderer.");
                    renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                    foreach (var feature in renderer.rendererFeatures)
                    {
                        if (feature is FullScreenPassRendererFeature ||
                            (feature is RenderObjects objects && (objects.settings.filterSettings.LayerMask.value & CHARACTER_MASK) != 0))
                        {
                            feature.SetActive(false);
                            EditorUtility.SetDirty(feature);
                        }
                    }
                    // All 3D characters are drawn with the environment; only Quad layer 31 is postponed.
                    renderer.opaqueLayerMask = (renderer.opaqueLayerMask.value | CHARACTER_MASK) & ~(1 << QUAD_LAYER);
                    renderer.transparentLayerMask = (renderer.transparentLayerMask.value | CHARACTER_MASK) & ~(1 << QUAD_LAYER);
                    var pixel = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    pixel.name = "Recording: Background and 3D Pixelize";
                    pixel.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
                    pixel.fetchColorBuffer = true;
                    pixel.passMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/shaderGraph/BackgroundPixelizeMaterial.mat");
                    AssetDatabase.AddObjectToAsset(pixel, renderer);
                    renderer.rendererFeatures.Add(pixel);
                    var quads = ScriptableObject.CreateInstance<RenderObjects>();
                    quads.name = "Recording: Fixed 128 Quad Characters";
                    quads.settings.Event = (UnityEngine.Rendering.Universal.RenderPassEvent)551;
                    quads.settings.filterSettings.RenderQueueType = RenderQueueType.Transparent;
                    quads.settings.filterSettings.LayerMask = 1 << QUAD_LAYER;
                    quads.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
                    AssetDatabase.AddObjectToAsset(quads, renderer);
                    renderer.rendererFeatures.Add(quads);
                    EditorUtility.SetDirty(renderer);
                    AssetDatabase.SaveAssetIfDirty(renderer);
                }
                bool registered = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) registered = true;
                if (!registered)
                {
                    int index = list.arraySize;
                    list.InsertArrayElementAtIndex(index);
                    list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                    pipeline.ApplyModifiedProperties();
                    pipeline.Update();
                    list = pipeline.FindProperty("m_RendererDataList");
                }
            }
            AssetDatabase.SaveAssetIfDirty(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
            AssetDatabase.SaveAssets();
        }

        private static void OpenNextScene()
        {
            int index = SessionState.GetInt(INDEX_KEY, 0);
            if (index >= SCENES.Length)
            {
                SessionState.SetBool(ACTIVE_KEY, false);
                File.WriteAllText(Path.GetFullPath("Recordings/EnvironmentPixelComparison/batch_status.txt"), "Completed: all three videos");
                return;
            }
            EditorSceneManager.OpenScene(SCENES[index]);
            SessionState.SetBool(NEXT_KEY, false);
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ACTIVE_KEY, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Begin;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Tick;
                _controller?.StopRecording();
                _controller = null;
                if (_feature != null) _feature.passMaterial = _savedMaterial;
                if (!SessionState.GetBool(NEXT_KEY, false)) SessionState.SetBool(ACTIVE_KEY, false);
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(NEXT_KEY, false))
            {
                SessionState.SetInt(INDEX_KEY, SessionState.GetInt(INDEX_KEY, 0) + 1);
                EditorApplication.delayCall += OpenNextScene;
            }
        }

        private static void Begin()
        {
            try
            {
                Application.runInBackground = true;
                _directory = Path.GetFullPath("Recordings/EnvironmentPixelComparison");
                Directory.CreateDirectory(_directory);
                int index = SessionState.GetInt(INDEX_KEY, 0);
                _name = (index + 1).ToString("D2") + "_" + Path.GetFileNameWithoutExtension(SCENES[index])
                    + "_160x90_to_1920x1080_" + SessionState.GetString(RUN_KEY, "");
                _animators = UnityEngine.Object.FindObjectsByType<Animator>().Where(a => a.runtimeAnimatorController != null
                    && a.runtimeAnimatorController.name == "Blink32Sequence").ToArray();
                if (_animators.Length != 3) throw new InvalidOperationException("Expected three sequence animators.");
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath(index));
                _feature = renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().First(f => f.isActive);
                _savedMaterial = _feature.passMaterial;
                _runtimeMaterial = new Material(_savedMaterial);
                _feature.passMaterial = _runtimeMaterial;
                var pipeline = new SerializedObject(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
                var list = pipeline.FindProperty("m_RendererDataList");
                int rendererIndex = -1;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) rendererIndex = i;
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>().Where(c => c.targetTexture == null && c.enabled))
                {
                    camera.GetComponent<UniversalAdditionalCameraData>().SetRenderer(rendererIndex);
                    camera.cullingMask |= CHARACTER_MASK | (1 << QUAD_LAYER);
                }
                var replacements = new Dictionary<Texture, RenderTexture>();
                TEXTURES.Clear();
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>().Where(c => c.name.StartsWith("CharacterCamera")))
                {
                    var source = camera.targetTexture;
                    var descriptor = source.descriptor;
                    descriptor.width = descriptor.height = 128;
                    var texture = new RenderTexture(descriptor) { filterMode = FilterMode.Point, name = "Recording128_" + source.name };
                    texture.Create();
                    replacements.Add(source, texture);
                    TEXTURES.Add(texture);
                    camera.targetTexture = texture;
                }
                foreach (var quad in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
                {
                    var source = quad.sharedMaterial;
                    if (source == null || source.mainTexture == null || !replacements.ContainsKey(source.mainTexture)) continue;
                    var material = new Material(source);
                    material.mainTexture = replacements[source.mainTexture];
                    quad.sharedMaterial = material;
                    quad.gameObject.layer = QUAD_LAYER;
                }
                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                settings.FrameRate = 30;
                settings.CapFrameRate = false;
                settings.FrameRatePlayback = FrameRatePlayback.Constant;
                settings.SetRecordModeToManual();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "Environment Pixel Comparison";
                movie.Enabled = true;
                movie.CaptureAudio = false;
                movie.CaptureAlpha = false;
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
                movie.EncoderSettings = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
                movie.OutputFile = Path.Combine(_directory, _name);
                settings.AddRecorderSettings(movie);
                _controller = new RecorderController(settings);
                _controller.PrepareRecording();
                _start = Time.time;
                _lastFrame = -1;
                _nextStatus = 0;
                CHAPTERS.Clear();
                SetStage(0);
                if (!_controller.StartRecording()) throw new InvalidOperationException("Recorder failed to start.");
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
                if (_feature != null) _feature.passMaterial = _savedMaterial;
                SessionState.SetBool(ACTIVE_KEY, false);
            }
        }

        private static void SetStage(int stage)
        {
            _stage = stage;
            _stageStart = Time.time;
            _finish = -1f;
            _preview = false;
            _runtimeMaterial.SetVector("_PixelResolution", new Vector4(RESOLUTIONS[stage].x, RESOLUTIONS[stage].y, 0, 0));
            foreach (var animator in _animators)
            {
                animator.Rebind();
                animator.Play("Base Layer.01_BlockingLoop", 0, 0f);
                animator.Update(0f);
            }
            CHAPTERS.Add((Time.time - _start).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " seconds: background and 3D " + RESOLUTIONS[stage].x + "x" + RESOLUTIONS[stage].y + "; Quad 128x128");
            File.WriteAllLines(Path.Combine(_directory, _name + "_chapters.txt"), CHAPTERS);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || _controller == null) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (!_preview && Time.time - _stageStart > 2f)
            {
                _preview = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(_directory, _name + "_preview_" + RESOLUTIONS[_stage].x + ".png"));
            }
            if (EditorApplication.timeSinceStartup >= _nextStatus)
            {
                _nextStatus = EditorApplication.timeSinceStartup + 2;
                WriteStatus("Recording");
            }
            foreach (var animator in _animators)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (!state.IsName("Base Layer.32_StunnedLoop") || state.normalizedTime < 1f) return;
            }
            if (_finish < 0f) _finish = Time.time + 0.3f;
            if (Time.time < _finish) return;
            if (_stage < RESOLUTIONS.Length - 1) { SetStage(_stage + 1); return; }
            _controller.StopRecording();
            _controller = null;
            EditorApplication.update -= Tick;
            _feature.passMaterial = _savedMaterial;
            WriteStatus("Completed video");
            SessionState.SetBool(NEXT_KEY, true);
            EditorApplication.isPlaying = false;
        }

        private static void WriteStatus(string state)
        {
            Status = state + "; scene=" + (SessionState.GetInt(INDEX_KEY, 0) + 1) + "/3; resolution="
                + RESOLUTIONS[_stage] + "; frame=" + Time.frameCount + "; elapsed=" + (Time.time - _start).ToString("F2")
                + "; file=" + _name;
            if (!string.IsNullOrEmpty(_directory)) File.WriteAllText(Path.Combine(_directory, "batch_status.txt"), Status);
        }
    }
}
