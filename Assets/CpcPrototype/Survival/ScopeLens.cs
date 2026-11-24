using UnityEngine;
using UnityEngine.Rendering;

namespace CpcPrototype
{
    /// <summary>Prevents the scope camera from capturing its own lens surface.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ScopeLens : MonoBehaviour
    {
        [SerializeField] private Camera _captureCamera;
        [SerializeField] private MeshRenderer _lensRenderer;
        [SerializeField] private Renderer[] _excludedRenderers;
        private bool[] _rendererStates;
        private bool _isCapturing;

        public void Configure(Camera captureCamera, MeshRenderer lensRenderer)
        {
            _captureCamera = captureCamera;
            _lensRenderer = lensRenderer;
            _excludedRenderers = captureCamera.transform.parent.GetComponentsInChildren<Renderer>(true);
            _rendererStates = new bool[_excludedRenderers.Length];
        }

        private void OnEnable()
        {
            if (_excludedRenderers != null)
                _rendererStates = new bool[_excludedRenderers.Length];
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RestoreRenderer();
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _captureCamera || _lensRenderer == null || _isCapturing)
                return;

            _isCapturing = true;
            for (int i = 0; i < _excludedRenderers.Length; i++)
            {
                Renderer excluded = _excludedRenderers[i];
                if (excluded == null) continue;
                _rendererStates[i] = excluded.enabled;
                excluded.enabled = false;
            }
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _captureCamera)
                RestoreRenderer();
        }

        private void RestoreRenderer()
        {
            if (_isCapturing && _excludedRenderers != null)
            {
                for (int i = 0; i < _excludedRenderers.Length; i++)
                    if (_excludedRenderers[i] != null)
                        _excludedRenderers[i].enabled = _rendererStates[i];
            }
            _isCapturing = false;
        }
    }
}
