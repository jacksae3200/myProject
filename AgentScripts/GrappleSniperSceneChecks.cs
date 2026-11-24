using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using CpcPrototype;
public static class GrappleSniperSceneChecks
{
 public static string Execute()
 {
 if(Application.isPlaying)throw new Exception("Stop Play mode first.");var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(current.isDirty)throw new Exception("Preserve unsaved changes before reload.");
 var scene=EditorSceneManager.OpenScene("Assets/Scenes/KayKit_DepthSurvival.unity");var p=UnityEngine.Object.FindAnyObjectByType<SurvivalPlayer>();var s=p.GrappleSniper;var g=p.Grapple;var report=new StringBuilder();
 void Check(bool ok,string label){report.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
 Check(p!=null&&g!=null&&s!=null,"Scene reload preserves player action references");
 Check(g.Anchors.Length==6&&g.Anchors.All(a=>a!=null&&a.HookPoint!=null&&a.Platform==null),"All six non-colliding bulb anchors survive scene reload");
 Check(s.Sniper.parent==s.BackSocket&&s.HandSocket==p.Body.GetBoneTransform(HumanBodyBones.RightHand),"Back and real hand sockets saved");
 Check(s.SniperHandGrip!=null&&s.SniperHandGrip.Body==p.Body&&s.SniperHandGrip.PrimaryMarker==s.SniperGripPoint&&s.SniperHandGrip.SecondaryMarker!=null,"Both hand grip references saved");
 Check(s.Sniper.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="SniperSupportPoint")==1,"Reusable prefab support marker is unique");
 Check(!s.ScopeCamera.enabled&&!s.ScopeCamera.GetComponent<AudioListener>().enabled,"Parked scope camera and listener remain disabled");
 int layer=p.Body.GetComponentInChildren<Renderer>(true).gameObject.layer;Check(s.Sniper.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==layer),"Back-mounted sniper uses player's pixel capture layer");
 Check(s.ScopeCamera.targetTexture!=null&&s.Sniper.GetComponentInChildren<ScopeLens>(true)!=null,"Scope capture and lens preserved");
 Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.Sniper.gameObject)=="Assets/sniper/Prefabs/AWM_GrappleSniper.prefab","Scene uses new reusable sniper prefab");
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/sniper/Prefabs/AWM_GrappleSniper.prefab");Check(prefab!=null&&!prefab.GetComponentInChildren<Camera>(true).enabled,"Reusable prefab defaults to camera disabled");
 var dependencies=AssetDatabase.GetDependencies("Assets/sniper/Prefabs/AWM_GrappleSniper.prefab",true);Check(dependencies.All(d=>!d.StartsWith("Assets/VCR01/VCR_AWM/")&&!d.StartsWith("Assets/CpcPrototype/ScopeLens/")),"AWM and lens dependencies migrated by GUID");
 Check(dependencies.Where(d=>d.EndsWith(".mat")).Select(AssetDatabase.LoadAssetAtPath<Material>).All(m=>m!=null&&m.shader!=null&&!ShaderUtil.ShaderHasError(m.shader)),"Sniper material shaders have no compile errors");
 Check(s.ScreenPixelMaterials.Length>0&&s.ScreenPixelMaterials.All(m=>m!=null&&m.HasProperty(s.ScreenPixelProperty)&&Mathf.Approximately(m.GetFloat(s.ScreenPixelProperty),200f)),"Fullscreen pixel material reference and base value survive reload");
 Check(g.Anchors.All(a=>a.transform.parent.parent.GetComponentsInChildren<Collider>(true).Length==0),"Bulb colliders remain removed after scene reload");
 Check(Mathf.Approximately(s.SlowMotionSeconds,5f)&&Mathf.Approximately(g.MaximumHoldSeconds,8f),"Test-only timing changes were not saved");
 Selection.activeGameObject=p.gameObject;Directory.CreateDirectory("Tools/GrappleSniper");File.WriteAllText("Tools/GrappleSniper/SceneChecks.txt",report.ToString());return report.ToString();
 }
}
