using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitEquipmentSetup
    {
        private const string SCENE = "Assets/Scenes/KayKit_DepthSurvival.unity";
        private const string FOLDER = "Assets/CpcPrototype/Survival/Generated/KayKitDepth/BackgroundPixel";
        private const int BACKGROUND_RESOLUTION = 128;

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open the KayKit scene in edit mode.");

            var player = Object.FindFirstObjectByType<SurvivalPlayer>();
            player.DepthViewSize = 1.16f;
            player.DepthCameraOffset = new Vector3(0, 0, -7f);
            player.View.orthographicSize = player.DepthViewSize;
            player.View.transform.position = new Vector3(player.transform.position.x, 1f, player.transform.position.z * .35f - 7f);

            if (player.Rifle.name != "Weapon_02 Grip")
            {
                Transform oldRifle = player.Rifle;
                int layer = oldRifle.gameObject.layer;
                player.Rifle = ReplaceProp(oldRifle, "Assets/gun/Weapon_02.fbx", "Weapon_02 Grip",
                    Quaternion.Euler(0, 90, 0), .17f, true, out Bounds rifleBounds);
                player.Muzzle.SetParent(player.Rifle, false);
                player.Muzzle.position = new Vector3(rifleBounds.center.x, rifleBounds.center.y, rifleBounds.max.z);
                player.Rifle.rotation = player.transform.rotation;
                Object.DestroyImmediate(oldRifle.gameObject);
                foreach (Renderer renderer in player.Rifle.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = layer;
            }

            if (player.Companion.Shield.name != "Police Shield Grip")
            {
                Transform oldShield = player.Companion.Shield;
                int layer = oldShield.gameObject.layer;
                player.Companion.Shield = ReplaceProp(oldShield, "Assets/police-shield/source/Police Shield.fbx",
                    "Police Shield Grip", Quaternion.identity, .21f, false, out _);
                player.Companion.Shield.rotation = player.Companion.transform.rotation;
                Object.DestroyImmediate(oldShield.gameObject);
                foreach (Renderer renderer in player.Companion.Shield.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = layer;
            }
            player.BulletModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/gun/BulletLite_02.fbx");
            player.BulletModelScale = 1.15f;
            int count = PixelizeBackground(GameObject.Find("KayKit City"));
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("KayKit scene save failed.");
            return "Saved equipment and camera. Background pixel materials: " + count;
        }

        private static Transform ReplaceProp(Transform old, string path, string name, Quaternion orientation,
            float size, bool measureLength, out Bounds bounds)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(old.parent, false);
            holder.SetPositionAndRotation(old.position, Quaternion.identity);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
            model.transform.localRotation = orientation;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bounds = Measure(renderers);
            model.transform.localScale *= size / (measureLength ? bounds.size.z : bounds.size.y);
            bounds = Measure(renderers);
            model.transform.position += holder.position - bounds.center;
            bounds = Measure(renderers);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return holder;
        }

        private static Bounds Measure(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static int PixelizeBackground(GameObject city)
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets/CpcPrototype/Survival/Generated/KayKitDepth", "BackgroundPixel");
            var textures = new Dictionary<Texture2D, Texture2D>();
            var materials = new Dictionary<Material, Material>();
            foreach (Renderer renderer in city.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null || !(source.mainTexture is Texture2D input) ||
                        AssetDatabase.GetAssetPath(source).StartsWith(FOLDER + "/", StringComparison.Ordinal)) continue;
                    if (!materials.TryGetValue(source, out Material pixel))
                    {
                        if (!textures.TryGetValue(input, out Texture2D texture))
                        {
                            texture = SurvivalPixelSetup.PixelTexture(input, BACKGROUND_RESOLUTION, FOLDER);
                            textures.Add(input, texture);
                        }
                        pixel = new Material(source) { mainTexture = texture };
                        string name = source.name.Replace('/', '_').Replace('\\', '_');
                        AssetDatabase.CreateAsset(pixel, AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/" + name + ".mat"));
                        materials.Add(source, pixel);
                    }
                    slots[i] = pixel;
                    changed = true;
                }
                if (!changed) continue;
                renderer.sharedMaterials = slots;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return materials.Count;
        }
    }
}
