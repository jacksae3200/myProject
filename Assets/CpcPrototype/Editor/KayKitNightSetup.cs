using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitNightSetup
    {
        private const string FOLDER = "Assets/CpcPrototype/Survival/Generated/KayKitDepth";
        private const string MOVEMENT = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Movement";

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/KayKit_DepthSurvival.unity" || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open the KayKit scene in Edit mode.");
            var player = Object.FindFirstObjectByType<SurvivalPlayer>();
            Transform city = GameObject.Find("KayKit City").transform;
            Transform street = city.Find("Pixel Street Props");
            if (player.AimGuide != null) throw new InvalidOperationException("Night setup already applied.");

            var controllers = new Dictionary<RuntimeAnimatorController, AnimatorController>();
            SetMovement(player.Body, controllers);
            SetMovement(player.Companion.Body, controllers);
            foreach (SurvivalZombie zombie in player.Companion.Monsters) SetMovement(zombie.Body, controllers);

            int props = BakeProps(city, street);
            int lamps = SetNight(player.View, street);
            player.AimGuide = player.gameObject.AddComponent<KayKitAimGuide>();
            player.AimGuide.Player = player;
            player.DirectionalMovement = player.Companion.DirectionalMovement = true;
            foreach (SurvivalZombie zombie in player.Companion.Monsters) zombie.DirectionalMovement = true;

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("KayKit scene save failed.");
            return $"Saved {props} pixel props, {lamps} lamp lights and {controllers.Count} directional controllers.";
        }

        private static void SetMovement(Animator animator, Dictionary<RuntimeAnimatorController, AnimatorController> controllers)
        {
            RuntimeAnimatorController source = animator.runtimeAnimatorController;
            if (!controllers.TryGetValue(source, out AnimatorController controller))
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/" + source.name + " Directional.controller");
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                    throw new IOException("Could not copy " + source.name);
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
                controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
                var state = controller.layers[0].stateMachine.states.Single(child => child.state.name == "Locomotion").state;
                var blend = (BlendTree)state.motion;
                Motion idle = blend.children[0].motion;
                blend.children = Array.Empty<ChildMotion>();
                blend.blendType = BlendTreeType.FreeformDirectional2D;
                blend.blendParameter = "MoveX";
                blend.blendParameterY = "MoveZ";
                blend.AddChild(idle, Vector2.zero);
                AddGait(blend, "Walk", 1f);
                AddGait(blend, "Run", 2f);
                EditorUtility.SetDirty(blend);
                EditorUtility.SetDirty(controller);
                controllers.Add(source, controller);
            }
            animator.runtimeAnimatorController = controller;
        }

        private static void AddGait(BlendTree blend, string gait, float radius)
        {
            string[] names = { "Forward", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };
            for (int i = 0; i < names.Length; i++)
            {
                string path = $"{MOVEMENT}/{gait}/HumanM@{gait}01_{names[i]}.fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null || !clip.humanMotion) throw new InvalidOperationException("Missing humanoid motion: " + path);
                float angle = i * Mathf.PI / 4f;
                blend.AddChild(clip, new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius);
            }
        }

        private static int BakeProps(Transform city, Transform street)
        {
            Transform[] props = city.Cast<Transform>().Where(t => t.gameObject.activeSelf && (t.name == "dumpster" || t.name == "bush")).ToArray();
            Camera camera = KayKitPixelStreetSetup.CreateCaptureCamera();
            try
            {
                foreach (Transform prop in props)
                {
                    Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
                    Bounds bounds = KayKitPixelStreetSetup.Measure(renderers);
                    float frame = Mathf.Max(bounds.size.x, bounds.size.y) * 1.1f;
                    Material sprite = KayKitPixelStreetSetup.CaptureProp(camera, renderers, bounds.center, frame, prop.name);
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = prop.name == "bush" ? "Pixel Bush" : "Pixel Dumpster";
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.transform.SetParent(street, false);
                    quad.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
                    quad.transform.localScale = Vector3.one * frame;
                    var renderer = quad.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = sprite;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    prop.gameObject.SetActive(false);
                }
            }
            finally { Object.DestroyImmediate(camera.gameObject); }
            return props.Length;
        }

        private static int SetNight(Camera view, Transform street)
        {
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.035f, .055f, .11f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.24f, .31f, .48f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = view.backgroundColor;
            RenderSettings.fogDensity = .035f;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                light.color = new Color(.58f, .72f, 1f);
                light.intensity = .38f;
            }
            int count = 0;
            foreach (MeshRenderer sprite in street.GetComponentsInChildren<MeshRenderer>())
            {
                Material material = sprite.sharedMaterial;
                bool isLamp = sprite.transform.parent.name == "Pixel Streetlight";
                material.SetColor("_BaseColor", isLamp ? new Color(.68f, .73f, .82f) : new Color(.52f, .59f, .75f));
                EditorUtility.SetDirty(material);
            }
            foreach (BoxCollider platform in street.GetComponentsInChildren<BoxCollider>())
            {
                if (platform.name != "Lamp Bulb Platform") continue;
                var bulb = new GameObject("Warm Bulb Light");
                bulb.transform.SetParent(platform.transform, false);
                bulb.transform.position = platform.bounds.center - Vector3.up * .04f;
                var light = bulb.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, .68f, .34f);
                light.range = 2.4f;
                light.intensity = 2.6f;
                count++;
            }
            return count;
        }
    }
}
