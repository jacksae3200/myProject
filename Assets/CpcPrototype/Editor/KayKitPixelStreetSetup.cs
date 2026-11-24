using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitPixelStreetSetup
    {
        private const string SCENE = "Assets/Scenes/KayKit_DepthSurvival.unity";
        private const string FOLDER = "Assets/CpcPrototype/Survival/Generated/KayKitDepth/PixelStreet";
        private const int CAPTURE_LAYER = 25;
        private const int SPRITE_RESOLUTION = 64;

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open the KayKit scene in edit mode.");
            var city = GameObject.Find("KayKit City");
            if (city.transform.Find("Pixel Street Props") != null)
                throw new InvalidOperationException("Pixel props already exist.");
            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets/CpcPrototype/Survival/Generated/KayKitDepth", "PixelStreet");
            var player = Object.FindFirstObjectByType<SurvivalPlayer>();
            Transform[] props = city.transform.Cast<Transform>().Where(t => IsCar(t) || t.name == "streetlight").ToArray();
            var markers = city.GetComponentsInChildren<BoxCollider>().Where(c => c.name == "Street Collision").ToArray();
            var group = new GameObject("Pixel Street Props").transform;
            group.SetParent(city.transform, false);
            var heads = new List<BoxCollider>();
            Camera camera = CreateCaptureCamera();
            try
            {
                foreach (Transform prop in props)
                {
                    bool car = IsCar(prop);
                    Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
                    Bounds oldBounds = Measure(renderers);
                    if (car)
                    {
                        BoxCollider marker = markers.FirstOrDefault(c => c != null && (c.bounds.center - oldBounds.center).sqrMagnitude < .0004f);
                        if (marker != null) Object.DestroyImmediate(marker.gameObject);
                    }
                    else prop.localScale *= .64f;

                    prop.position = new Vector3(prop.position.x, prop.position.y, 0);
                    Bounds bounds = Measure(renderers);
                    prop.position -= Vector3.up * bounds.min.y;
                    bounds = Measure(renderers);
                    float frame = Mathf.Max(bounds.size.x, bounds.size.y) * 1.10f;
                    Material sprite = CaptureProp(camera, renderers, bounds.center, frame, prop.name);
                    var root = new GameObject(car ? "Pixel Car" : "Pixel Streetlight").transform;
                    root.SetParent(group, false);
                    root.position = new Vector3(prop.position.x, 0, 0);
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Pixel Sprite Quad";
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.transform.SetParent(root, false);
                    quad.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
                    quad.transform.localScale = Vector3.one * frame;
                    var quadRenderer = quad.GetComponent<MeshRenderer>();
                    quadRenderer.sharedMaterial = sprite;
                    quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    if (car)
                    {
                        BoxCollider body = root.gameObject.AddComponent<BoxCollider>();
                        body.center = root.InverseTransformPoint(bounds.center);
                        body.size = new Vector3(bounds.size.x * .91f, bounds.size.y, bounds.size.z);
                    }
                    else
                    {
                        var head = new GameObject("Lamp Bulb Platform");
                        head.transform.SetParent(root, false);
                        head.transform.position = new Vector3(bounds.min.x + bounds.size.x * .23f, bounds.max.y - .022f, 0);
                        var top = head.AddComponent<BoxCollider>();
                        top.size = new Vector3(bounds.size.x * .43f, .034f, .20f);
                        head.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
                        heads.Add(top);
                    }
                    prop.gameObject.SetActive(false);
                }
            }
            finally { Object.DestroyImmediate(camera.gameObject); }

            player.OneWayPlatforms = heads.ToArray();
            player.GetComponent<CharacterController>().skinWidth = .012f;
            player.JumpHeight = .73f;
            player.Gravity = 10f;
            player.transform.position = new Vector3(player.transform.position.x, player.transform.position.y, 0);
            player.Companion.transform.position = new Vector3(player.Companion.transform.position.x, player.Companion.transform.position.y, 0);
            foreach (SurvivalZombie zombie in player.Companion.Monsters)
                zombie.transform.position = new Vector3(zombie.transform.position.x, zombie.transform.position.y, 0);
            var surface = city.GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/StreetNavigation.asset"));
            Snap(player.Companion.transform);
            foreach (SurvivalZombie zombie in player.Companion.Monsters) Snap(zombie.transform);
            ApplyScreenPixels(player.View);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            return $"Saved {props.Length} sprite props, {heads.Count} one-way bulb platforms.";
        }

        internal static Camera CreateCaptureCamera()
        {
            var camera = new GameObject("Temporary Prop Capture").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << CAPTURE_LAYER;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 5f;
            return camera;
        }

        private static bool IsCar(Transform t) => t.name.StartsWith("car_", StringComparison.Ordinal);

        internal static Bounds Measure(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        internal static Material CaptureProp(Camera camera, Renderer[] renderers, Vector3 center, float frame, string name)
        {
            int[] layers = renderers.Select(r => r.gameObject.layer).ToArray();
            var target = RenderTexture.GetTemporary(SPRITE_RESOLUTION, SPRITE_RESOLUTION, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(SPRITE_RESOLUTION, SPRITE_RESOLUTION, TextureFormat.RGBA32, false);
            string path = AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/" + name + ".png");
            try
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = CAPTURE_LAYER;
                camera.transform.SetPositionAndRotation(center - Vector3.forward * 2, Quaternion.identity);
                camera.orthographicSize = frame * .5f;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, SPRITE_RESOLUTION, SPRITE_RESOLUTION), 0, 0);
                Color32[] pixels = image.GetPixels32();
                if (!pixels.Any(p => p.a > 128) || !pixels.Any(p => p.a < 32))
                    throw new InvalidOperationException("Transparent sprite capture failed: " + name);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Material template = AssetDatabase.LoadAssetAtPath<Material>("Assets/CpcPrototype/Survival/Generated/KayKitDepth/PixelQuad.mat");
            var material = new Material(template) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) };
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/" + name + ".mat"));
            return material;
        }

        private static void Snap(Transform actor)
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(actor.position, out UnityEngine.AI.NavMeshHit hit, .9f, UnityEngine.AI.NavMesh.AllAreas))
                throw new InvalidOperationException("No street navigation near " + actor.name);
            actor.position = hit.position;
        }

        private static void ApplyScreenPixels(Camera camera)
        {
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var settings = new SerializedObject(pipeline);
            SerializedProperty renderers = settings.FindProperty("m_RendererDataList");
            int defaultIndex = settings.FindProperty("m_DefaultRendererIndex").intValue;
            string source = AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(defaultIndex).objectReferenceValue);
            string rendererPath = FOLDER + "/KayKitPixelRenderer.asset";
            if (!AssetDatabase.CopyAsset(source, rendererPath)) throw new IOException("Renderer copy failed.");
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            var pass = renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().First();
            var material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/shaderGraph/PixelizeShader.mat"));
            material.SetFloat("_PixelHeight", 360);
            AssetDatabase.CreateAsset(material, FOLDER + "/CityPixelPass.mat");
            pass.passMaterial = material;
            pass.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            pass.requirements = ScriptableRenderPassInput.None;
            pass.fetchColorBuffer = true;
            pass.SetActive(true);
            EditorUtility.SetDirty(pass);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);

            settings.Update();
            int index = renderers.arraySize;
            renderers.InsertArrayElementAtIndex(index);
            renderers.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
            settings.ApplyModifiedProperties();
            camera.GetUniversalAdditionalCameraData().SetRenderer(index);
        }
    }
}

