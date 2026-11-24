using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.SceneManagement;

public static class VerifyScopeLensGraph
{
 private const string ROOT="Assets/sniper/ScopeLens/";
 private static Color32[] Render(Camera camera,Renderer renderer,Material material,int size,string file)
 {
  var rt=new RenderTexture(size,size,24,RenderTextureFormat.ARGB32){antiAliasing=1};rt.Create();var old=RenderTexture.active;
  var read=new Texture2D(size,size,TextureFormat.RGBA32,false);
  try{renderer.sharedMaterial=material;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,size,size),0,0);read.Apply();if(file!=null)File.WriteAllBytes(file,read.EncodeToPNG());return read.GetPixels32();}
  finally{camera.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(read);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
 }
 public static string Execute()
 {
  Shader original=AssetDatabase.LoadAssetAtPath<Shader>(ROOT+"ScopeLens.shader");Shader graph=AssetDatabase.LoadAssetAtPath<Shader>(ROOT+"ScopeLensGraph.shadergraph");
  if(original==null||graph==null||ShaderUtil.ShaderHasError(graph))throw new Exception("Shader import or compilation failed");
  var report=new StringBuilder();var output="Tools/GrappleSniper/ScopeLensGraph";Directory.CreateDirectory(output);report.AppendLine("Unity "+Application.unityVersion+" | original vs standard-node Shader Graph | RGB pixel comparison");
  var source=AssetDatabase.LoadAssetAtPath<Material>(ROOT+"ScopeLens.mat");
  var oldMat=new Material(original);oldMat.CopyPropertiesFromMaterial(source);var newMat=new Material(graph);newMat.CopyPropertiesFromMaterial(source);
  var checker=new Texture2D(128,128,TextureFormat.RGBA32,false);checker.wrapMode=TextureWrapMode.Clamp;checker.filterMode=FilterMode.Bilinear;
  var pixels=new Color[128*128];for(int y=0;y<128;y++)for(int x=0;x<128;x++)pixels[y*128+x]=new Color(x/127f,y/127f,((x/8+y/8)%2)*.7f+.15f,1f);checker.SetPixels(pixels);checker.Apply();
  Scene preview=EditorSceneManager.NewPreviewScene();var cameraObject=new GameObject("Lens Comparison Camera");SceneManager.MoveGameObjectToScene(cameraObject,preview);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=preview;camera.orthographic=true;camera.orthographicSize=.58f;camera.nearClipPlane=.01f;camera.farClipPlane=10f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f,1f);camera.allowHDR=false;camera.allowMSAA=false;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
  var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);SceneManager.MoveGameObjectToScene(quad,preview);UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());var renderer=quad.GetComponent<Renderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
  var names=new[]{"Default","NoDistortion","PositiveCurvature","NegativeCurvature","MaxZoom","SoftEdge","TransparentTint","ReverseFace","SmallAntialias"};
  try{
   for(int i=0;i<names.Length;i++)
   {
    oldMat.CopyPropertiesFromMaterial(source);newMat.CopyPropertiesFromMaterial(source);oldMat.SetTexture("_MainTex",checker);newMat.SetTexture("_MainTex",checker);
    void Float(string prop,float value){oldMat.SetFloat(prop,value);newMat.SetFloat(prop,value);}
    if(i==1)Float("_Distortion",0f);if(i==2)Float("_Distortion",.3f);if(i==3)Float("_Distortion",-.3f);if(i==4)Float("_Zoom",4f);if(i==5)Float("_EdgeSoftness",.15f);if(i==6){oldMat.SetColor("_Tint",new Color(.5f,.8f,.6f,.4f));newMat.SetColor("_Tint",new Color(.5f,.8f,.6f,.4f));}
    camera.transform.SetPositionAndRotation(new Vector3(0,0,i==7?2f:-2f),Quaternion.Euler(0,i==7?180:0,0));int size=i==8?64:256;
    var a=Render(camera,renderer,oldMat,size,output+"/"+names[i]+"_Original.png");var b=Render(camera,renderer,newMat,size,output+"/"+names[i]+"_Graph.png");
    if(a.Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Count()<100)throw new Exception("Reference render is blank or invalid");
    double total=0;int max=0,changed=0;for(int p=0;p<a.Length;p++){int dr=Math.Abs(a[p].r-b[p].r),dg=Math.Abs(a[p].g-b[p].g),db=Math.Abs(a[p].b-b[p].b);total+=dr+dg+db;max=Math.Max(max,Math.Max(dr,Math.Max(dg,db)));if(dr>2||dg>2||db>2)changed++;}
    double mean=total/(a.Length*3d);bool pass=mean<=.6&&changed<=a.Length*.01;
    report.AppendLine($"{(pass?"PASS":"FAIL")} {names[i]}: mean RGB error {mean:F5}/255, max {max}/255, pixels over 2/255 {changed}/{a.Length}");
    File.WriteAllText(output+"/RenderComparison.txt",report.ToString());if(!pass)throw new Exception("Render mismatch: "+names[i]);
   }
   foreach(string property in new[]{"_MainTex","_BumpMap","_Distortion","_Zoom","_EdgeSoftness","_Vignette","_Tint"})if(!newMat.HasProperty(property))throw new Exception("Missing original property "+property);
   report.AppendLine("PASS All seven original property reference names retained; original shader and production material were not changed during comparison.");File.WriteAllText(output+"/RenderComparison.txt",report.ToString());return report.ToString();
  }finally{EditorSceneManager.ClosePreviewScene(preview);UnityEngine.Object.DestroyImmediate(oldMat);UnityEngine.Object.DestroyImmediate(newMat);UnityEngine.Object.DestroyImmediate(checker);}
 }
}
