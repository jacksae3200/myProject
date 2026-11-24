using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class CpcVoiceSceneSetup
    {
        [MenuItem("Tools/CPC/Voice Mode/Batch Inference")]
        public static void UseBatch() { SetMode(VoiceRecognitionMode.BatchInference); }
        [MenuItem("Tools/CPC/Voice Mode/Sliding Window")]
        public static void UseSliding() { SetMode(VoiceRecognitionMode.SlidingWindow); }
        private static void SetMode(VoiceRecognitionMode mode)
        {
            CpcVoiceInput voice = UnityEngine.Object.FindFirstObjectByType<CpcVoiceInput>();
            if (voice == null) throw new InvalidOperationException("Open the survival scene with voice input.");
            Undo.RecordObject(voice, "Switch voice recognition mode");
            voice.SetRecognitionMode(mode);
            EditorUtility.SetDirty(voice);
            if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        [MenuItem("Tools/CPC/Install Push To Talk")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/KayKit_DepthSurvival.unity") throw new InvalidOperationException("Open KayKit_DepthSurvival first.");
            SurvivalPlayer player = UnityEngine.Object.FindFirstObjectByType<SurvivalPlayer>();
            if (player == null || player.Chat == null || player.Companion == null) throw new InvalidOperationException("Player/chat/CPC references are required.");
            ShieldCompanion companion = player.Companion;
            if (companion.Tactics == null)
            {
                Undo.RecordObject(companion, "Connect CPC tactics");
                if (!companion.TryGetComponent<CpcTactics>(out var tactics)) tactics = Undo.AddComponent<CpcTactics>(companion.gameObject);
                companion.Tactics = tactics;
                EditorUtility.SetDirty(companion);
            }
            Undo.RecordObject(companion.Tactics, "Connect CPC tactics");
            companion.Tactics.Companion = companion;
            EditorUtility.SetDirty(companion.Tactics);
            if (!player.TryGetComponent<CpcVoiceInput>(out var voice)) voice = Undo.AddComponent<CpcVoiceInput>(player.gameObject);
            if (!player.TryGetComponent<SlidingWindowVoiceInput>(out _)) Undo.AddComponent<SlidingWindowVoiceInput>(player.gameObject);
            Undo.RecordObject(voice, "Connect voice input");
            voice.Player = player;
            voice.Chat = player.Chat;
            EditorUtility.SetDirty(voice);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
