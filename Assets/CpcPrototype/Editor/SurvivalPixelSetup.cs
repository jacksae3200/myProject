using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class SurvivalPixelSetup
    {
        private const string FOLDER = "Assets/CpcPrototype/Survival/Generated/Pixel";
        private const float SCALE = .25f;

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != SurvivalSceneInstaller.SCENE)
                throw new InvalidOperationException("Open the survival scene in edit mode.");
            var player = Object.FindFirstObjectByType<SurvivalPlayer>();
            if (player == null) throw new InvalidOperationException("Survival player is missing.");
            Directory.CreateDirectory(FOLDER);
            AssetDatabase.Refresh();
            var textures = new Dictionary<(Texture2D, int), Texture2D>();
            var materials = new Dictionary<(Material, int), Material>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                int resolution = renderer.GetComponentInParent<Health>() != null ||
                    renderer.transform.IsChildOf(player.View.transform) ? 32 : 64;
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null || !(source.mainTexture is Texture2D input) ||
                        AssetDatabase.GetAssetPath(source).StartsWith(FOLDER, StringComparison.Ordinal)) continue;
                    var key = (source, resolution);
                    if (!materials.TryGetValue(key, out Material pixel))
                    {
                        if (!textures.TryGetValue((input, resolution), out Texture2D texture))
                        {
                            texture = PixelTexture(input, resolution);
                            textures.Add((input, resolution), texture);
                        }

                        pixel = new Material(source) { mainTexture = texture };
                        // Preserve the shader and its material settings, including tree cutout.
                        string file = source.name.Replace('/', '_').Replace('\\', '_');
                        AssetDatabase.CreateAsset(pixel, AssetDatabase.GenerateUniqueAssetPath(FOLDER + "/" + file + "_" + resolution + ".mat"));
                        materials.Add(key, pixel);
                    }
                    slots[i] = pixel;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }

            if (player.transform.localScale.x > .25f) Resize(player);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            return $"Pixelized {textures.Count} textures and {materials.Count} materials; actor scale {player.transform.localScale.x:F4}.";
        }

        internal static Texture2D PixelTexture(Texture2D source, int resolution, string folder = FOLDER)
        {
            float factor = Mathf.Min(1f, resolution / (float)Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * factor));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * factor));
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + source.name + "_" + resolution + ".png");
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapModeU = source.wrapModeU;
            importer.wrapModeV = source.wrapModeV;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Resize(SurvivalPlayer player)
        {
            player.transform.localScale *= SCALE;
            player.CameraPivot *= SCALE;
            player.ThirdPersonOffset *= SCALE;
            player.FirstPersonOffset *= SCALE;
            player.View.nearClipPlane *= SCALE;
            player.ViewRifle.transform.localScale *= SCALE;
            player.ViewRifle.transform.localPosition *= SCALE;
            player.JumpHeight *= SCALE;
            player.Gravity *= SCALE;
            player.RollSpeed *= SCALE;
            player.MoveSpeed *= SCALE;
            player.SprintSpeed *= SCALE;
            player.TracerWidth *= SCALE;
            player.TracerLength *= SCALE;

            var companion = player.Companion;
            ResizeAgent(companion.transform);
            companion.FollowDistance *= SCALE;
            companion.FollowResumeMargin *= SCALE;
            companion.FollowSpeed *= SCALE;
            companion.SprintSpeed *= SCALE;
            companion.GuardDistance *= SCALE;
            companion.MeleeRange *= SCALE;
            companion.RetreatDistance *= SCALE;
            foreach (var zombie in companion.Monsters)
            {
                ResizeAgent(zombie.transform);
                zombie.MoveSpeed *= SCALE;
                zombie.AttackRange *= SCALE;
            }
        }

        private static void ResizeAgent(Transform actor)
        {
            actor.localScale *= SCALE;
            NavMeshAgent agent = actor.GetComponent<NavMeshAgent>();
            agent.radius *= SCALE;
            agent.height *= SCALE;
            agent.acceleration *= SCALE;
        }
    }
}
