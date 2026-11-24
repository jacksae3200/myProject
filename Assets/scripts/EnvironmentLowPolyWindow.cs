#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMeshSimplifier; // 패키지: UnityMeshSimplifier (Package Manager > Add package from git URL)

/// <summary>
/// 씬에서 지정한 레이어(기본: Environment)의 MeshFilter 메시를 로우폴리로 바꿔주는 에디터 툴.
/// - 원본 메시 에셋은 건드리지 않고, 새 메시 에셋을 만들어 sharedMesh만 교체합니다.
/// - 같은 메시를 쓰는 오브젝트는 한 번만 변환해서 공유합니다.
/// - Ctrl+Z로 되돌릴 수 있습니다.
/// 위치: Assets/Editor/EnvironmentLowPolyWindow.cs
/// 메뉴: Tools > Low Poly > Environment Layer Converter
/// </summary>
public class EnvironmentLowPolyWindow : EditorWindow
{
    int layer;
    float quality = 0.3f;          // 1 = 원본 유지, 0.3 = 폴리곤 30%만 남김
    int minVertices = 100;         // 이보다 정점이 적은 메시는 건너뜀
    bool preserveBorders = true;   // 열린 모서리 보존 (구멍/찢어짐 방지)
    bool preserveUVSeams = true;   // UV 이음새 보존 (텍스처 깨짐 방지)
    bool flatShading = true;       // 면 단위 노말 (로우폴리 느낌)
    bool includeInactive = true;
    string saveFolder = "Assets/LowPolyMeshes";

    [MenuItem("Tools/Low Poly/Environment Layer Converter")]
    static void Open()
    {
        GetWindow<EnvironmentLowPolyWindow>("Env LowPoly");
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
        quality = EditorGUILayout.Slider("Quality (남길 비율)", quality, 0.02f, 1f);
        minVertices = EditorGUILayout.IntField("Min Vertices (이하 건너뜀)", minVertices);
        preserveBorders = EditorGUILayout.Toggle("Preserve Border Edges", preserveBorders);
        preserveUVSeams = EditorGUILayout.Toggle("Preserve UV Seams", preserveUVSeams);
        flatShading = EditorGUILayout.Toggle("Flat Shading", flatShading);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);
        saveFolder = EditorGUILayout.TextField("Save Folder", saveFolder);

        EditorGUILayout.Space();
        if (GUILayout.Button("Convert Layer To Low Poly", GUILayout.Height(32)))
            Convert();
    }

    void Convert()
    {
#if UNITY_2022_2_OR_NEWER
        var filters = FindObjectsByType<MeshFilter>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        var filters = FindObjectsOfType<MeshFilter>(includeInactive);
#endif

        var targets = new List<MeshFilter>();
        foreach (var mf in filters)
        {
            if (mf.gameObject.layer != layer) continue;
            if (!mf.gameObject.scene.IsValid()) continue;
            if (mf.sharedMesh == null) continue;
            targets.Add(mf);
        }

        if (targets.Count == 0)
        {
            Debug.LogWarning($"[LowPoly] 레이어 '{LayerMask.LayerToName(layer)}'에 MeshFilter 오브젝트가 없습니다.");
            return;
        }

        EnsureFolder(saveFolder);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Environment Low Poly");
        int undoGroup = Undo.GetCurrentGroup();

        var cache = new Dictionary<Mesh, Mesh>();
        int converted = 0, skipped = 0;
        long trisBefore = 0, trisAfter = 0;

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var mf = targets[i];
                var src = mf.sharedMesh;

                EditorUtility.DisplayProgressBar("Low Poly 변환 중", src.name, (float)i / targets.Count);

                // 이미 변환된 메시(저장 폴더 안)는 건너뜀
                string srcPath = AssetDatabase.GetAssetPath(src);
                if (!string.IsNullOrEmpty(srcPath) && srcPath.StartsWith(saveFolder))
                {
                    skipped++;
                    continue;
                }

                if (src.vertexCount < minVertices)
                {
                    skipped++;
                    continue;
                }

                if (!cache.TryGetValue(src, out var lowPoly))
                {
                    lowPoly = Simplify(src);
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{saveFolder}/{lowPoly.name}.asset");
                    AssetDatabase.CreateAsset(lowPoly, path);
                    cache[src] = lowPoly;
                }

                trisBefore += TriCount(src);
                trisAfter += TriCount(lowPoly);

                Undo.RecordObject(mf, "Environment Low Poly");
                mf.sharedMesh = lowPoly;
                EditorUtility.SetDirty(mf);
                converted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"[LowPoly] 변환 {converted}개 / 건너뜀 {skipped}개 / 새 메시 {cache.Count}개 " +
                  $"/ 삼각형 {trisBefore:N0} → {trisAfter:N0}");
    }

    Mesh Simplify(Mesh src)
    {
        var simplifier = new MeshSimplifier();
        simplifier.Initialize(src);

        var options = SimplificationOptions.Default;
        options.PreserveBorderEdges = preserveBorders;
        options.PreserveUVSeamEdges = preserveUVSeams;
        simplifier.SimplificationOptions = options;

        simplifier.SimplifyMesh(quality);

        Mesh result = simplifier.ToMesh();
        result.name = src.name + "_LowPoly";

        if (flatShading)
        {
            Mesh flat = MakeFlat(result);
            DestroyImmediate(result);
            result = flat;
        }

        result.RecalculateBounds();
        return result;
    }

    // 삼각형마다 정점을 분리해서 면 노말이 나오게 만듭니다.
    static Mesh MakeFlat(Mesh src)
    {
        var verts = src.vertices;
        var uv0 = src.uv;
        var uv1 = src.uv2;
        var colors = src.colors;

        bool hasUV0 = uv0.Length == verts.Length;
        bool hasUV1 = uv1.Length == verts.Length; // 라이트맵 UV
        bool hasCol = colors.Length == verts.Length;

        var nv = new List<Vector3>();
        var nuv0 = new List<Vector2>();
        var nuv1 = new List<Vector2>();
        var ncol = new List<Color>();
        var subTris = new List<int[]>();

        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            var tris = src.GetTriangles(sm);
            var newTris = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                int idx = tris[i];
                nv.Add(verts[idx]);
                if (hasUV0) nuv0.Add(uv0[idx]);
                if (hasUV1) nuv1.Add(uv1[idx]);
                if (hasCol) ncol.Add(colors[idx]);
                newTris[i] = nv.Count - 1;
            }
            subTris.Add(newTris);
        }

        var flat = new Mesh { name = src.name, indexFormat = IndexFormat.UInt32 };
        flat.SetVertices(nv);
        if (hasUV0) flat.SetUVs(0, nuv0);
        if (hasUV1) flat.SetUVs(1, nuv1);
        if (hasCol) flat.SetColors(ncol);

        flat.subMeshCount = subTris.Count;
        for (int sm = 0; sm < subTris.Count; sm++)
            flat.SetTriangles(subTris[sm], sm);

        flat.RecalculateNormals();
        if (hasUV0) flat.RecalculateTangents();
        return flat;
    }

    static int TriCount(Mesh m)
    {
        int count = 0;
        for (int i = 0; i < m.subMeshCount; i++)
            count += (int)m.GetIndexCount(i) / 3;
        return count;
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
