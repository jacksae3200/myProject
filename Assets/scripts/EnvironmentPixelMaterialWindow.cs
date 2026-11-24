#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 씬에서 지정한 레이어(기본: Environment)의 MeshRenderer 머티리얼을
/// EnvPixel 셰이더(Shader Graph) 머티리얼로 일괄 교체합니다.
/// - 기존 머티리얼의 텍스처/색상을 복사해서 새 머티리얼을 만듭니다. (원본 머티리얼은 그대로)
/// - 같은 머티리얼은 한 번만 변환해서 공유합니다.
/// - Ctrl+Z로 되돌릴 수 있습니다.
/// 위치: Assets/Editor/EnvironmentPixelMaterialWindow.cs
/// 메뉴: Tools > Pixelize > Environment Pixel Material
/// </summary>
public class EnvironmentPixelMaterialWindow : EditorWindow
{
    const string ShaderName = "Shader Graphs/EnvPixel"; // Shader Graph 파일 이름이 EnvPixel일 때

    int layer;
    float texelRes = 64f;
    float posterizeSteps = 8f;
    float lightSteps = 3f;
    float ambient = 0.25f;
    bool includeInactive = true;
    string saveFolder = "Assets/PixelMaterials";

    [MenuItem("Tools/Pixelize/Environment Pixel Material")]
    static void Open()
    {
        GetWindow<EnvironmentPixelMaterialWindow>("Env Pixel");
    }

    void OnEnable()
    {
        int l = LayerMask.NameToLayer("Environment");
        layer = l >= 0 ? l : 0;
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        layer = EditorGUILayout.LayerField("Target Layer", layer);
        if (LayerMask.NameToLayer("Environment") < 0)
            EditorGUILayout.HelpBox("'Environment' 레이어가 프로젝트에 없습니다. 위에서 레이어를 직접 골라주세요.", MessageType.Warning);
        else if (layer == 0)
            EditorGUILayout.HelpBox("Default 레이어가 선택되어 있습니다. 씬의 거의 모든 오브젝트가 대상이 됩니다.", MessageType.Warning);

        EditorGUILayout.Space();
        texelRes = EditorGUILayout.Slider("Texel Res (작을수록 픽셀 큼)", texelRes, 8f, 256f);
        posterizeSteps = EditorGUILayout.Slider("Color Steps", posterizeSteps, 2f, 32f);
        lightSteps = EditorGUILayout.Slider("Light Steps", lightSteps, 2f, 8f);
        ambient = EditorGUILayout.Slider("Ambient", ambient, 0f, 1f);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);
        saveFolder = EditorGUILayout.TextField("Save Folder", saveFolder);

        EditorGUILayout.Space();
        if (GUILayout.Button("Apply Pixel Material To Layer", GUILayout.Height(32)))
            Apply();
    }

    void Apply()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[Pixelize] 셰이더 '{ShaderName}'를 찾을 수 없습니다. " +
                           "Shader Graph 파일 이름이 EnvPixel인지, 저장했는지 확인하세요.");
            return;
        }

#if UNITY_2022_2_OR_NEWER
        var renderers = FindObjectsByType<MeshRenderer>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        var renderers = FindObjectsOfType<MeshRenderer>(includeInactive);
#endif

        var targets = new List<MeshRenderer>();
        foreach (var r in renderers)
        {
            if (r.gameObject.layer != layer) continue;
            if (!r.gameObject.scene.IsValid()) continue;
            targets.Add(r);
        }

        if (targets.Count == 0)
        {
            Debug.LogWarning($"[Pixelize] 레이어 '{LayerMask.LayerToName(layer)}'에 MeshRenderer 오브젝트가 없습니다.");
            return;
        }

        EnsureFolder(saveFolder);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Environment Pixel Material");
        int undoGroup = Undo.GetCurrentGroup();

        var cache = new Dictionary<Material, Material>();
        int changedRenderers = 0;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var r = targets[i];
                EditorUtility.DisplayProgressBar("Pixel Material 적용 중", r.name, (float)i / targets.Count);

                var mats = r.sharedMaterials;
                bool changed = false;

                for (int m = 0; m < mats.Length; m++)
                {
                    var src = mats[m];
                    if (src == null) continue;
                    if (src.shader == shader) continue; // 이미 변환됨

                    if (!cache.TryGetValue(src, out var pixelMat))
                    {
                        pixelMat = CreatePixelMaterial(src, shader);
                        cache[src] = pixelMat;
                    }

                    mats[m] = pixelMat;
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(r, "Environment Pixel Material");
                    r.sharedMaterials = mats;
                    EditorUtility.SetDirty(r);
                    changedRenderers++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"[Pixelize] 렌더러 {changedRenderers}개 / 새 머티리얼 {cache.Count}개 적용 완료");
    }

    Material CreatePixelMaterial(Material src, Shader shader)
    {
        var mat = new Material(shader) { name = src.name + "_Pixel" };

        // 기존 머티리얼의 텍스처/색상 복사 (URP Lit: _BaseMap/_BaseColor, Built-in: _MainTex/_Color)
        Texture tex = null;
        if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
        if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");

        Color color = Color.white;
        if (src.HasProperty("_BaseColor")) color = src.GetColor("_BaseColor");
        else if (src.HasProperty("_Color")) color = src.GetColor("_Color");

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_TexelRes")) mat.SetFloat("_TexelRes", texelRes);
        if (mat.HasProperty("_PosterizeSteps")) mat.SetFloat("_PosterizeSteps", posterizeSteps);
        if (mat.HasProperty("_LightSteps")) mat.SetFloat("_LightSteps", lightSteps);
        if (mat.HasProperty("_Ambient")) mat.SetFloat("_Ambient", ambient);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{saveFolder}/{mat.name}.mat");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
