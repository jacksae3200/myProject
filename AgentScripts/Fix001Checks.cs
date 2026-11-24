using System;using System.IO;using System.Linq;using System.Text;using System.Threading.Tasks;using UnityEngine;using UnityEngine.AI;using UnityEngine.InputSystem;using CpcPrototype;
public static class Fix001Checks
{
 private static readonly StringBuilder REPORT=new StringBuilder();
 private static void Check(bool ok,string text){REPORT.AppendLine((ok?"PASS ":"FAIL ")+text);File.WriteAllText("Tools/GrappleSniper/Fix001/Checks.txt",REPORT.ToString());if(!ok)throw new Exception(text);}
 private static async Task Wait(Func<bool> test,float seconds=4f){float end=Time.realtimeSinceStartup+seconds;while(!test()&&Time.realtimeSinceStartup<end)await Task.Delay(15);if(!test())throw new Exception("Wait timed out");}
 private static void Place(SurvivalPlayer p,Vector3 v){var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=v;cc.enabled=true;p.ResetMotion();Physics.SyncTransforms();}
 public static async Task<string> Execute()
 {
 Directory.CreateDirectory("Tools/GrappleSniper/Fix001");REPORT.Clear();Check(Application.isPlaying,"Actual Play mode");
 var p=UnityEngine.Object.FindAnyObjectByType<SurvivalPlayer>();var g=p.Grapple;var s=p.GrappleSniper;var oldMouse=Mouse.current;var oldKeyboard=Keyboard.current;var mouse=InputSystem.AddDevice<Mouse>("Fix001Mouse");var keyboard=InputSystem.AddDevice<Keyboard>("Fix001Keyboard");mouse.MakeCurrent();keyboard.MakeCurrent();
 var material=s.ScreenPixelMaterials.Single();int property=Shader.PropertyToID(s.ScreenPixelProperty);float saved=material.GetFloat(property);float timescale=Time.timeScale;int frames=Time.frameCount;
 try{
 foreach(var z in UnityEngine.Object.FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include)){z.enabled=false;foreach(var ability in z.GetComponents<ZombieAbility>())ability.enabled=false;var agent=z.GetComponent<NavMeshAgent>();if(agent!=null)agent.enabled=false;}
 foreach(var type in new[]{typeof(ShieldCompanion),typeof(CpcTactics),typeof(CpcTacticalMotor),typeof(CpcCrowdProtection)})foreach(var obj in UnityEngine.Object.FindObjectsByType(type,FindObjectsInactive.Include))((Behaviour)obj).enabled=false;
 p.Health.Configure(CombatTeam.Player,10000f);
 Check(g.Anchors.Length==6&&g.Anchors.All(a=>a.Platform==null&&a.transform.parent.parent.GetComponentsInChildren<Collider>(true).Length==0),"Six bulb platforms have no physical collider");
 Check(p.OneWayPlatforms.All(c=>c!=null),"Deleted one-way platform references removed");Check(Mathf.Approximately(saved,200f),"Active fullscreen material base value is 200");
 var anchor=g.Anchors.OrderBy(a=>(a.Position-p.transform.position).sqrMagnitude).First();
 async Task Begin(){Place(p,new Vector3(anchor.Position.x,.04f,anchor.Position.z-.32f));await Task.Delay(120);Check(g.TryBegin(anchor),"Grapple accepts marker without bulb collider");}
 await Begin();int samples=0;bool valid=true;
 while(g.IsTravelling){await Task.Delay(15);if(!g.IsTravelling)break;float height=Mathf.Approximately(p.DepthCameraOffset.y,0f)?Mathf.Max(p.CameraPivot.y,p.DepthViewSize-.16f):p.CameraPivot.y;Vector3 expected=new Vector3(p.transform.position.x,height,p.transform.position.z*.35f)+p.DepthCameraOffset;valid&=!s.IsActive&&!s.ControlsView&&s.ViewBlend==0f&&Quaternion.Angle(p.View.transform.rotation,Quaternion.LookRotation(-p.DepthCameraOffset))<.1f&&Vector3.Distance(expected,p.View.transform.position)<.03f&&Mathf.Approximately(material.GetFloat(property),saved)&&Mathf.Approximately(Time.timeScale,timescale);samples++;}
 Check(valid&&samples>5,"Third-person camera, fullscreen pixels and time scale remain unchanged throughout grapple travel");
 Check(g.Phase==GrapplePhase.Holding&&s.IsActive,"First-person entry starts only after grapple arrival");await Wait(()=>s.Phase==GrappleSniperPhase.Ready);await Task.Delay(50);Check(Mathf.Approximately(material.GetFloat(property),400f),"Actual screen pixel height doubles 200 to 400 in first person");
 Check(s.PixelActors.Where(a=>a!=null&&a.enabled).All(a=>a.CurrentResolution==Mathf.RoundToInt(a.MaxResolution*s.PixelResolutionMultiplier)),"Actor pixel capture doubling still works");
 ScreenCapture.CaptureScreenshot("Tools/GrappleSniper/Fix001/FirstPerson.png");await Wait(()=>!s.IsActive,7f);Check(Mathf.Approximately(material.GetFloat(property),saved),"Normal third-person return restores exact screen pixel value");
 // A custom initial value must be restored, rather than hard-coding 200.
 material.SetFloat(property,173f);await Begin();await Wait(()=>s.Phase==GrappleSniperPhase.Ready);await Task.Delay(40);Check(Mathf.Approximately(material.GetFloat(property),346f),"Custom screen pixel value 173 doubles to 346");p.Chat.Open();await Task.Delay(70);Check(!s.IsActive&&Mathf.Approximately(material.GetFloat(property),173f),"Chat cancellation restores custom pixel value");p.Chat.Close();
 await Begin();await Wait(()=>s.Phase==GrappleSniperPhase.Ready);s.enabled=false;Check(Mathf.Approximately(material.GetFloat(property),173f)&&!g.IsControllingMovement,"Disable restores pixel value and releases grapple");s.enabled=true;
 Place(p,anchor.Position+Vector3.up*.03f);float y=p.transform.position.y;await Task.Delay(300);Check(p.transform.position.y<y-.12f,"Player falls through the former bulb platform instead of standing on it");
 Check(Time.frameCount>frames+30,"Player loop advances throughout checks");return REPORT.ToString();
 }finally{s.RestoreImmediately();g.Release();material.SetFloat(property,saved);if(p.IsChatOpen)p.Chat.Close();InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);oldMouse?.MakeCurrent();oldKeyboard?.MakeCurrent();File.WriteAllText("Tools/GrappleSniper/Fix001/Checks.txt",REPORT.ToString());}
 }
}
