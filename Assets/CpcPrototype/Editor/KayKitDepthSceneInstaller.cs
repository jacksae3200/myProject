using System;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitDepthSceneInstaller
    {
        private const string SOURCE = "Assets/Scenes/ZRN_Survival.unity";
        private const string DESTINATION = "Assets/Scenes/KayKit_DepthSurvival.unity";
        private const string KIT = "Assets/KayKit_City_Builder_Bits_1.0_FREE/Assets/fbx (unity)/";
        private const string OUTPUT = "Assets/CpcPrototype/Survival/Generated/KayKitDepth";

        [MenuItem("Tools/CPC/Create KayKit Depth Survival")]
        private static void CreateFromMenu() => Create();

        public static string Create()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play mode and save the current scene.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DESTINATION) != null)
                throw new InvalidOperationException("The KayKit scene already exists.");
            if (!AssetDatabase.IsValidFolder(OUTPUT))
                AssetDatabase.CreateFolder("Assets/CpcPrototype/Survival/Generated", "KayKitDepth");
            if (!AssetDatabase.CopyAsset(SOURCE, DESTINATION)) throw new InvalidOperationException("Scene copy failed.");
            Scene scene = EditorSceneManager.OpenScene(DESTINATION);
            GameObject gameplay = GameObject.Find("Survival Gameplay");
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root != gameplay) Object.DestroyImmediate(root);

            var city = new GameObject("KayKit City");
            var floor = new GameObject("Walkable Street");
            floor.transform.SetParent(city.transform, false);
            var floorCollider = floor.AddComponent<BoxCollider>();
            floorCollider.center = new Vector3(0, -.1f, 0);
            floorCollider.size = new Vector3(24, .2f, 6);
            for (int i = 0; i < 12; i++)
            {
                float x = -11 + i * 2;
                Place(city.transform, i % 4 == 0 ? "road_straight_crossing" : "road_straight", x, 0, 90, true);
                Place(city.transform, "base", x, -2, 0, true);
                Place(city.transform, "base", x, -4, 0, true);
                Place(city.transform, "base", x, 2, 0, true);
                Place(city.transform, "building_" + (char)('A' + i % 8), x, 3.8f, 180);
                if (i % 2 == 0)
                {
                    Place(city.transform, "streetlight", x + .55f, 1.32f, 180);
                    Place(city.transform, "bush", x - .6f, 2.66f, 0);
                }
                if (i % 3 == 1) Place(city.transform, i % 2 == 0 ? "car_taxi" : "car_police", x, -.72f, 90, obstacle: true);
                if (i % 4 == 2) Place(city.transform, "dumpster", x, 2.26f, 180, obstacle: true);
            }
            Boundary(city.transform, new Vector3(-12.05f, .35f, 0), new Vector3(.1f, .7f, 6));
            Boundary(city.transform, new Vector3(12.05f, .35f, 0), new Vector3(.1f, .7f, 6));
            Boundary(city.transform, new Vector3(0, .35f, -3.05f), new Vector3(24, .7f, .1f));
            Boundary(city.transform, new Vector3(0, .35f, 3.05f), new Vector3(24, .7f, .1f));

            var sun = new GameObject("City Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.65f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -36, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.64f, .68f, .78f);
            RenderSettings.fog = false;

            var player = Object.FindFirstObjectByType<SurvivalPlayer>();
            player.DepthView = true;
            player.View.orthographic = true;
            player.View.orthographicSize = player.DepthViewSize;
            player.View.nearClipPlane = .02f;
            player.View.clearFlags = CameraClearFlags.SolidColor;
            player.View.backgroundColor = new Color(.43f, .59f, .71f);
            player.View.transform.rotation = Quaternion.LookRotation(-player.DepthCameraOffset);
            player.ViewRifle.SetActive(false);
            player.transform.SetPositionAndRotation(new Vector3(-7, .04f, -.25f), Quaternion.Euler(0, 90, 0));
            player.ShotRange = 10f;
            player.TracerWidth = .014f;
            player.TracerLength = .9f;
            player.Companion.transform.SetPositionAndRotation(new Vector3(-7.56f, .02f, -.35f), player.transform.rotation);
            player.Companion.DetectionRange = 2.65f;
            player.Companion.TauntRange = 1.5f;
            player.Companion.GuardDistance = .39f;

            var surface = gameplay.GetComponent<NavMeshSurface>();
            surface.RemoveData();
            surface.navMeshData = null;
            surface.collectObjects = CollectObjects.Children;
            Object.DestroyImmediate(surface);
            surface = city.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("City NavMesh was not built.");
            AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/CityNavigation.asset"));
            PlaceOnNavMesh(player.Companion.transform);
            Vector3[] enemySpawns = { new Vector3(-3.25f, 0, -.1f), new Vector3(1.7f, 0, .54f), new Vector3(6.55f, 0, -.15f) };
            for (int i = 0; i < player.Companion.Monsters.Length; i++)
            {
                SurvivalZombie zombie = player.Companion.Monsters[i];
                zombie.transform.position = enemySpawns[i];
                zombie.transform.rotation = Quaternion.Euler(0, -90, 0);
                zombie.DetectionRange = 2.35f;
                zombie.LoseRange = 3.8f;
                zombie.HomeLeash = 4.5f;
                PlaceOnNavMesh(zombie.transform);
            }

            var template = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Pixel Quad Cutout" };
            template.SetFloat("_AlphaClip", 1f);
            template.SetFloat("_Cutoff", .32f);
            template.SetFloat("_Cull", 0f);
            template.EnableKeyword("_ALPHATEST_ON");
            template.SetOverrideTag("RenderType", "TransparentCutout");
            template.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(template, AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/PixelQuad.mat"));
            AddPixelActor(player.transform, player.Body, player.View, template, 26);
            AddPixelActor(player.Companion.transform, player.Companion.Body, player.View, template, 27);
            for (int i = 0; i < player.Companion.Monsters.Length; i++)
            {
                SurvivalZombie zombie = player.Companion.Monsters[i];
                AddPixelActor(zombie.transform, zombie.Body, player.View, template, 28 + i);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("KayKit scene save failed.");
            Selection.activeObject = player.gameObject;
            return DESTINATION;
        }

        private static void PlaceOnNavMesh(Transform actor)
        {
            if (!NavMesh.SamplePosition(actor.position, out NavMeshHit hit, .7f, NavMesh.AllAreas))
                throw new InvalidOperationException("Unreachable spawn: " + actor.name);
            actor.position = hit.position;
        }

        private static void AddPixelActor(Transform root, Animator body, Camera view, Material template, int layer)
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                renderer.gameObject.layer = layer;
            view.cullingMask &= ~(1 << layer);
            var camera = new GameObject("Pixel Capture Camera").AddComponent<Camera>();
            camera.transform.SetParent(root, false);
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << layer;
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 2f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -10;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Pixel Character Quad";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root, false);
            MeshRenderer rendererQuad = quad.GetComponent<MeshRenderer>();
            rendererQuad.sharedMaterial = template;
            rendererQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var pixel = root.gameObject.AddComponent<ActorPixelQuad>();
            pixel.WorldView = view;
            pixel.Capture = camera;
            pixel.Quad = rendererQuad;
        }

        private static void Place(Transform parent, string name, float x, float z, float yaw, bool floorTile = false, bool obstacle = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(KIT + name + ".fbx");
            GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            item.transform.SetPositionAndRotation(new Vector3(x, 0, z), Quaternion.Euler(0, yaw, 0));
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float verticalOffset = floorTile ? -bounds.max.y : -bounds.min.y;
            item.transform.position += Vector3.up * verticalOffset;
            if (!obstacle) return;
            bounds.center += Vector3.up * verticalOffset;
            Boundary(parent, bounds.center, bounds.size * .96f);
        }

        private static void Boundary(Transform parent, Vector3 position, Vector3 size)
        {
            var marker = new GameObject("Street Collision");
            marker.transform.SetParent(parent, false);
            marker.transform.position = position;
            marker.AddComponent<BoxCollider>().size = size;
        }
    }
}
