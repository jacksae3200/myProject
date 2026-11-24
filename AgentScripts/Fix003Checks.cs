using System;using System.IO;using System.Linq;using System.Text;using System.Threading.Tasks;
using UnityEngine;using UnityEngine.Rendering;using UnityEngine.AI;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;using CpcPrototype;
public static class Fix003Checks
{
 private static readonly StringBuilder REPORT=new StringBuilder();
 private static void Check(bool ok,string label){REPORT.AppendLine((ok?"PASS ":"FAIL ")+label);File.WriteAllText("Tools/Fix003/Checks.txt",REPORT.ToString());if(!ok)throw new Exception(label);}
 private static async Task Wait(Func<bool> f,int ms=6000){float end=Time.realtimeSinceStartup+ms/1000f;while(!f()&&Time.realtimeSinceStartup<end)await Task.Delay(20);if(!f())throw new Exception("Wait timed out");}
 private static void Place(SurvivalPlayer p,Vector3 position){var cc=p.GetComponent<CharacterController>();cc.enabled=false;p.transform.position=position;cc.enabled=true;p.ResetMotion();Physics.SyncTransforms();}
 public static async Task<string> Execute()
 {
 Directory.CreateDirectory("Tools/Fix003");Directory.CreateDirectory("Assets/Screenshots/Fix003");REPORT.Clear();
 var c=UnityEngine.Object.FindFirstObjectByType<StreetBuildingCulling>();var p=c.Player;var s=c.Sniper;var g=p.Grapple;
 var oldMouse=Mouse.current;var oldKeyboard=Keyboard.current;var mouse=InputSystem.AddDevice<Mouse>("Fix003Mouse");var keyboard=InputSystem.AddDevice<Keyboard>("Fix003Keyboard");mouse.MakeCurrent();keyboard.MakeCurrent();int firstFrame=Time.frameCount;bool observedThird=false,observedFirst=false,observedScope=false;
 Action<ScriptableRenderContext,Camera> observe=(ctx,cam)=>{if(cam==p.View){if(c.CullingActive) observedThird=c.NegativeSide.All(r=>r.forceRenderingOff)&&c.PositiveSide.All(r=>!r.forceRenderingOff);else observedFirst=c.NegativeSide.Concat(c.PositiveSide).All(r=>!r.forceRenderingOff);}if(cam==s.ScopeCamera&&s.IsActive)observedScope=c.NegativeSide.Concat(c.PositiveSide).All(r=>!r.forceRenderingOff);};
 RenderPipelineManager.beginCameraRendering+=observe;
 try{
 Check(Application.isPlaying,"Actual Unity Play mode");
 foreach(var z in UnityEngine.Object.FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include)){z.enabled=false;foreach(var a in z.GetComponents<ZombieAbility>())a.enabled=false;var agent=z.GetComponent<NavMeshAgent>();if(agent!=null)agent.enabled=false;}
 foreach(var type in new[]{typeof(ShieldCompanion),typeof(CpcTactics),typeof(CpcTacticalMotor),typeof(CpcCrowdProtection)})foreach(var b in UnityEngine.Object.FindObjectsByType(type,FindObjectsInactive.Include))((Behaviour)b).enabled=false;
 p.Health.Configure(CombatTeam.Player,10000f);Place(p,new Vector3(-7f,.04f,0f));await Task.Delay(150);
 var root=GameObject.Find("Downtown City Street").transform;
 Check(root.Find("Road and Sidewalks").childCount==4,"Four continuous Downtown road/sidewalk tiles");
 Check(root.Find("South Buildings").childCount==6&&root.Find("North Buildings").childCount==6,"Six Downtown buildings on each side");
 Check(root.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported)),"All imported materials have supported shaders");
 Check(g.Anchors.Length==6&&g.Anchors.All(a=>a!=null&&a.gameObject.activeInHierarchy),"All six gameplay grapple anchors retained");
 Check(c.ShouldCull(p.View,false)&&!c.ShouldCull(p.View,true),"Third-person chooses camera-side south row");
 Check(!c.ShouldCull(s.ScopeCamera,false)&&!c.ShouldCull(s.ScopeCamera,true),"Scope camera is excluded from third-person culling");
 await Wait(()=>observedThird);Check(observedThird,"Actual render callback hides only south buildings");
 Check(c.NegativeSide.Concat(c.PositiveSide).All(r=>!r.forceRenderingOff),"Renderer states restored after rendering");
 var cameraPos=p.View.transform.position;p.View.transform.position=new Vector3(cameraPos.x,cameraPos.y,p.transform.position.z+2f);Check(c.ShouldCull(p.View,true)&&!c.ShouldCull(p.View,false),"Reverse camera side chooses north row");p.View.transform.position=cameraPos;
 for(int i=-11;i<=11;i+=2){RaycastHit hit;Check(GameObject.Find("Walkable Street").GetComponent<Collider>().Raycast(new Ray(new Vector3(i,2f,0f),Vector3.down),out hit,3f)&&Mathf.Abs(hit.point.y)<.01f,"Road ground collision x="+i);}
 Check(root.GetComponentsInChildren<BoxCollider>().Count(b=>b.name=="Building Collision")==12,"Twelve building colliders remain active when visuals are culled");
 var original=p.transform.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));await Task.Delay(300);InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Task.Delay(60);Check(p.transform.position.x>original.x+.05f,"Actual D input moves player on replacement road");
 var nextShot=typeof(SurvivalPlayer).GetField("_nextShot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);float shots=(float)nextShot.GetValue(p);InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.75f,Screen.height*.5f),buttons=1});await Task.Delay(150);InputSystem.QueueStateEvent(mouse,new MouseState());Check((float)nextShot.GetValue(p)>shots,"Rifle input fires after environment replacement");
 var anchor=g.Anchors.OrderBy(a=>(a.Position-p.transform.position).sqrMagnitude).First();Place(p,new Vector3(anchor.Position.x,.04f,anchor.Position.z-.32f));await Task.Delay(120);Check(g.TryBegin(anchor),"Grapple begins with retained anchor");await Wait(()=>s.Phase==GrappleSniperPhase.Ready);
 Check(!c.CullingActive&&!c.ShouldCull(p.View,false)&&!c.ShouldCull(p.View,true),"First-person removes building culling");await Wait(()=>observedFirst&&observedScope);Check(observedFirst&&observedScope,"Actual first-person and scope renders show both rows");
 ScreenCapture.CaptureScreenshot("Assets/Screenshots/Fix003/FirstPerson.png");
 InputSystem.QueueStateEvent(mouse,new MouseState{buttons=2});await Wait(()=>s.AimBlend>.95f);Check(s.AimBlend>.95f,"RMB scope aiming works");ScreenCapture.CaptureScreenshot("Assets/Screenshots/Fix003/Scoped.png");await Task.Delay(100);InputSystem.QueueStateEvent(mouse,new MouseState());
 s.EndSniper();await Wait(()=>!s.IsActive);observedThird=false;await Wait(()=>observedThird);Check(c.CullingActive&&observedThird,"Third-person return reapplies camera-side culling");
 c.enabled=false;Check(c.NegativeSide.Concat(c.PositiveSide).All(r=>!r.forceRenderingOff),"Disabling culling leaves no hidden renderers");c.enabled=true;
 Check(Time.frameCount>firstFrame+20,"Player loop advances throughout tests");return REPORT.ToString();
 }finally{RenderPipelineManager.beginCameraRendering-=observe;s.RestoreImmediately();g.Release();InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);oldMouse?.MakeCurrent();oldKeyboard?.MakeCurrent();File.WriteAllText("Tools/Fix003/Checks.txt",REPORT.ToString());}
 }
}
