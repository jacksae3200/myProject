using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class PrototypeSceneBuilder
    {
        public const string SCENE_PATH = "Assets/CpcPrototype/Scenes/PicoCpcTest.unity";
        public const string SETTINGS_PATH = "Assets/CpcPrototype/Settings/GemmaLocalSettings.asset";
        private const string PICO_PREFAB = "Assets/Picola/PicoChan/Prefabs/PicoChan.prefab";

        [MenuItem("Tools/CPC/Create PICOCHAN Test Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
            if (File.Exists(SCENE_PATH)) throw new System.InvalidOperationException("Test scene already exists. Open it with Tools/CPC/Open PICOCHAN Test Scene.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PICO_PREFAB);
            if (prefab == null) throw new System.InvalidOperationException("PICOCHAN prefab is missing.");
            Directory.CreateDirectory("Assets/CpcPrototype/Scenes");
            Directory.CreateDirectory("Assets/CpcPrototype/Settings");
            Directory.CreateDirectory("Assets/CpcPrototype/Materials");
            AssetDatabase.Refresh();
            // 기존 씬을 덮어쓰지 않고 새 씬에 테스트 오브젝트를 만든다.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.7f, 0.8f);
            Material groundMaterial = MakeMaterial("ArenaGround", new Color(0.24f, 0.34f, 0.3f));
            Material monsterMaterial = MakeMaterial("MonsterRed", new Color(0.85f, 0.18f, 0.16f));
            Material markerMaterial = MakeMaterial("HomeMarker", new Color(0.9f, 0.55f, 0.15f));
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestArena_Ground"; ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(80f, 0.5f, 80f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.8f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            GameObject player = CreateCharacter("PICOCHAN_Player", new Vector3(0f, 0.1f, 0f), prefab, CombatTeam.Player, 100f);
            ThirdPersonPlayer controller = player.AddComponent<ThirdPersonPlayer>();
            GameObject cameraObject = new GameObject("ThirdPersonCamera"); cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 58f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.13f, 0.19f, 0.28f);
            cameraObject.AddComponent<AudioListener>();
            ThirdPersonCamera cameraRig = cameraObject.AddComponent<ThirdPersonCamera>();
            cameraRig.Configure(player.transform, controller); controller.Configure(camera.transform);
            cameraObject.transform.SetPositionAndRotation(new Vector3(0, 3.1f, -4.7f), Quaternion.Euler(22f, 0f, 0f));

            GameObject companion = CreateCharacter("PICOCHAN_CPC", new Vector3(-2f, 0.1f, -1f), prefab, CombatTeam.Companion, 100f);
            CpcLlmSettings settings = AssetDatabase.LoadAssetAtPath<CpcLlmSettings>(SETTINGS_PATH);
            if (settings == null) { settings = ScriptableObject.CreateInstance<CpcLlmSettings>(); AssetDatabase.CreateAsset(settings, SETTINGS_PATH); }
            CpcResponsePolicyInstaller.Install();
            LocalLlmClient llm = companion.AddComponent<LocalLlmClient>(); llm.Configure(settings);
            CompanionCpc cpc = companion.AddComponent<CompanionCpc>(); cpc.Configure(controller, player.GetComponent<Health>());

            Vector3[] positions = { new Vector3(0f, 0.1f, 10f), new Vector3(9f, 0.1f, 15f), new Vector3(-10f, 0.1f, 18f) };
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject monster = new GameObject("CubeMonster_" + (i + 1)); monster.transform.position = positions[i];
                CharacterController capsule = monster.AddComponent<CharacterController>();
                capsule.height = 1.2f; capsule.radius = 0.55f; capsule.center = Vector3.up * 0.65f;
                Health health = monster.AddComponent<Health>(); health.Configure(CombatTeam.Monster, 100f);
                CharacterMotor motor = monster.AddComponent<CharacterMotor>();
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.name = "MonsterVisual";
                visual.transform.SetParent(monster.transform, false); visual.transform.localPosition = Vector3.up * 0.65f;
                visual.transform.localScale = Vector3.one * 1.2f;
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.GetComponent<Renderer>().sharedMaterial = monsterMaterial; motor.ConfigureVisual(visual.transform);
                CubeMonsterBrain brain = monster.AddComponent<CubeMonsterBrain>();
                brain.Configure(player.GetComponent<Health>(), companion.GetComponent<Health>());
                GameObject home = GameObject.CreatePrimitive(PrimitiveType.Cylinder); home.name = "HomeMarker_" + (i + 1);
                home.transform.position = new Vector3(positions[i].x, 0.025f, positions[i].z);
                home.transform.localScale = new Vector3(2.2f, 0.025f, 2.2f);
                Object.DestroyImmediate(home.GetComponent<Collider>()); home.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            }
            GameObject hud = new GameObject("PrototypeHUD");
            hud.AddComponent<PrototypeHud>().Configure(controller, player.GetComponent<Health>(), cpc, camera);
            CpcChatInstaller.Install();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            // 기존 씬에 미저장 변경이 있으면 닫지 않고 보존한다.
            bool otherDirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            { Scene other = SceneManager.GetSceneAt(i); if (other != scene && other.isDirty) otherDirty = true; }
            if (!otherDirty) EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            else Debug.LogWarning("[CPC] 기존 씬의 미저장 변경을 보존했습니다. 테스트 전 다른 씬을 저장하고 PicoCpcTest만 여세요.");
            Selection.activeObject = settings;
            Debug.Log("[CPC] PICOCHAN test scene created. Settings: " + SETTINGS_PATH);
        }

        [MenuItem("Tools/CPC/Open PICOCHAN Test Scene")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SCENE_PATH);
        }

        private static GameObject CreateCharacter(string name, Vector3 position, GameObject prefab, CombatTeam team, float health)
        {
            GameObject root = new GameObject(name); root.transform.position = position;
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.6f; controller.radius = 0.3f; controller.center = Vector3.up * 0.8f;
            controller.stepOffset = 0.3f; controller.skinWidth = 0.04f;
            root.AddComponent<Health>().Configure(team, health);
            CharacterMotor motor = root.AddComponent<CharacterMotor>();
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            visual.name = "PICOCHAN_Visual";
            // 실제 메시 높이에 맞춰 캐릭터 컨트롤러와 시점을 일관되게 유지한다.
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.y > 0.01f)
                {
                    float scale = 1.6f / bounds.size.y;
                    visual.transform.localScale *= scale;
                    visual.transform.localPosition = Vector3.up * ((position.y - bounds.min.y) * scale);
                }
            }
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            motor.ConfigureVisual(visual.transform);
            root.AddComponent<PicoProceduralPose>();
            return root;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            string path = "Assets/CpcPrototype/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path); return material;
        }
    }
}
