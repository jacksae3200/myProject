using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class KayKitCombatEffectsSetup
    {
        private const string GENERATED = "Assets/CpcPrototype/Survival/Generated/KayKitDepth";
        private const string EFFECTS = GENERATED + "/CombatEffects";
        private const string FLASH = "Assets/effect/assets/BinbunVFX/shared/texture/flash_side/";

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "KayKit_DepthSurvival" || EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Open KayKit in Edit mode.");
            var player = Object.FindAnyObjectByType<SurvivalPlayer>();
            if (player.CombatEffects != null) throw new System.InvalidOperationException("Combat effects already configured.");
            if (!AssetDatabase.IsValidFolder(EFFECTS)) AssetDatabase.CreateFolder(GENERATED, "CombatEffects");

            var effects = new GameObject("KayKit Combat Effects").AddComponent<KayKitCombatEffects>();
            effects.Player = player;
            effects.MuzzleMaterial = CreateMaterial("Muzzle Flash", FLASH + "flash_side_01.png", new Color(6f, 3.2f, 1.1f), true);
            effects.SplashMaterial = CreateMaterial("Blood Splash", FLASH + "flash_side_06.png", new Color(.8f, .035f, .045f));
            effects.BulletHoleMaterial = CreateMaterial("Bullet Hole", "Assets/effect/bullethole.png", Color.white);
            effects.BloodMaterial = CreateMaterial("Blood Mark", "Assets/effect/blood.png", Color.white);
            effects.SplashMaterial.SetFloat("_MaskMode", 1f);
            effects.BulletHoleMaterial.SetFloat("_MaskMode", 2f);
            player.CombatEffects = effects;
            player.TracerColor = new Color(5f, 2.4f, .65f);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, EFFECTS + "/Night Bloom.asset");
            Bloom bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(.7f);
            bloom.scatter.Override(.52f);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            var volume = new GameObject("KayKit Night Bloom").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            player.View.allowHDR = true;
            player.View.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            Material bulb = CreateMaterial("Streetlamp Emission", null, new Color(5f, 2.45f, .7f), true);
            bulb.SetFloat("_Radial", 1f);
            Transform street = GameObject.Find("KayKit City").transform.Find("Pixel Street Props");
            int count = 0;
            foreach (Light light in street.GetComponentsInChildren<Light>())
            {
                if (light.name != "Warm Bulb Light") continue;
                Transform lamp = light.transform.parent.parent;
                Transform sprite = lamp.Find("Pixel Sprite Quad");
                var emitter = GameObject.CreatePrimitive(PrimitiveType.Quad);
                emitter.name = "HDR Bulb Emission";
                Object.DestroyImmediate(emitter.GetComponent<Collider>());
                emitter.transform.SetParent(light.transform, false);
                Vector3 position = light.transform.parent.position;
                position.y += .007f;
                position.z = sprite.position.z - .003f;
                emitter.transform.SetPositionAndRotation(position, Quaternion.identity);
                emitter.transform.localScale = new Vector3(.11f, .035f, 1f);
                var renderer = emitter.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = bulb;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                count++;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save KayKit.");
            return $"Configured HDR tracer, combat pools and Bloom with {count} bulbs.";
        }

        private static Material CreateMaterial(string name, string texturePath, Color tint, bool additive = false)
        {
            var material = new Material(Shader.Find("CpcPrototype/Combat Effect")) { name = name };
            if (texturePath != null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) throw new FileNotFoundException(texturePath);
                material.mainTexture = texture;
            }
            material.SetColor("_Tint", tint);
            material.SetFloat("_DestinationBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            AssetDatabase.CreateAsset(material, EFFECTS + "/" + name + ".mat");
            return material;
        }
    }
}

