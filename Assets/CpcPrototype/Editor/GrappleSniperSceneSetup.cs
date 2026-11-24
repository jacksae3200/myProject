using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CpcPrototype.Editor
{
    public static class GrappleSniperSceneSetup
    {
        private const string SCENE = "Assets/Scenes/KayKit_DepthSurvival.unity";
        private const string PREFAB = "Assets/sniper/Prefabs/AWM_GrappleSniper.prefab";

        [MenuItem("Tools/CPC/Install Grapple And Sniper")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before installation.");
            var player = Object.FindAnyObjectByType<SurvivalPlayer>();
            if (player == null || player.gameObject.scene.path != SCENE)
                throw new InvalidOperationException("Open KayKit_DepthSurvival first. The installer preserves the currently open scene.");
            EnsureFolder("Assets/sniper");
            EnsureFolder("Assets/sniper/Prefabs");
            MoveFolder("Assets/VCR01/VCR_AWM", "Assets/sniper/VCR_AWM");
            MoveFolder("Assets/CpcPrototype/ScopeLens", "Assets/sniper/ScopeLens");
            string shaderSource = "Assets/CpcPrototype/Survival/ScopeLens.shader";
            if (AssetDatabase.LoadAssetAtPath<Shader>(shaderSource) != null)
            {
                string error = AssetDatabase.MoveAsset(shaderSource, "Assets/sniper/ScopeLens/ScopeLens.shader");
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            var transforms = player.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var grapple = player.GetComponent<PlayerGrappleAction>();
            if (grapple == null) grapple = Undo.AddComponent<PlayerGrappleAction>(player.gameObject);
            grapple.Player = player;
            grapple.RopeOrigin = player.Body.GetBoneTransform(HumanBodyBones.RightHand);
            grapple.Anchors = transforms.Where(t=>t.name=="HDR Bulb Emission").Select(t=>
            {
                var a=t.GetComponent<GrappleAnchor>();
                if(a==null) a=Undo.AddComponent<GrappleAnchor>(t.gameObject);
                a.HookPoint=t;
                a.Platform=t.GetComponentInParent<BoxCollider>();
                a.RingRadius=.06f;
                EditorUtility.SetDirty(a);
                return a;
            }).ToArray();
            if (grapple.Anchors.Length == 0) throw new InvalidOperationException("No lamp bulb anchors found.");
            var action=player.GetComponent<GrappleSniperAction>();
            if(action==null) action=Undo.AddComponent<GrappleSniperAction>(player.gameObject);
            Transform weapon=transforms.FirstOrDefault(t=>t.name=="AWMOptimizedBlackURP");
            if(weapon==null && action.Sniper!=null) weapon=action.Sniper;
            if(weapon==null) throw new InvalidOperationException("The scene AWMOptimizedBlackURP is required.");
            bool initialized=weapon.Find("SniperGripPoint")!=null;
            if(!initialized)
            {
                if(PrefabUtility.IsPartOfPrefabInstance(weapon.gameObject))
                    PrefabUtility.UnpackPrefabInstance(weapon.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                Undo.RecordObject(weapon,"Prepare AWM prefab");
                weapon.SetParent(null,true);
                weapon.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                weapon.localScale=Vector3.one;
                Vector3 grip=new Vector3(.52f,.12f,-.62f);
                foreach(Transform child in weapon) child.localPosition-=grip;
                var marker=new GameObject("SniperGripPoint").transform;
                marker.SetParent(weapon,false);
                var muzzle=new GameObject("SniperMuzzle").transform;
                muzzle.SetParent(weapon,false);
                var suppressor=weapon.Find("Surpressor").GetComponent<Renderer>();
                Bounds b=suppressor.bounds;
                muzzle.position=new Vector3(b.center.x,b.center.y,b.min.z-.01f);
                PrefabUtility.SaveAsPrefabAssetAndConnect(weapon.gameObject,PREFAB,InteractionMode.AutomatedAction);
            }
            if(weapon.Find("SniperSupportPoint")==null)
            {
                var support=new GameObject("SniperSupportPoint").transform;
                support.SetParent(weapon,false);
                support.localPosition=new Vector3(0f,.015f,-.45f);
            }
            var supportPoint=weapon.Find("SniperSupportPoint");
            if(PrefabUtility.IsAddedGameObjectOverride(supportPoint.gameObject))
                PrefabUtility.ApplyAddedGameObject(supportPoint.gameObject,PREFAB,InteractionMode.AutomatedAction);
            Transform socket=player.Body.GetBoneTransform(HumanBodyBones.Chest).Find("WeaponBackSocket");
            if(socket==null)
            {
                socket=new GameObject("WeaponBackSocket").transform;
                Undo.RegisterCreatedObjectUndo(socket.gameObject,"Create weapon back socket");
                socket.SetParent(player.Body.GetBoneTransform(HumanBodyBones.Chest),false);
            }
            action.Player=player;
            action.Grapple=grapple;
            action.Sniper=weapon;
            action.ScopeEyePoint=weapon.GetComponentsInChildren<Transform>(true).Single(t=>t.name.Equals("scopeEyepoint",StringComparison.OrdinalIgnoreCase));
            action.ScopeCamera=weapon.GetComponentsInChildren<Camera>(true).Single(c=>c.name=="scopeViewPort");
            action.SniperMuzzle=weapon.Find("SniperMuzzle");
            action.SniperGripPoint=weapon.Find("SniperGripPoint");
            action.Head=player.Body.GetBoneTransform(HumanBodyBones.Head);
            action.BackSocket=socket;
            action.HandSocket=player.Body.GetBoneTransform(HumanBodyBones.RightHand);
            var handGrip=weapon.GetComponent<EquipmentGrip>();
            if(handGrip==null) handGrip=Undo.AddComponent<EquipmentGrip>(weapon.gameObject);
            handGrip.Body=player.Body;
            handGrip.Equipment=weapon;
            handGrip.PrimaryMarker=action.SniperGripPoint;
            handGrip.SecondaryMarker=weapon.Find("SniperSupportPoint");
            action.SniperHandGrip=handGrip;
            EditorUtility.SetDirty(handGrip);
            action.PlayerPixels=player.GetComponent<ActorPixelQuad>();
            action.PixelActors=Object.FindObjectsByType<ActorPixelQuad>(FindObjectsInactive.Include).Where(a=>a.gameObject.scene==player.gameObject.scene).ToArray();
            player.Grapple=grapple;
            player.GrappleSniper=action;
            weapon.SetParent(socket,false);
            weapon.localPosition=action.SniperBackPosition;
            weapon.localRotation=Quaternion.Euler(action.SniperBackRotation);
            weapon.localScale=Vector3.one*(action.SniperWorldScale/Mathf.Abs(socket.lossyScale.x));
            int playerLayer=player.Body.GetComponentInChildren<Renderer>(true).gameObject.layer;
            foreach(var t in weapon.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer=playerLayer;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }
            action.ScopeCamera.cullingMask=player.View.cullingMask;
            action.ScopeCamera.nearClipPlane=.003f;
            action.ScopeCamera.farClipPlane=1000f;
            action.ScopeCamera.enabled=false;
            var listener=action.ScopeCamera.GetComponent<AudioListener>();
            if(listener!=null) listener.enabled=false;
            var lens=weapon.GetComponentInChildren<ScopeLens>(true);
            if(lens!=null) lens.Configure(action.ScopeCamera,lens.GetComponent<MeshRenderer>());
            foreach(var o in new Object[]{player,grapple,action,weapon,action.ScopeCamera})EditorUtility.SetDirty(o);
            // The reusable prefab contains the scene-authored scope and lens, with GUID-preserving asset moves.
            PrefabUtility.RecordPrefabInstancePropertyModifications(weapon);
            PrefabUtility.RecordPrefabInstancePropertyModifications(action.ScopeCamera);
            if(listener!=null)PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
            if(lens!=null)PrefabUtility.RecordPrefabInstancePropertyModifications(lens);
            GrappleFix001Setup.Apply(player);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            var prefabRoot=PrefabUtility.LoadPrefabContents(PREFAB);
            try
            {
                var prefabCamera=prefabRoot.GetComponentsInChildren<Camera>(true).Single(c=>c.name=="scopeViewPort");
                prefabCamera.enabled=false;
                var prefabListener=prefabCamera.GetComponent<AudioListener>();
                if(prefabListener!=null)prefabListener.enabled=false;
                if(prefabRoot.transform.Find("SniperSupportPoint")==null)
                {
                    var support=new GameObject("SniperSupportPoint").transform;
                    support.SetParent(prefabRoot.transform,false);
                    support.localPosition=new Vector3(0f,.015f,-.45f);
                }
                PrefabUtility.SaveAsPrefabAsset(prefabRoot,PREFAB);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(player.gameObject.scene))throw new InvalidOperationException("Scene save failed.");
            Selection.activeGameObject=player.gameObject;
            Debug.Log($"GRAPPLE_SNIPER_INSTALLED: {grapple.Anchors.Length} lamps, prefab {PREFAB}, {action.PixelActors.Length} pixel actors.");
        }

        private static void MoveFolder(string source,string destination)
        {
            if(!AssetDatabase.IsValidFolder(source))return;
            if(AssetDatabase.IsValidFolder(destination))throw new InvalidOperationException("Destination already exists: "+destination);
            string error=AssetDatabase.MoveAsset(source,destination);
            if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
        }
        private static void EnsureFolder(string folder)
        {
            if(AssetDatabase.IsValidFolder(folder))return;
            string parent=System.IO.Path.GetDirectoryName(folder).Replace('\\','/');
            AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(folder));
        }
    }
}
