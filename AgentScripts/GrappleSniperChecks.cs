using System;using System.IO;using System.Linq;using System.Text;using System.Threading.Tasks;
using UnityEngine;using UnityEngine.AI;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;
using CpcPrototype;
public static class GrappleSniperChecks
{
 private static readonly StringBuilder Report=new StringBuilder();
 private static Mouse _mouse; private static Keyboard _keyboard; private static Mouse _previousMouse; private static Keyboard _previousKeyboard;
 private const string REPORT_PATH = "Tools/GrappleSniper/Bug010/Checks.txt";
 private static void Check(bool condition,string label){Report.AppendLine((condition?"PASS ":"FAIL ")+label);File.WriteAllText(REPORT_PATH,Report.ToString());if(!condition)throw new Exception(label);}
 private static void Input(Vector2 pos,bool right=false,bool left=false){_mouse.MakeCurrent();InputSystem.QueueStateEvent(_mouse,new MouseState{position=pos,buttons=(ushort)((left?1:0)|(right?2:0))});}
 private static async Task Until(Func<bool> predicate,int timeout=5000){float end=Time.realtimeSinceStartup+timeout/1000f;while(!predicate()&&Time.realtimeSinceStartup<end)await Task.Delay(20);Check(predicate(),"Wait condition within "+timeout+"ms");}
 private static void Place(SurvivalPlayer p,Vector3 position){var c=p.GetComponent<CharacterController>();c.enabled=false;p.transform.position=position;c.enabled=true;p.ResetMotion();Physics.SyncTransforms();}
 public static async Task<string> Execute()
 {
 Directory.CreateDirectory(Path.GetDirectoryName(REPORT_PATH));Report.Clear();
 Check(Application.isPlaying,"Play mode");var p=UnityEngine.Object.FindAnyObjectByType<SurvivalPlayer>();var g=p.Grapple;var s=p.GrappleSniper;var controller=p.GetComponent<CharacterController>();
 float scale=Time.timeScale,fixedTime=Time.fixedDeltaTime;int initialFrame=Time.frameCount;var rifleParent=p.Rifle.parent;var sniperParent=s.Sniper.parent;
 float originalMaxDistance = g.MaxHookDistance;
 _previousMouse=Mouse.current;_previousKeyboard=Keyboard.current;
 _mouse=InputSystem.AddDevice<Mouse>("GrappleCheckMouse");_keyboard=InputSystem.AddDevice<Keyboard>("GrappleCheckKeyboard");_mouse.MakeCurrent();_keyboard.MakeCurrent();
 try
 {
 foreach(var z in UnityEngine.Object.FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include)) {z.enabled=false;foreach(var a in z.GetComponents<ZombieAbility>())a.enabled=false;var agent=z.GetComponent<NavMeshAgent>();if(agent!=null)agent.enabled=false;}
 foreach(var type in new[]{typeof(ShieldCompanion),typeof(CpcTactics),typeof(CpcTacticalMotor),typeof(CpcCrowdProtection)})foreach(var b in UnityEngine.Object.FindObjectsByType(type,FindObjectsInactive.Include))((Behaviour)b).enabled=false;
 p.Health.Configure(CombatTeam.Player,10000f);
 var lamp=g.Anchors.OrderBy(a=>(a.Position-p.transform.position).sqrMagnitude).First();
 Input(new Vector2(25f,25f));await Task.Delay(120);Input(new Vector2(25f,25f),true);await Task.Delay(100);Input(new Vector2(25f,25f));
 Check(g.Phase==GrapplePhase.Idle,"Right-click away from a bulb does not grapple");Check(!g.TryBegin(null),"Unmarked/null target refused");
 Place(p,new Vector3(lamp.Position.x,.04f,lamp.Position.z-.32f));await Task.Delay(150);
 var screen=(Vector2)p.View.WorldToScreenPoint(lamp.Position);
 float hookDistance = Vector3.Distance(p.transform.position, lamp.Position);
 g.MaxHookDistance = hookDistance - .05f;
 Input(screen);await Task.Delay(75);
 Check(g.HoveredAnchor==null && !g.TryGetAnchor(screen,out _) && !g.TryBegin(lamp),"Lowering Max Hook Distance excludes the bulb from aiming and firing");
 Input(screen,true);await Task.Delay(60);Input(screen);await Task.Delay(40);
 Check(g.Phase==GrapplePhase.Idle && !g.Rope.enabled,"Right-click outside the configured range does not fire the rope");
 g.MaxHookDistance = hookDistance + .05f;
 Input(screen);await Task.Delay(100);
 Check(g.HoveredAnchor==lamp,"Bulb pointer selects green ready anchor");
 Vector3 start = default;
 void RecordStart() => start = p.transform.position;
 g.Started += RecordStart;
 Input(screen,true);await Task.Delay(60);Input(screen);
 g.Started -= RecordStart;
 Check(g.Phase!=GrapplePhase.Idle,"Actual right-click starts rope extension");
 g.MaxHookDistance = originalMaxDistance;
 await Until(()=>g.Phase!=GrapplePhase.Extending,2000);Check(g.Phase==GrapplePhase.Swinging,"Steep grounded hook starts a swing");
 float swingDeadline = Time.unscaledTime + 3f;
 float maximumRadiusError = 0f;
 float previousRadius = g.InitialLength;
 bool lengthDecreased = true;
 int swingSamples = 0;
 while (g.Phase == GrapplePhase.Swinging && Time.unscaledTime < swingDeadline)
 {
     float radius = Vector3.Distance(p.transform.position, lamp.Position);
     maximumRadiusError = Mathf.Max(maximumRadiusError, Mathf.Abs(radius - g.CurrentLength));
     lengthDecreased &= radius <= previousRadius + .002f;
     previousRadius = radius;
     swingSamples++;
     await Task.Delay(20);
 }
 Check(g.Phase==GrapplePhase.Holding,"Grounded swing reaches its destination");
 Check(swingSamples>10 && lengthDecreased && maximumRadiusError<.015f,"Swing shortens the radius across fixed-step motion");
 Check(Vector3.Distance(p.transform.position,lamp.Position+.5f*(lamp.Position-start))<.015f,"Swing ends at half the starting radius on the opposite side");
 Check(s.IsActive&&Time.timeScale<scale,"Arrival automatically starts sniper and slow motion");float sniperStarted=Time.unscaledTime;
 await Task.Delay(80);Check(s.ViewBlend>0f&&s.ViewBlend<1f,"First-person camera interpolates across frames");
 await Until(()=>s.Phase==GrappleSniperPhase.Ready,2000);await Task.Delay(80);
 Check(p.Rifle.parent==s.BackSocket&&s.Sniper.parent==s.HandSocket,"Rifle on back and sniper equipped");
 Check(!s.PlayerPixels.Quad.enabled,"Own billboard hidden in first person");
 foreach(var actor in s.PixelActors.Where(a=>a!=null&&a.enabled))Check(actor.CurrentResolution==Mathf.Min(actor.FocusResolutionLimit,Mathf.RoundToInt(actor.MaxResolution*s.PixelResolutionMultiplier)),"Distance-independent doubled resolution: "+actor.name);
 Input(screen,true);await Until(()=>s.AimBlend>.99f,1000);Check(s.AimBlend>.99f,"Held RMB scopes using unscaled aim time");
 Vector3 eye=p.View.WorldToViewportPoint(s.ScopeEyePoint.position);Check(Mathf.Abs(eye.x-.5f)<.02f&&Mathf.Abs(eye.y-.5f)<.02f,"scopeEyepoint centered in camera");
 Input(screen);await Until(()=>s.AimBlend<.01f,1000);Check(s.AimBlend<.01f,"Release RMB exits zoom");
 var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="Sniper Damage Check";target.transform.position=p.View.transform.position+p.View.transform.forward*.8f;target.transform.localScale=Vector3.one*.12f;
 var hp=target.AddComponent<Health>();hp.Configure(CombatTeam.Monster,500f);Physics.SyncTransforms();
 int shots=s.ShotsFired;Input(screen,false,true);await Task.Delay(50);Input(screen);await Task.Delay(30);
 Check(s.ShotsFired==shots+1,"LMB fires one sniper shot");Check(Mathf.Abs(hp.Current-(500f-s.BodyDamage))<.01f,"Sniper configured body damage applied through real raycast");Check(s.RecoilAmount>0f,"Shot creates recoil");Check(!s.TryFire(),"Sniper cooldown prevents immediate duplicate shot");
 Check(p.GetComponentsInChildren<LineRenderer>().Any(l=>l.name=="Bullet Tracer"&&l.enabled),"Existing rifle tracer and bullet effect reused");UnityEngine.Object.Destroy(target);
 await Until(()=>s.Phase==GrappleSniperPhase.Returning,6500);Check(Time.unscaledTime-sniperStarted<5.4f,"Slow motion expires after approximately 5 real seconds");Check(Mathf.Approximately(Time.timeScale,scale),"Time scale restored at timeout");await Until(()=>!s.IsActive,2000);
 Check(Mathf.Approximately(Time.fixedDeltaTime,fixedTime),"Fixed timestep restored");Check(p.Rifle.parent==rifleParent&&s.Sniper.parent==sniperParent,"Weapons restored after smooth third-person return");Check(s.PlayerPixels.Quad.enabled,"Player billboard restored");Check(s.PixelActors.All(a=>a==null||a.FocusResolutionBlend==0f),"Pixel resolution override cleared");Check(g.Phase==GrapplePhase.Idle,"Hold released after sniper return");
 s.enabled=false;
 Place(p,new Vector3(lamp.Position.x-1.4f,.04f,lamp.Position.z));await Task.Delay(120);
 Vector3 shallowStart=p.transform.position;
 Check(g.TryBegin(lamp),"Low-angle hook accepted");
 await Until(()=>g.Phase!=GrapplePhase.Extending,2000);
 Check(g.Phase==GrapplePhase.Swinging,"Low-angle hook follows a circular path");
 await Until(()=>!g.IsTravelling,2500);
 Check(g.Phase==GrapplePhase.Holding && Vector3.Distance(p.transform.position,lamp.Position+.5f*(lamp.Position-shallowStart))<.015f,"Low-angle swing reaches the opposite side at half radius");
 g.Release();

 Vector3 airStart = lamp.Position + new Vector3(-.3f, -.3f, 0f);
 Vector3 airRadial = airStart - lamp.Position;
 // Halfway through rotation, the cubic retraction leaves 9/16 of the starting radius.
 Vector3 airMidpoint = lamp.Position + Vector3.ProjectOnPlane(Vector3.down, airRadial).normalized * (.5625f * airRadial.magnitude);
 Place(p, airStart);
 Check(g.TryBegin(lamp),"Airborne hook accepted");
 float closestToMidpoint = float.PositiveInfinity;
 float maximumSidewaysDrift = 0f;
 Vector3 swingPlaneNormal = Vector3.Cross(Vector3.up, airRadial).normalized;
 float airDeadline = Time.unscaledTime + 3f;
 while (g.IsTravelling && Time.unscaledTime < airDeadline)
 {
     closestToMidpoint = Mathf.Min(closestToMidpoint, Vector3.Distance(p.transform.position, airMidpoint));
     maximumSidewaysDrift = Mathf.Max(maximumSidewaysDrift, Mathf.Abs(Vector3.Dot(p.transform.position - lamp.Position, swingPlaneNormal)));
     await Task.Delay(15);
 }
 Check(g.Phase==GrapplePhase.Holding && closestToMidpoint<.04f,"Unobstructed airborne swing passes under the bulb");
 Check(maximumSidewaysDrift<.005f,"With floor clearance the swing stays on the player-to-bulb line in plan view");
 g.Release();

 Place(p, airStart);
 var arcObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
 try
 {
     arcObstacle.transform.position = airMidpoint + (controller.bounds.center - p.transform.position);
     arcObstacle.transform.localScale = Vector3.one * .14f;
     Physics.SyncTransforms();
     Check(g.TryBegin(lamp),"A mid-arc obstacle does not block the initial rope shot");
     await Until(()=>!g.IsTravelling,2500);
     Check(g.Phase==GrapplePhase.Idle && !g.Rope.enabled,"Collision on the arc cancels the grapple and hides the rope");
 }
 finally { UnityEngine.Object.Destroy(arcObstacle); }
 s.enabled=true;
 Place(p,new Vector3(lamp.Position.x,.04f,lamp.Position.z-.32f));await Task.Delay(120);
 var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=Vector3.Lerp(controller.bounds.center,lamp.Position,.5f);wall.transform.localScale=new Vector3(.25f,.1f,.08f);Physics.SyncTransforms();Check(!g.CanHook(lamp),"Blocked hook is rejected");UnityEngine.Object.Destroy(wall);await Task.Delay(100);
 Check(g.TryBegin(lamp),"Hook available after obstacle removal");await Until(()=>s.IsActive,4000);s.enabled=false;await Task.Delay(80);Check(Mathf.Approximately(Time.timeScale,scale)&&g.Phase==GrapplePhase.Idle,"Disabling sniper restores time and releases grapple");s.enabled=true;
 Check(Time.frameCount>initialFrame+30,"Player loop advanced during all checks");Report.AppendLine("COMPLETE: real scene controllers, InputSystem, collision, camera, raycast damage and time restoration. Synthetic input; no microphone/model validation.");return Report.ToString();
 }
 finally
 {
 if(p.Chat!=null&&p.IsChatOpen)p.Chat.Close();s.RestoreImmediately();g.Release();
 g.MaxHookDistance = originalMaxDistance;
 if(_mouse!=null)InputSystem.RemoveDevice(_mouse);if(_keyboard!=null)InputSystem.RemoveDevice(_keyboard);_previousMouse?.MakeCurrent();_previousKeyboard?.MakeCurrent();
 File.WriteAllText(REPORT_PATH,Report.ToString());
 }
 }
}
