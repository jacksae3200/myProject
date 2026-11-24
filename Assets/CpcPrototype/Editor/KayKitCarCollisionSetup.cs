using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class KayKitCarCollisionSetup
    {
        private const int STRIP_COUNT = 12;

        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "KayKit_DepthSurvival" || EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Open KayKit in Edit mode.");

            Transform city = GameObject.Find("KayKit City").transform;
            Transform street = city.Find("Pixel Street Props");
            Transform[] cars = street.Cast<Transform>().Where(t => t.name == "Pixel Car").ToArray();
            int strips = 0;
            foreach (Transform car in cars) strips += FitCar(car);

            var companion = Object.FindAnyObjectByType<ShieldCompanion>();
            companion.GroundedNavigation = true;
            var surface = city.GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            const string folder = "Assets/CpcPrototype/Survival/Generated/KayKitDepth/PixelStreet/";
            AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath(folder + "StreetGroundNavigation.asset"));
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("KayKit save failed.");
            return $"Updated {cars.Length} cars with {strips} silhouette strips and rebuilt navigation.";
        }

        private static int FitCar(Transform car)
        {
            var old = car.GetComponent<BoxCollider>();
            Transform quad = car.Find("Pixel Sprite Quad");
            var renderer = quad.GetComponent<MeshRenderer>();
            string path = AssetDatabase.GetAssetPath(renderer.sharedMaterial.mainTexture);
            var image = new Texture2D(2, 2);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path))) throw new IOException("Could not read " + path);
                Color32[] pixels = image.GetPixels32();
                int width = image.width;
                int height = image.height;
                int left = width;
                int right = -1;
                for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (pixels[y * width + x].a < 128) continue;
                    left = Mathf.Min(left, x);
                    right = Mathf.Max(right, x);
                }
                if (right < left) throw new IOException("Empty car silhouette " + path);

                var profile = new GameObject("Silhouette Collision");
                profile.transform.SetParent(car, false);
                var modifier = profile.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = 1; // Not Walkable
                int count = 0;
                int span = right - left + 1;
                float bottom = old.center.y - old.size.y * .5f;
                for (int i = 0; i < STRIP_COUNT; i++)
                {
                    int start = left + i * span / STRIP_COUNT;
                    int end = left + (i + 1) * span / STRIP_COUNT;
                    int top = -1;
                    for (int x = start; x < end; x++)
                    for (int y = top + 1; y < height; y++)
                        if (pixels[y * width + x].a >= 128) top = y;
                    if (top < 0) continue;
                    Vector3 edge = car.InverseTransformPoint(quad.TransformPoint(
                        new Vector3((start + end) * .5f / width - .5f, (top + 1f) / height - .5f)));
                    float size = edge.y - bottom;
                    if (size <= 0f) continue;
                    var box = profile.AddComponent<BoxCollider>();
                    box.center = new Vector3(edge.x, bottom + size * .5f, old.center.z);
                    box.size = new Vector3((end - start) * quad.localScale.x / width, size, old.size.z);
                    count++;
                }
                Object.DestroyImmediate(old);
                return count;
            }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
