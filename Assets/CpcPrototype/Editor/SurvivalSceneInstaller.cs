using System;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class SurvivalSceneInstaller
    {
        public const string SCENE = "Assets/Scenes/ZRN_Survival.unity";
        private const string OUTPUT = "Assets/CpcPrototype/Survival/Generated";
        private const string BLINK = "Assets/Blink/Art/Animations/Animations_Starter_Pack/";
        private const string RIFLE = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/AssaultRifle/";
        private const string CHARACTERS = "Assets/Shady_3d/PREFAB/";

        [MenuItem("Tools/CPC/Install ZRN Survival Scene")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE) throw new InvalidOperationException("Open ZRN_Survival before installing.");
            if (GameObject.Find("Survival Gameplay") != null) throw new InvalidOperationException("Survival Gameplay is already installed.");
            if (!AssetDatabase.IsValidFolder(OUTPUT)) AssetDatabase.CreateFolder("Assets/CpcPrototype/Survival", "Generated");

            AvatarMask rifleMask = MakeMask("RifleUpperBody");
            AvatarMask shieldMask = MakeMask("ShieldUpperBody");
            AnimatorController playerController = MakeCharacterController("SurvivalPlayer", rifleMask, true);
            AnimatorController cpcController = MakeCharacterController("ShieldCompanion", shieldMask, false);
            AnimatorController zombieController = MakeZombieController();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Normal Camera" || root.name == "GameController" || root.name == "SnowBallFightObjects")
                    root.SetActive(false);
            }
            GameObject ground = GameObject.Find("Sapporo_Ground");
            foreach (MeshFilter mesh in ground.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh != null && mesh.GetComponent<Collider>() == null)
                    mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            }
            GameObject gameplay = new GameObject("Survival Gameplay");
            NavMeshSurface surface = gameplay.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.center = new Vector3(-55, 4, -21);
            surface.size = new Vector3(52, 16, 46);
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("Navigation bake produced no data.");
            AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/ZRN_NavMesh.asset"));

            Vector3 playerPosition = Sample(new Vector3(-63, 2.15f, -16));
            Quaternion forward = Quaternion.Euler(0, 180, 0);
            GameObject playerRoot = CreateRoot(gameplay.transform, "Player | Helmet + hood", playerPosition, forward, CombatTeam.Player, 100);
            CharacterController controller = playerRoot.AddComponent<CharacterController>();
            controller.height = 1.78f;
            controller.radius = .32f;
            controller.center = Vector3.up * .89f;
            controller.stepOffset = .3f;
            SurvivalPlayer player = playerRoot.AddComponent<SurvivalPlayer>();
            player.Body = AddModel(playerRoot.transform, CHARACTERS + "Apocalyptic character (Helmet +hood).prefab", playerController, 1.78f, -.15f);
            player.BodyRenderers = player.Body.GetComponentsInChildren<Renderer>(true);
            Transform rifle = AttachProp(player.Body, HumanBodyBones.RightHand, "Cube Rifle", new Vector3(.12f, .14f, .72f),
                new Vector3(.025f, .025f, .22f), new Vector3(0, 0, 0), new Color(.17f, .20f, .23f));
            player.Rifle = rifle;
            player.Muzzle = new GameObject("Muzzle").transform;
            player.Muzzle.SetParent(rifle, false);
            player.Muzzle.localPosition = Vector3.forward * .53f;
            var cameraObject = new GameObject("Survival Camera");
            cameraObject.transform.SetParent(gameplay.transform, false);
            cameraObject.tag = "MainCamera";
            player.View = cameraObject.AddComponent<Camera>();
            player.View.nearClipPlane = .035f;
            cameraObject.AddComponent<AudioListener>();
            GameObject viewRifle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            viewRifle.name = "First Person Cube Rifle";
            Object.DestroyImmediate(viewRifle.GetComponent<Collider>());
            viewRifle.transform.SetParent(cameraObject.transform, false);
            viewRifle.transform.localPosition = new Vector3(.21f, -.22f, .48f);
            viewRifle.transform.localScale = new Vector3(.1f, .12f, .63f);
            viewRifle.GetComponent<Renderer>().sharedMaterial = rifle.GetComponent<Renderer>().sharedMaterial;
            viewRifle.SetActive(false);
            player.ViewRifle = viewRifle;

            Vector3 companionPosition = Sample(playerPosition + new Vector3(-.5f, 0, 2f));
            GameObject cpcRoot = CreateRoot(gameplay.transform, "CPC | Helmet", companionPosition, forward, CombatTeam.Companion, 100);
            AddAgent(cpcRoot, .32f, 1.78f);
            ShieldCompanion companion = cpcRoot.AddComponent<ShieldCompanion>();
            companion.Player = player;
            companion.Body = AddModel(cpcRoot.transform, CHARACTERS + "Apocalyptic character (Helmet).prefab", cpcController, 1.78f, -.15f);
            companion.Shield = AttachProp(companion.Body, HumanBodyBones.LeftHand, "Cube Shield", new Vector3(.67f, .93f, .075f),
                new Vector3(.02f, -.13f, .09f), new Vector3(0, 0, 0), new Color(.25f, .43f, .61f));
            cpcRoot.GetComponent<LocalLlmClient>().Configure(AssetDatabase.LoadAssetAtPath<CpcLlmSettings>(PrototypeSceneBuilder.SETTINGS_PATH));
            player.Companion = companion;
            var chatObject = new GameObject("Survival Chat");
            chatObject.transform.SetParent(gameplay.transform, false);
            player.Chat = chatObject.AddComponent<CpcChatPanel>();
            player.Chat.ConfigurePanel(AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/CpcPrototype/Settings/ChatPanelSettings.asset"));

            Vector3[] spots = { new Vector3(-62, 2, -23), new Vector3(-60, 2, -29), new Vector3(-63, 2, -30) };
            var zombies = new SurvivalZombie[spots.Length];
            for (int i = 0; i < zombies.Length; i++)
            {
                Vector3 position = Sample(spots[i]);
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(position, playerPosition, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Zombie spawn is disconnected: " + position);
                GameObject zombieRoot = CreateRoot(gameplay.transform, "Zombie " + (i + 1), position, Quaternion.identity, CombatTeam.Monster, 85);
                AddAgent(zombieRoot, .34f, 1.8f);
                zombies[i] = zombieRoot.AddComponent<SurvivalZombie>();
                zombies[i].Player = player;
                zombies[i].Companion = companion;
                zombies[i].Body = AddModel(zombieRoot.transform, "Assets/ArtStore3D/Zombie/Prefab/Zombie.prefab", zombieController, 1.8f);
            }
            companion.Monsters = zombies;
            ApplySceneMaterials();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Selection.activeObject = playerRoot;
        }

        public static void ApplySceneMaterials()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open ZRN_Survival in edit mode.");
            var materials = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null) continue;
                    bool zombie = source.shader.name == "Standard" && source.name == "Zombie_Mat";
                    bool cutout = source.name.Contains("TransP") && source.HasProperty("_AlphaClip") &&
                        source.GetFloat("_AlphaClip") < .5f && source.mainTexture != null;
                    if (!zombie && !cutout) continue;
                    if (!materials.TryGetValue(source, out Material target))
                    {
                        string path = OUTPUT + "/" + source.name + "_Survival.mat";
                        target = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (target == null)
                        {
                            target = new Material(source);
                            if (zombie)
                            {
                                target.shader = Shader.Find("Universal Render Pipeline/Lit");
                                target.SetTexture("_BaseMap", source.mainTexture);
                                target.SetColor("_BaseColor", source.color);
                                target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                                if (target.GetTexture("_BumpMap") != null) target.EnableKeyword("_NORMALMAP");
                            }
                            if (cutout)
                            {
                                target.SetFloat("_AlphaClip", 1);
                                target.SetFloat("_Cutoff", .45f);
                                target.SetFloat("_Cull", 0);
                                target.EnableKeyword("_ALPHATEST_ON");
                                target.SetOverrideTag("RenderType", "TransparentCutout");
                                target.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                            }
                            AssetDatabase.CreateAsset(target, path);
                        }
                        materials.Add(source, target);
                    }
                    slots[i] = target;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Vector3 Sample(Vector3 point)
        {
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                throw new InvalidOperationException("No walkable spawn near " + point);
            return hit.position;
        }

        private static GameObject CreateRoot(Transform parent, string name, Vector3 position, Quaternion rotation, CombatTeam team, float hp)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            root.AddComponent<Health>().Configure(team, hp);
            return root;
        }

        private static void AddAgent(GameObject root, float radius, float height)
        {
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = height;
            agent.acceleration = 20;
            agent.angularSpeed = 540;
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.radius = radius;
            collider.height = height;
            collider.center = Vector3.up * height * .5f;
        }

        private static Animator AddModel(Transform parent, string path, RuntimeAnimatorController controller, float height, float groundOffset = 0)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                throw new InvalidOperationException("Valid humanoid avatar required: " + path);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = height / bounds.size.y;
            model.transform.localScale *= scale;
            model.transform.localPosition += Vector3.up * ((parent.position.y - bounds.min.y) * scale + groundOffset);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static Transform AttachProp(Animator body, HumanBodyBones bone, string name, Vector3 size,
            Vector3 offset, Vector3 rotation, Color color)
        {
            Transform hand = body.GetBoneTransform(bone);
            if (hand == null) throw new InvalidOperationException("Hand missing: " + bone);
            var grip = new GameObject(name + " Grip").transform;
            grip.SetParent(hand, false);
            grip.rotation = body.transform.rotation;
            grip.position = hand.position + body.transform.TransformDirection(offset);
            grip.localRotation *= Quaternion.Euler(rotation);
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = name;
            Object.DestroyImmediate(prop.GetComponent<Collider>());
            prop.transform.SetParent(grip, false);
            Vector3 scale = grip.lossyScale;
            prop.transform.localScale = new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/" + name + ".mat"));
            prop.GetComponent<Renderer>().sharedMaterial = material;
            return prop.transform;
        }

        private static AvatarMask MakeMask(string name)
        {
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                    part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                    part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            }
            AssetDatabase.CreateAsset(mask, AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/" + name + ".mask"));
            return mask;
        }

        private static AnimatorController MakeCharacterController(string name, AvatarMask mask, bool rifle)
        {
            AnimatorController controller = NewController(name);
            AnimatorStateMachine lower = controller.layers[0].stateMachine;
            AnimatorState locomotion = AddLocomotion(controller, lower, Movement("Idle"), Movement("RunForward"), Movement("Sprint"));
            lower.defaultState = locomotion;
            AddState(lower, "Jump", Movement("Jumps", "Jump_Up"));
            AddState(lower, "Fall", Movement("FallingLoop"));
            AddState(lower, "Roll", Movement("RollForward"));
            controller.AddLayer(rifle ? "Rifle Upper Body" : "Shield Upper Body");
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer upper = layers[1];
            upper.avatarMask = mask;
            upper.defaultWeight = rifle ? 1 : 0;
            AnimatorStateMachine upperMachine = upper.stateMachine;
            if (rifle)
            {
                AnimatorState aim = AddState(upperMachine, "Aim", Clip(RIFLE + "HumanM@AssaultRifle_Aim01.fbx"));
                upperMachine.defaultState = aim;
                AnimatorState fire = AddState(upperMachine, "Fire", Clip(RIFLE + "HumanM@AssaultRifle_Aim01_Shoot01.fbx"));
                ReturnAfterClip(fire, aim);
                AddState(upperMachine, "Reload", Clip(RIFLE + "HumanM@AssaultRifle_Reload01.fbx"));
            }
            else
            {
                AnimatorState block = AddState(upperMachine, "Block", Combat("BlockingLoop"));
                upperMachine.defaultState = block;
                ReturnAfterClip(AddState(upperMachine, "Taunt", Combat("Buff")), block);
                ReturnAfterClip(AddState(upperMachine, "Melee", Combat("MeleeAttack_TwoHanded")), block);
            }
            controller.layers = layers;
            return controller;
        }

        private static AnimatorController MakeZombieController()
        {
            AnimatorController controller = NewController("SurvivalZombie");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState move = AddLocomotion(controller, machine, Movement("Idle"), Movement("RunForward"), Movement("RunForward"));
            machine.defaultState = move;
            ReturnAfterClip(AddState(machine, "Attack", Combat("MeleeAttack_OneHanded")), move);
            AddState(machine, "Dead", Combat("Death"));
            return controller;
        }

        private static AnimatorController NewController(string name)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(
                AssetDatabase.GenerateUniqueAssetPath(OUTPUT + "/" + name + ".controller"));
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            return controller;
        }

        private static AnimatorState AddLocomotion(AnimatorController controller, AnimatorStateMachine machine,
            AnimationClip idle, AnimationClip run, AnimationClip sprint)
        {
            var blend = new BlendTree { name = "Movement Blend", blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(blend, controller);
            blend.AddChild(idle, 0);
            blend.AddChild(run, .5f);
            blend.AddChild(sprint, 1);
            AnimatorState state = machine.AddState("Locomotion");
            state.motion = blend;
            return state;
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;
            return state;
        }

        private static void ReturnAfterClip(AnimatorState from, AnimatorState to)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = .96f;
            transition.duration = .1f;
        }

        private static AnimationClip Movement(string name, string clip = null) => Clip(BLINK + "Movement/" + name + ".fbx", clip);
        private static AnimationClip Combat(string name) => Clip(BLINK + "Combat/" + name + ".fbx");
        private static AnimationClip Clip(string path, string name = null)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(clip => clip.humanMotion && !clip.name.StartsWith("__preview__", StringComparison.Ordinal) && clip.name != "tpose");
            AnimationClip selected = name == null ? clips.FirstOrDefault() : clips.FirstOrDefault(clip => clip.name == name);
            return selected != null ? selected : throw new InvalidOperationException("Missing humanoid clip: " + path + " " + name);
        }
    }
}
