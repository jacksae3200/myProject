using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VerifyScopeLens
{
    public static void Execute()
    {
        var lens = Resources.FindObjectsOfTypeAll<CpcPrototype.ScopeLens>()
            .Single(l => l.gameObject.scene.IsValid());
        var renderer = lens.GetComponent<MeshRenderer>();
        var material = renderer.sharedMaterial;
        var camera = lens.transform.parent.parent.GetComponentsInChildren<Camera>(true).Single(c => c.name == "scopeViewPort");
        var capture = camera.targetTexture;
        if (capture == null || material.GetTexture("_MainTex") != capture)
            throw new InvalidOperationException("Capture texture binding is invalid.");
        if (ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Lens shader has compile errors.");
        if (material.GetTexture("_BumpMap") == null)
            throw new InvalidOperationException("Circular normal map is missing.");
        Directory.CreateDirectory("Screenshots");
        camera.Render();
        Save(capture, "Screenshots/ScopeCapture.png");
        var observerObject = new GameObject("Scope Verification Camera") { hideFlags = HideFlags.HideAndDontSave };
        var target = new RenderTexture(768, 768, 24);
        try
        {
            var observer = observerObject.AddComponent<Camera>();
            observer.enabled = false;
            observer.transform.position = lens.transform.position + lens.transform.forward * 0.2f;
            observer.transform.rotation = Quaternion.LookRotation(-lens.transform.forward, lens.transform.up);
            observer.orthographic = true;
            observer.orthographicSize = 0.06f;
            observer.nearClipPlane = 0.01f;
            observer.farClipPlane = 0.3f;
            observer.clearFlags = CameraClearFlags.SolidColor;
            observer.backgroundColor = new Color(0.12f, 0.035f, 0.12f);
            observer.targetTexture = target;
            observer.Render();
            Save(target, "Screenshots/ScopeLensVerification.png");
            if (!renderer.enabled) throw new InvalidOperationException("Lens renderer was not restored after capture.");
            Debug.Log("SCOPE_VERIFIED: shader compiled, camera texture and normal map bound, renderer restored; screenshots saved.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(observerObject);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Save(RenderTexture source, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = source;
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
