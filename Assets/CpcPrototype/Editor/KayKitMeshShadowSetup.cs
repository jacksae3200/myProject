using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class KayKitMeshShadowSetup
    {
        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "KayKit_DepthSurvival" || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open KayKit in Edit mode.");

            var actors = Object.FindObjectsByType<ActorPixelQuad>(FindObjectsSortMode.None);
            Transform city = GameObject.Find("KayKit City").transform;
            Transform street = city.Find("Pixel Street Props");
            var projections = Object.FindObjectsByType<PixelGroundShadow>(FindObjectsSortMode.None);
            if (projections.All(p => !p.enabled)) throw new InvalidOperationException("Mesh shadows already configured.");

            int casters = 0;
            foreach (ActorPixelQuad actor in actors)
            {
                foreach (Renderer source in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if ((actor.Capture.cullingMask & (1 << source.gameObject.layer)) == 0) continue;
                    if (CreateCaster(source, source.transform) != null) casters++;
                }
                actor.Quad.shadowCastingMode = ShadowCastingMode.Off;
            }

            var originals = new HashSet<Transform>(city.Cast<Transform>().Where(t => !t.gameObject.activeSelf));
            int props = 0;
            foreach (PixelGroundShadow projection in street.GetComponentsInChildren<PixelGroundShadow>())
            {
                MeshRenderer sprite = projection.Silhouette;
                string kind = sprite.name switch
                {
                    "Pixel Bush" => "bush",
                    "Pixel Dumpster" => "dumpster",
                    _ => sprite.transform.parent.name == "Pixel Car" ? "car_" : "streetlight"
                };
                Transform original = originals.Where(t => t.name.StartsWith(kind, StringComparison.Ordinal))
                    .OrderBy(t => (t.position - sprite.transform.position).sqrMagnitude).First();
                originals.Remove(original);
                foreach (Renderer source in original.GetComponentsInChildren<Renderer>(true))
                {
                    Renderer caster = CreateCaster(source, sprite.transform.parent);
                    if (caster == null) continue;
                    Transform target = caster.transform;
                    target.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    Vector3 scale = source.transform.lossyScale;
                    Vector3 parentScale = target.parent.lossyScale;
                    target.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
                    casters++;
                }
                sprite.shadowCastingMode = ShadowCastingMode.Off;
                props++;
            }

            foreach (PixelGroundShadow projection in projections) projection.enabled = false;
            int lamps = 0;
            foreach (Light light in street.GetComponentsInChildren<Light>())
            {
                if (light.name != "Warm Bulb Light") continue;
                light.shadows = LightShadows.Soft;
                light.shadowBias = .02f;
                light.shadowNormalBias = .06f;
                var data = new SerializedObject(light.GetUniversalAdditionalLightData());
                data.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                data.ApplyModifiedProperties();
                lamps++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("KayKit save failed.");
            return $"Saved {casters} original mesh casters, {props} props, and {lamps} shadow-casting lamps.";
        }

        private static Renderer CreateCaster(Renderer source, Transform parent)
        {
            var skin = source as SkinnedMeshRenderer;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (skin == null && (!(source is MeshRenderer) || filter == null || filter.sharedMesh == null)) return null;
            if (skin != null && skin.sharedMesh == null) return null;
            var root = new GameObject(source.name + " Mesh Shadow");
            root.transform.SetParent(parent, false);
            Renderer caster;
            if (skin != null)
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = skin.sharedMesh;
                renderer.bones = skin.bones;
                renderer.rootBone = skin.rootBone;
                renderer.localBounds = skin.localBounds;
                renderer.updateWhenOffscreen = true;
                caster = renderer;
            }
            else
            {
                root.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                caster = root.AddComponent<MeshRenderer>();
            }

            caster.sharedMaterials = source.sharedMaterials;
            caster.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            caster.receiveShadows = false;
            return caster;
        }
    }
}

