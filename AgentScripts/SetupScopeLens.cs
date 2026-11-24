using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupScopeLens
{
    public static void Execute()
    {
        const string ROOT = "Assets/sniper/ScopeLens";
        var transforms = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(t.gameObject.scene)).ToArray();
        var eyes = transforms.Where(t => string.Equals(t.name, "scopeEyePoint", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (eyes.Length != 1) throw new InvalidOperationException("Expected exactly one scopeEyePoint in the open scene.");
        Transform eye = eyes[0];
        Camera camera = eye.parent.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.name == "scopeViewPort");
        if (camera == null) throw new InvalidOperationException("scopeViewPort camera was not found beside scopeEyePoint.");

        Directory.CreateDirectory(ROOT);
        AssetDatabase.Refresh();
        string normalPath = ROOT + "/CircularLensNormal.png";
        // Mathematical hemisphere normal data, stored as a real reusable normal-map asset.
        const int SIZE = 512;
        var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            float nx = ((x + 0.5f) / SIZE * 2f - 1f) * 0.85f;
            float ny = ((y + 0.5f) / SIZE * 2f - 1f) * 0.85f;
            Vector3 normal = new Vector3(nx, ny, Mathf.Sqrt(Mathf.Max(0.001f, 1f - nx * nx - ny * ny))).normalized;
            pixels[y * SIZE + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(normalPath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        string renderPath = ROOT + "/ScopeCapture.renderTexture";
        var renderTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(renderPath);
        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
            {
                name = "ScopeCapture", antiAliasing = 1, useMipMap = false,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            AssetDatabase.CreateAsset(renderTexture, renderPath);
        }
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ROOT + "/ScopeLensGraph.shadergraph");
        if (shader == null) throw new InvalidOperationException("ScopeLensGraph failed to import. The preserved ScopeLens.shader is the reference implementation.");
        string materialPath = ROOT + "/ScopeLens.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "ScopeLens" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.SetTexture("_MainTex", renderTexture);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.SetFloat("_Distortion", 0.09f);
        material.SetFloat("_Zoom", 1.15f);
        material.SetFloat("_EdgeSoftness", 0.025f);
        material.SetFloat("_Vignette", 0.3f);
        EditorUtility.SetDirty(material);

        Transform existing = eye.Find("Scope Lens");
        GameObject lens = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Quad);
        Undo.RegisterCompleteObjectUndo(camera, "Configure Scope Capture");
        if (existing == null) Undo.RegisterCreatedObjectUndo(lens, "Create Scope Lens");
        lens.name = "Scope Lens";
        lens.transform.SetParent(eye, false);
        lens.transform.localPosition = Vector3.zero;
        lens.transform.localRotation = Quaternion.identity;
        // Counter parent scale so the lens fits inside the existing scope rim.
        Vector3 scale = eye.lossyScale;
        lens.transform.localScale = new Vector3(0.064f / Mathf.Abs(scale.x), 0.064f / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z));
        var collider = lens.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = lens.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var controller = lens.GetComponent<CpcPrototype.ScopeLens>();
        if (controller == null) controller = Undo.AddComponent<CpcPrototype.ScopeLens>(lens);
        controller.Configure(camera, renderer);
        EditorUtility.SetDirty(controller);

        // The existing camera is at the front of the scope; look away from the eyepiece.
        Vector3 direction = (camera.transform.position - eye.position).normalized;
        camera.transform.rotation = Quaternion.LookRotation(direction, eye.up);
        camera.targetTexture = renderTexture;
        camera.aspect = 1f;
        camera.fieldOfView = 30f;
        camera.nearClipPlane = 0.03f;
        camera.depth = -10f;
        camera.enabled = true;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        var listener = camera.GetComponent<AudioListener>();
        if (listener != null) { Undo.RecordObject(listener, "Disable Scope Audio Listener"); listener.enabled = false; }
        EditorUtility.SetDirty(camera);
        EditorUtility.SetDirty(data);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        EditorSceneManager.MarkSceneDirty(eye.gameObject.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(eye.gameObject.scene);
        Selection.activeGameObject = lens;
        Debug.Log("SCOPE_SETUP_OK: " + eye.gameObject.scene.path + " | 1024x1024 capture, 6.4cm lens, circular hemisphere normal, FOV 30.");
    }
}
