using System;using System.IO;using System.Linq;using System.Text;using UnityEngine;using UnityEditor;
public static class ApplyScopeLensGraph
{
 public static string Execute()
 {
  const string ROOT="Assets/sniper/ScopeLens/";
  var shader=AssetDatabase.LoadAssetAtPath<Shader>(ROOT+"ScopeLensGraph.shadergraph");var original=AssetDatabase.LoadAssetAtPath<Shader>(ROOT+"ScopeLens.shader");
  if(shader==null||ShaderUtil.ShaderHasError(shader)||original==null)throw new Exception("Shaders must import successfully before replacement.");
  var materials=AssetDatabase.FindAssets("t:Material",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null&&(m.shader==original||AssetDatabase.GetAssetPath(m)==ROOT+"ScopeLens.mat")).ToArray();
  var report=new StringBuilder();foreach(var material in materials)
  {
   var texture=material.GetTexture("_MainTex");var normal=material.GetTexture("_BumpMap");var tint=material.GetColor("_Tint");
   string[] props={"_Distortion","_Zoom","_EdgeSoftness","_Vignette"};float[] values=props.Select(material.GetFloat).ToArray();
   Undo.RecordObject(material,"Replace scope lens with Shader Graph");material.shader=shader;
   if(material.GetTexture("_MainTex")!=texture||material.GetTexture("_BumpMap")!=normal||material.GetColor("_Tint")!=tint)throw new Exception("Texture/tint changed");
   for(int i=0;i<props.Length;i++)if(!Mathf.Approximately(material.GetFloat(props[i]),values[i]))throw new Exception("Property changed: "+props[i]);
   EditorUtility.SetDirty(material);report.AppendLine("PASS replaced "+AssetDatabase.GetAssetPath(material)+"; both textures, four floats and tint preserved.");
  }
  if(materials.Length==0)throw new Exception("No scope lens material found.");
  AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(ROOT+"ScopeLens.mat",ImportAssetOptions.ForceSynchronousImport);
  var saved=AssetDatabase.LoadAssetAtPath<Material>(ROOT+"ScopeLens.mat");if(saved.shader!=shader)throw new Exception("Material replacement not persisted");
  report.AppendLine("PASS material reload uses "+shader.name+"; original ScopeLens.shader remains present.");
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/sniper/Prefabs/AWM_GrappleSniper.prefab");var lens=prefab.GetComponentInChildren<CpcPrototype.ScopeLens>(true);
  if(lens.GetComponent<Renderer>().sharedMaterial!=saved)throw new Exception("Prefab lens material does not match production material");
  report.AppendLine("PASS existing AWM prefab lens uses the replaced shared material.");
  foreach(var live in Resources.FindObjectsOfTypeAll<CpcPrototype.ScopeLens>().Where(x=>x.gameObject.scene.IsValid()&&!UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(x.gameObject.scene)))
   if(live.GetComponent<Renderer>().sharedMaterial.shader!=shader)throw new Exception("Scene lens uses another shader");
  report.AppendLine("PASS loaded scene lenses use the Shader Graph without changing scene transforms.");
  Directory.CreateDirectory("Tools/GrappleSniper/ScopeLensGraph");File.WriteAllText("Tools/GrappleSniper/ScopeLensGraph/MaterialChecks.txt",report.ToString());Selection.activeObject=shader;AssetDatabase.OpenAsset(shader);return report.ToString();
 }
}
