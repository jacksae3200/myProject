using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitToonAndFootstepsSetup
    {
        private const string ROOT = "Assets/CpcPrototype/Survival/Generated/KayKitDepth";
        private const string OUTPUT = ROOT + "/ToonAndFootsteps";

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "KayKit_DepthSurvival" || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open KayKit in Edit mode.");
            var player = Object.FindAnyObjectByType<SurvivalPlayer>();
            if (player.TryGetComponent<KayKitFootsteps>(out _)) throw new InvalidOperationException("Already configured.");
            if (!AssetDatabase.IsValidFolder(OUTPUT)) AssetDatabase.CreateFolder(ROOT, "ToonAndFootsteps");
            Shader toon = Shader.Find("CpcPrototype/Pixel Toon");
            if (toon == null || ShaderUtil.ShaderHasError(toon)) throw new InvalidOperationException("Pixel Toon shader failed to compile.");
            AudioClip[] footsteps = SplitFootsteps();
            ConfigureAudio(player, footsteps);

            var actorMaterials = new Dictionary<Material, Material>();
            var propMaterials = new Dictionary<Material, Material>();
            var compositeMaterials = new Dictionary<Material, Material>();
            var actors = Object.FindObjectsByType<ActorPixelQuad>(FindObjectsSortMode.None);
            foreach (ActorPixelQuad actor in actors)
            {
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if ((actor.Capture.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null) materials[i] = Convert(materials[i], toon, actorMaterials, "Model", 0f);
                    renderer.sharedMaterials = materials;
                }
                actor.Quad.sharedMaterial = Convert(actor.Quad.sharedMaterial, toon, compositeMaterials, "Capture", .57f);
                actor.Quad.shadowCastingMode = ShadowCastingMode.On;
            }

            Transform street = GameObject.Find("KayKit City").transform.Find("Pixel Street Props");
            int props = 0;
            foreach (MeshRenderer sprite in street.GetComponentsInChildren<MeshRenderer>())
            {
                if (sprite.name != "Pixel Sprite Quad" && sprite.name != "Pixel Bush" && sprite.name != "Pixel Dumpster") continue;
                sprite.sharedMaterial = Convert(sprite.sharedMaterial, toon, propMaterials, "Prop", .52f);
                sprite.shadowCastingMode = ShadowCastingMode.On;
                sprite.receiveShadows = true;
                props++;
            }

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(43f, 140f, 0f);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("KayKit save failed.");
            return $"Saved {actors.Length} pixel actors, {props} lit props and {footsteps.Length} walk clips.";
        }

        private static Material Convert(Material source, Shader shader, Dictionary<Material, Material> cache, string kind, float cutoff)
        {
            if (cache.TryGetValue(source, out Material material)) return material;
            material = new Material(shader);
            if (source.HasProperty("_BaseMap"))
            {
                material.mainTexture = source.GetTexture("_BaseMap");
                material.mainTextureOffset = source.GetTextureOffset("_BaseMap");
                material.mainTextureScale = source.GetTextureScale("_BaseMap");
            }
            else if (source.HasProperty("_MainTex"))
            {
                material.mainTexture = source.mainTexture;
                material.mainTextureOffset = source.mainTextureOffset;
                material.mainTextureScale = source.mainTextureScale;
            }

            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.color : Color.white;
            material.SetColor("_BaseColor", tint);
            if (kind == "Model" && source.IsKeywordEnabled("_ALPHATEST_ON") && source.HasProperty("_Cutoff"))
                cutoff = source.GetFloat("_Cutoff");
            material.SetFloat("_Cutoff", cutoff);
            if (source.HasProperty("_Cull")) material.SetFloat("_Cull", source.GetFloat("_Cull"));
            if (kind == "Capture") material.SetFloat("_CaptureComposite", 1f);
            if (kind == "Prop")
            {
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_Ambient", new Color(.49f, .56f, .72f));
                material.SetFloat("_LightScale", .72f);
            }
            if (cutoff > 0f) material.renderQueue = (int)RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath($"{OUTPUT}/{kind} {source.name}.mat"));
            cache.Add(source, material);
            return material;
        }

        private static void ConfigureAudio(SurvivalPlayer player, AudioClip[] footsteps)
        {
            var effects = player.CombatEffects;
            effects.Gunfire = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/soundEffect/gunfire.wav");
            var water = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/soundEffect/waterWalk.wav");
            foreach (GameObject actor in new[] { player.gameObject, player.Companion.gameObject })
            {
                var steps = actor.AddComponent<KayKitFootsteps>();
                steps.Effects = effects;
                steps.WalkClips = footsteps;
                steps.WaterWalk = water;
                var source = steps.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = actor == player.gameObject ? 0f : .65f;
                source.minDistance = 2f;
                source.maxDistance = 15f;
            }
            if (Object.FindAnyObjectByType<AudioListener>() == null) player.View.gameObject.AddComponent<AudioListener>();

            var footprint = new Material(Shader.Find("CpcPrototype/Combat Effect"))
            {
                mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/effect/footprint.png"),
                mainTextureOffset = new Vector2(490f / 1600f, 25f / 1600f),
                mainTextureScale = new Vector2(600f / 1600f, 1570f / 1600f)
            };
            footprint.SetFloat("_MaskMode", 3f);
            AssetDatabase.CreateAsset(footprint, OUTPUT + "/Bloody Footprint.mat");
            effects.FootprintMaterial = footprint;
        }

        private static AudioClip[] SplitFootsteps()
        {
            var source = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/soundEffect/walk.wav");
            var samples = new float[source.samples * source.channels];
            if (!source.GetData(samples, 0)) throw new IOException("Could not read walk.wav.");
            var onsets = new List<int>();
            int lastPeak = -source.frequency;
            for (int frame = 0; frame < source.samples; frame++)
            {
                bool peak = false;
                for (int channel = 0; channel < source.channels; channel++)
                    peak |= Mathf.Abs(samples[frame * source.channels + channel]) > .12f;
                if (!peak) continue;
                if (frame - lastPeak > source.frequency * .33f) onsets.Add(frame);
                lastPeak = frame;
            }
            if (onsets.Count != 6) throw new IOException($"Expected 6 walk sounds, found {onsets.Count}.");

            var clips = new AudioClip[onsets.Count];
            for (int i = 0; i < clips.Length; i++)
            {
                int start = Mathf.Max(0, onsets[i] - source.frequency / 25);
                int end = Mathf.Min(source.samples, onsets[i] + source.frequency * 3 / 5);
                string path = $"{OUTPUT}/Walk {i + 1}.wav";
                WriteWave(path, source, samples, start, end);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return clips;
        }

        private static void WriteWave(string path, AudioClip clip, float[] samples, int start, int end)
        {
            using var writer = new BinaryWriter(File.Create(path));
            int bytes = (end - start) * clip.channels * 2;
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(bytes + 36);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(bytes);
            int fade = clip.frequency / 100;
            for (int frame = start; frame < end; frame++)
            {
                float gain = Mathf.Clamp01(Mathf.Min(frame - start, end - 1 - frame) / (float)fade);
                for (int channel = 0; channel < clip.channels; channel++)
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[frame * clip.channels + channel] * gain, -1f, 1f) * short.MaxValue));
            }
        }
    }
}

