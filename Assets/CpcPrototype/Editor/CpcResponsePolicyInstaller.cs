using UnityEditor;
using UnityEngine;

namespace CpcPrototype.Editor
{
    public static class CpcResponsePolicyInstaller
    {
        public const string POLICY_PATH = "Assets/CpcPrototype/Settings/CpcResponsePolicy.asset";

        [MenuItem("Tools/CPC/Install Response Policy")]
        public static void Install()
        {
            CpcLlmSettings settings = AssetDatabase.LoadAssetAtPath<CpcLlmSettings>(PrototypeSceneBuilder.SETTINGS_PATH);
            if (settings == null) throw new System.InvalidOperationException("GemmaLocalSettings 에셋이 없습니다.");
            CpcResponsePolicy policy = AssetDatabase.LoadAssetAtPath<CpcResponsePolicy>(POLICY_PATH);
            if (policy == null)
            {
                policy = ScriptableObject.CreateInstance<CpcResponsePolicy>();
                AssetDatabase.CreateAsset(policy, POLICY_PATH);
            }
            settings.ResponsePolicy = policy;
            if (string.IsNullOrEmpty(settings.SystemPrompt) || !settings.SystemPrompt.Contains("[대화 표현 및 주제 규칙]"))
                settings.SystemPrompt += CpcResponsePolicy.DEFAULT_SYSTEM_RULES;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            Selection.activeObject = policy;
        }
    }
}
