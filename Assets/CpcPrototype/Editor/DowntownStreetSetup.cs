using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class DowntownStreetSetup
    {
        private const string MODEL_ROOT = "Assets/Downtown City MegaKit[Standard]/Exports/FBX (Unity)/";
        [MenuItem("Tools/CPC/Apply Downtown Street (FIX-003)")]
        public static void Apply()
        {
            if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/KayKit_DepthSurvival.unity")
                throw new InvalidOperationException("Open KayKit_DepthSurvival in edit mode first.");
            Directory.CreateDirectory("Temp/Fix003");
            if (GameObject.Find("Downtown City Street") != null)
                throw new InvalidOperationException("Downtown street is already installed.");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Temp/Fix003/Before.unity", true);
            var oldCity = GameObject.Find("KayKit City");
            // Retain the original collision envelope and six graph-referenced streetlights.
            foreach (Transform child in oldCity.transform)
            {
                if (child.name == "Walkable Street" || child.name == "Pixel Street Props") continue;
                if (child.name == "Street Collision" && (Mathf.Abs(child.position.x) > 12f || Mathf.Abs(child.position.z) > 3f)) continue;
                child.gameObject.SetActive(false);
            }
            var props = oldCity.transform.Find("Pixel Street Props");
            foreach (Transform child in props)
                if (child.GetComponentInChildren<GrappleAnchor>(true) == null) child.gameObject.SetActive(false);
            var root = new GameObject("Downtown City Street");
            Undo.RegisterCreatedObjectUndo(root, "Create Downtown Street");
            var road = new GameObject("Road and Sidewalks"); road.transform.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var tile = Model("Street_2Lane", road.transform);
                tile.transform.localScale = new Vector3(1f, .5f, .5f);
                tile.transform.position = new Vector3(-9f + i * 6f, .075f, 0f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var row = new GameObject(side < 0 ? "South Buildings" : "North Buildings");
                row.transform.SetParent(root.transform, false);
                for (int i = 0; i < 6; i++)
                {
                    string[] models = { "Building_Small_1", "Building_Medium_2_001", "Building_Large_2" };
                    var building = Model(models[(i + (side > 0 ? 1 : 0)) % 3], row.transform);
                    building.name = (side < 0 ? "South " : "North ") + (i + 1) + " - " + building.name;
                    building.transform.localScale = Vector3.one * .18f;
                    building.transform.rotation = Quaternion.Euler(0f, side < 0 ? 0f : 180f, 0f);
                    Bounds bounds = BoundsOf(building);
                    float targetZ = side * (3.2f + bounds.extents.z);
                    building.transform.position += new Vector3(-10f + 4f * i - bounds.center.x, .075f - bounds.min.y, targetZ - bounds.center.z);
                    bounds = BoundsOf(building);
                    var collision = new GameObject("Building Collision");
                    collision.transform.SetParent(building.transform, true);
                    collision.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
                    collision.transform.localScale = Vector3.one / .18f;
                    collision.AddComponent<BoxCollider>().size = bounds.size;
                }
                var sidewalk = new GameObject(side < 0 ? "South Sidewalk Collision" : "North Sidewalk Collision");
                sidewalk.transform.SetParent(root.transform, false);
                sidewalk.transform.position = new Vector3(0f, .025f, side * 2.25f);
                sidewalk.AddComponent<BoxCollider>().size = new Vector3(24f, .1f, 1.5f);
            }
            var culling = root.AddComponent<StreetBuildingCulling>();
            culling.Player = UnityEngine.Object.FindFirstObjectByType<SurvivalPlayer>();
            culling.Sniper = culling.Player.GetComponent<GrappleSniperAction>();
            culling.NegativeSide = root.transform.Find("South Buildings").GetComponentsInChildren<Renderer>();
            culling.PositiveSide = root.transform.Find("North Buildings").GetComponentsInChildren<Renderer>();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("FIX-003: Downtown road 4 tiles, buildings 6+6, camera culling saved.");
        }

        private static GameObject Model(string name, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_ROOT + name + ".fbx");
            if (asset == null) throw new FileNotFoundException(name);
            return (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        }

        private static Bounds BoundsOf(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
