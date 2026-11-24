using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnimationPreview.Editor
{
    /// <summary>원본 메시와 셰이더를 유지하고 복제한 텍스처의 임포트 설정만 변경합니다.</summary>
    public sealed class VikingTexturePixelizer : EditorWindow
    {
        private const string SOURCE_SCENE = "Assets/Viking Village/Scenes/The_Viking_Village.unity";
        private const string OUTPUT_ROOT = "Assets/VikingTexturePreview";
        private int _resolution = 64;

        [MenuItem("Tools/Pixelize/Viking Texture Only")]
        private static void Open() => GetWindow<VikingTexturePixelizer>("Viking Texture Only");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("원본 씬에서 비교 씬을 생성합니다. 메시/UV/셰이더는 유지하고 텍스처 복사본만 저해상도 + Point 필터로 변경합니다.", MessageType.Info);
            _resolution = EditorGUILayout.IntPopup("Maximum Texture Size", _resolution,
                new[] { "32", "64", "128", "256" }, new[] { 32, 64, 128, 256 });
            if (GUILayout.Button("Create Texture Only Preview")) CreatePreview(_resolution);
        }

        public static string CreatePreview(int resolution = 64)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            if (!new[] { 32, 64, 128, 256 }.Contains(resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled";
            Directory.CreateDirectory(OUTPUT_ROOT);
            AssetDatabase.Refresh();
            string folder = AssetDatabase.GenerateUniqueAssetPath(OUTPUT_ROOT + "/Preview" + resolution);
            AssetDatabase.CreateFolder(OUTPUT_ROOT, Path.GetFileName(folder));
            AssetDatabase.CreateFolder(folder, "Textures");
            AssetDatabase.CreateFolder(folder, "Materials");
            Scene scene = EditorSceneManager.OpenScene(SOURCE_SCENE);
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            var filters = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            var skins = renderers.OfType<SkinnedMeshRenderer>().ToArray();
            var meshes = filters.Select(filter => filter.sharedMesh).Concat(skins.Select(skin => skin.sharedMesh)).ToArray();
            long vertices = meshes.Where(mesh => mesh != null).Sum(mesh => (long)mesh.vertexCount);
            var materials = new Dictionary<Material, Material>();
            var textures = new Dictionary<Texture, Texture>();
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int index = 0; index < slots.Length; index++)
                    {
                        Material source = slots[index];
                        if (source == null) continue;
                        if (!materials.TryGetValue(source, out Material copy))
                        {
                            // 셰이더, 색상, 투명도, 타일링, 노멀 강도 등을 그대로 복사합니다.
                            copy = new Material(source);
                            foreach (string property in source.GetTexturePropertyNames())
                            {
                                Texture texture = source.GetTexture(property);
                                string path = AssetDatabase.GetAssetPath(texture);
                                if (!(texture is Texture2D) || !path.StartsWith("Assets/Viking Village/", StringComparison.Ordinal)) continue;
                                if (!textures.TryGetValue(texture, out Texture pixel))
                                {
                                    if (!(AssetImporter.GetAtPath(path) is TextureImporter)) continue;
                                    string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/Textures/" + Path.GetFileName(path));
                                    if (!AssetDatabase.CopyAsset(path, destination)) throw new IOException(path);
                                    var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
                                    importer.maxTextureSize = resolution;
                                    importer.filterMode = FilterMode.Point;
                                    importer.mipmapEnabled = false;
                                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                                    // 플랫폼별 원본 해상도/압축 override가 복사본 설정을 덮어쓰지 않게 합니다.
                                    foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                                        importer.ClearPlatformTextureSettings(platform);
                                    importer.SaveAndReimport();
                                    pixel = AssetDatabase.LoadAssetAtPath<Texture>(destination);
                                    textures.Add(texture, pixel);
                                }
                                copy.SetTexture(property, pixel);
                            }
                            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/Materials/" + source.name.Replace('/', '_') + ".mat"));
                            materials.Add(source, copy);
                        }
                        slots[index] = copy;
                    }
                    renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                var after = filters.Select(filter => filter.sharedMesh).Concat(skins.Select(skin => skin.sharedMesh)).ToArray();
                if (!meshes.SequenceEqual(after)) throw new InvalidOperationException("Mesh references changed.");
                string output = folder + "/Viking_TextureOnly.unity";
                ConfigureTextureOnlyRenderer(folder);
                EditorSceneManager.SaveScene(scene, output);
                AssetDatabase.SaveAssets();
                string report = $"Source: {SOURCE_SCENE}\nPreview: {output}\nMax texture size: {resolution}\nRenderers: {renderers.Length}\nMaterials: {materials.Count}\nTextures: {textures.Count}\nMesh references unchanged: True\nVertex sum before/after (all mesh instances, including inactive LODs): {vertices}/{after.Where(mesh => mesh != null).Sum(mesh => (long)mesh.vertexCount)}\n";
                File.WriteAllText(folder + "/Verification.txt", report);
                AssetDatabase.Refresh();
                Debug.Log(report);
                return report;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static void ConfigureTextureOnlyRenderer(string folder)
        {
            var pipeline = new SerializedObject(GraphicsSettings.currentRenderPipeline);
            var list = pipeline.FindProperty("m_RendererDataList");
            var camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.enabled);
            var data = camera.GetUniversalAdditionalCameraData();
            var serializedCamera = new SerializedObject(data);
            int sourceIndex = serializedCamera.FindProperty("m_RendererIndex").intValue;
            if (sourceIndex < 0) sourceIndex = pipeline.FindProperty("m_DefaultRendererIndex").intValue;
            string source = AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(sourceIndex).objectReferenceValue);
            string destination = folder + "/TextureOnly_Renderer.asset";
            if (!AssetDatabase.CopyAsset(source, destination)) throw new IOException(source);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(destination);
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature is FullScreenPassRendererFeature) feature.SetActive(false);
            }
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            int index = list.arraySize;
            list.arraySize++;
            list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
            pipeline.ApplyModifiedPropertiesWithoutUndo();
            data.SetRenderer(index);
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
    }
}
