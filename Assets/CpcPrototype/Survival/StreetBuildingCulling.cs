using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CpcPrototype
{
    /// <summary>Hide only the camera-side facade during the third-person camera render.</summary>
    [DisallowMultipleComponent]
    public sealed class StreetBuildingCulling : MonoBehaviour
    {
        public SurvivalPlayer Player;
        public GrappleSniperAction Sniper;
        public Renderer[] NegativeSide = Array.Empty<Renderer>();
        public Renderer[] PositiveSide = Array.Empty<Renderer>();
        [Min(0f)] public float SideDeadZone = .1f;
        private bool[] _negativeStates, _positiveStates;
        private Transform _playerTransform;
        private Camera _renderingCamera;
        public bool CullingActive => isActiveAndEnabled && Player != null &&
            (Sniper == null || !Sniper.ControlsView);

        private void Awake()
        {
            if (Player != null) _playerTransform = Player.transform;
            _negativeStates = new bool[NegativeSide.Length];
            _positiveStates = new bool[PositiveSide.Length];
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        public bool ShouldCull(Camera camera, bool positiveSide)
        {
            if (!CullingActive || camera != Player.View || _playerTransform == null) return false;
            float offset = camera.transform.position.z - _playerTransform.position.z;
            return positiveSide ? offset > SideDeadZone : offset < -SideDeadZone;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!ShouldCull(camera, false) && !ShouldCull(camera, true)) return;
            _renderingCamera = camera;
            Apply(NegativeSide, _negativeStates, ShouldCull(camera, false));
            Apply(PositiveSide, _positiveStates, ShouldCull(camera, true));
        }

        private static void Apply(Renderer[] renderers, bool[] states, bool hidden)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                states[i] = renderer.forceRenderingOff;
                if (hidden) renderer.forceRenderingOff = true;
            }
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _renderingCamera) Restore();
        }

        private void Restore()
        {
            if (_renderingCamera == null) return;
            Restore(NegativeSide, _negativeStates);
            Restore(PositiveSide, _positiveStates);
            _renderingCamera = null;
        }

        private static void Restore(Renderer[] renderers, bool[] states)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = states[i];
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            Restore();
        }
    }
}
