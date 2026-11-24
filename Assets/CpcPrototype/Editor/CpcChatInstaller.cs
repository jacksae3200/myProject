using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CpcPrototype.Editor
{
    public static class CpcChatInstaller
    {
        public static void Install()
        {
            const string path = "Assets/CpcPrototype/Settings/ChatPanelSettings.asset";
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.sortingOrder = 100;
                settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/CpcPrototype/UI/ChatRuntimeTheme.tss");
                AssetDatabase.CreateAsset(settings, path);
            }
            PrototypeHud hud = Object.FindFirstObjectByType<PrototypeHud>();
            if (hud == null) throw new System.InvalidOperationException("PicoCpcTest 씬을 먼저 여세요.");
            CpcChatPanel panel = hud.GetComponent<CpcChatPanel>();
            if (panel == null) panel = Undo.AddComponent<CpcChatPanel>(hud.gameObject);
            panel.ConfigurePanel(settings);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            AssetDatabase.SaveAssets();
            // 사용자가 다른 씬을 열었을 때에는 해당 씬 파일을 자동 저장하지 않는다.
            if (hud.gameObject.scene.path == PrototypeSceneBuilder.SCENE_PATH) EditorSceneManager.SaveScene(hud.gameObject.scene);
        }
    }
}
