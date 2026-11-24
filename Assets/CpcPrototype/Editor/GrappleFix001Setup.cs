using System;using System.Linq;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using UnityEditor;using UnityEditor.SceneManagement;
namespace CpcPrototype.Editor
{
    public static class GrappleFix001Setup
    {
        public static void Apply(SurvivalPlayer player)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
            var bulbs=player.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Lamp Bulb Platform").ToArray();
            foreach(var bulb in bulbs)
                foreach(var collider in bulb.GetComponentsInChildren<Collider>(true))Undo.DestroyObjectImmediate(collider);
            foreach(var anchor in player.Grapple.Anchors)
            {Undo.RecordObject(anchor,"Remove lamp platform reference");anchor.Platform=null;EditorUtility.SetDirty(anchor);}
            Undo.RecordObject(player,"Remove deleted one-way platforms");
            player.OneWayPlatforms=player.OneWayPlatforms.Where(c=>c!=null).ToArray();
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var pipelineObject=new SerializedObject(pipeline);
            var cameraObject=new SerializedObject(player.View.GetUniversalAdditionalCameraData());
            int index=cameraObject.FindProperty("m_RendererIndex").intValue;
            if(index<0)index=pipelineObject.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer=(ScriptableRendererData)pipelineObject.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue;
            var materials=renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().Where(f=>f.isActive&&f.passMaterial!=null&&f.passMaterial.HasProperty("_PixelHeight")).Select(f=>f.passMaterial).Distinct().ToArray();
            if(materials.Length==0)throw new InvalidOperationException("No active fullscreen pixel pass on the player camera.");
            var action=player.GrappleSniper;Undo.RecordObject(action,"Bind screen pixel pass");action.ScreenPixelMaterials=materials;
            action.ScreenPixelProperty="_PixelHeight";action.ScreenPixelMultiplier=2f;
            EditorUtility.SetDirty(action);EditorUtility.SetDirty(player);EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        }
        public static string Install()
        {
            var player=UnityEngine.Object.FindAnyObjectByType<SurvivalPlayer>();
            if(player==null||player.gameObject.scene.path!="Assets/Scenes/KayKit_DepthSurvival.unity")throw new InvalidOperationException("Open KayKit_DepthSurvival.");
            System.IO.Directory.CreateDirectory("Temp/Fix001");
            if(!System.IO.File.Exists("Temp/Fix001/Before.unity"))
                if(!EditorSceneManager.SaveScene(player.gameObject.scene,"Temp/Fix001/Before.unity",true))throw new Exception("Snapshot failed.");
            Apply(player);AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(player.gameObject.scene))throw new Exception("Scene save failed.");
            return "FIX-001 saved: bulb colliders removed, fullscreen pixel pass bound; previous unsaved scene copied to Temp/Fix001/Before.unity.";
        }
    }
}
