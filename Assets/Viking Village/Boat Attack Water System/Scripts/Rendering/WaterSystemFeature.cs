using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace WaterSystem
{
    public class WaterSystemFeature : ScriptableRendererFeature
    {

        #region Water Effects Pass

        private sealed class WaterFxPass : ScriptableRenderPass
        {
            private static readonly int WATER_FX_MAP = Shader.PropertyToID("_WaterFXMap");
            private static readonly ShaderTagId WATER_FX_TAG = new ShaderTagId("WaterFX");
            private sealed class PassData
            {
                public RendererListHandle Renderers;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();
                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.width = Mathf.Max(1, descriptor.width / 2);
                descriptor.height = Mathf.Max(1, descriptor.height / 2);
                descriptor.colorFormat = RenderTextureFormat.Default;
                var texture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_WaterFXMap", false);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Render Water FX", out var passData))
                {
                    var drawingSettings = RenderingUtils.CreateDrawingSettings(WATER_FX_TAG, renderingData,
                        cameraData, lightData, SortingCriteria.CommonTransparent);
                    var filteringSettings = new FilteringSettings(RenderQueueRange.transparent);
                    passData.Renderers = renderGraph.CreateRendererList(
                        new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings));
                    builder.UseRendererList(passData.Renderers);
                    builder.SetRenderAttachment(texture, 0, AccessFlags.Write);
                    // Publish the packed foam/normal/displacement map for the water shaders.
                    builder.SetGlobalTextureAfterPass(texture, WATER_FX_MAP);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0f, 0.5f, 0.5f, 0.5f), 1f, 0);
                        context.cmd.DrawRendererList(data.Renderers);
                    });
                }
            }
        }
        #endregion

        #region Caustics Pass

        private sealed class WaterCausticsPass : ScriptableRenderPass
        {
            private static readonly int MAIN_LIGHT_DIR = Shader.PropertyToID("_MainLightDir");
            public Material WaterCausticMaterial;
            private Mesh _mesh;

            private sealed class PassData
            {
                public Material Material;
                public Mesh Mesh;
                public Matrix4x4 Matrix;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var camera = frameData.Get<UniversalCameraData>().camera;
                if (camera.cameraType == CameraType.Preview || WaterCausticMaterial == null)
                    return;

                var resources = frameData.Get<UniversalResourceData>();
                WaterCausticMaterial.SetMatrix(MAIN_LIGHT_DIR, RenderSettings.sun != null
                    ? RenderSettings.sun.transform.localToWorldMatrix
                    : Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(-45f, 45f, 0f), Vector3.one));
                if (_mesh == null)
                    _mesh = GenerateCausticsMesh(1000f);
                var position = camera.transform.position;
                position.y = 0f;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Render Water Caustics", out var passData))
                {
                    passData.Material = WaterCausticMaterial;
                    passData.Mesh = _mesh;
                    passData.Matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        context.cmd.DrawMesh(data.Mesh, data.Matrix, data.Material, 0, 0));
                }
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_mesh);
                _mesh = null;
            }
        }
        #endregion

        WaterFxPass m_WaterFxPass;
        WaterCausticsPass m_CausticsPass;

        public WaterSystemSettings settings = new WaterSystemSettings();
        [HideInInspector][SerializeField] private Shader causticShader;
        [HideInInspector][SerializeField] private Texture2D causticTexture;

        private Material _causticMaterial;

        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int Size = Shader.PropertyToID("_Size");
        private static readonly int CausticTexture = Shader.PropertyToID("_CausticMap");

        public override void Create()
        {
            m_CausticsPass?.Dispose();
            // WaterFX Pass
            m_WaterFxPass = new WaterFxPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };

            // Caustic Pass
            m_CausticsPass = new WaterCausticsPass();

            causticShader = causticShader ? causticShader : Shader.Find("Hidden/BoatAttack/Caustics");
            if (causticShader == null) return;
            if (_causticMaterial)
            {
                DestroyImmediate(_causticMaterial);
            }
            _causticMaterial = CoreUtils.CreateEngineMaterial(causticShader);
            _causticMaterial.SetFloat("_BlendDistance", settings.causticBlendDistance);

            if (causticTexture == null)
            {
                Debug.Log("Caustics Texture missing, attempting to load.");
#if UNITY_EDITOR
                causticTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.verasl.water-system/Textures/WaterSurface_single.tif");
#endif
            }
            _causticMaterial.SetTexture(CausticTexture, causticTexture);

            switch (settings.debug)
            {
                case WaterSystemSettings.DebugMode.Caustics:
                    _causticMaterial.SetFloat(SrcBlend, 1f);
                    _causticMaterial.SetFloat(DstBlend, 0f);
                    _causticMaterial.EnableKeyword("_DEBUG");
                    m_CausticsPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                    break;
                case WaterSystemSettings.DebugMode.WaterEffects:
                    break;
                case WaterSystemSettings.DebugMode.Disabled:
                    // Caustics
                    _causticMaterial.SetFloat(SrcBlend, 2f);
                    _causticMaterial.SetFloat(DstBlend, 0f);
                    _causticMaterial.DisableKeyword("_DEBUG");
                    m_CausticsPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
                    // WaterEffects
                    break;
            }

            _causticMaterial.SetFloat(Size, settings.causticScale);
            m_CausticsPass.WaterCausticMaterial = _causticMaterial;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_WaterFxPass);
            renderer.EnqueuePass(m_CausticsPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_CausticsPass?.Dispose();
            CoreUtils.Destroy(_causticMaterial);
        }

        /// <summary>
        /// This function Generates a flat quad for use with the caustics pass.
        /// </summary>
        /// <param name="size">The length of the quad.</param>
        /// <returns></returns>
        private static Mesh GenerateCausticsMesh(float size)
        {
            var m = new Mesh();
            size *= 0.5f;

            var verts = new[]
            {
                new Vector3(-size, 0f, -size),
                new Vector3(size, 0f, -size),
                new Vector3(-size, 0f, size),
                new Vector3(size, 0f, size)
            };
            m.vertices = verts;

            var tris = new[]
            {
                0, 2, 1,
                2, 3, 1
            };
            m.triangles = tris;

            return m;
        }

        [System.Serializable]
        public class WaterSystemSettings
        {
            [Header("Caustics Settings")]
            [Range(0.1f, 1f)]
            public float causticScale = 0.25f;

            public float causticBlendDistance = 3f;

            [Header("Advanced Settings")] public DebugMode debug = DebugMode.Disabled;

            public enum DebugMode
            {
                Disabled,
                WaterEffects,
                Caustics
            }
        }
    }
}
