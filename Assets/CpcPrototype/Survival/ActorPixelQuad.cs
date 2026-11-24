using UnityEngine;
using UnityEngine.Serialization;

namespace CpcPrototype
{
    public sealed class ActorPixelQuad : MonoBehaviour
    {
        public Camera WorldView;
        [Range(0f, 1f)] public float FocusResolutionBlend;
        [Min(1f)] public float FocusResolutionMultiplier = 2f;
        [Min(16)] public int FocusResolutionLimit = 2048;
        public int CurrentResolution => Capture != null && Capture.targetTexture != null ? Capture.targetTexture.width : 0;
        private readonly RenderTexture[] _focusTextures = new RenderTexture[9];
        public Camera Capture;
        public MeshRenderer Quad;
        [Range(1, 128)] public int MinResolution = 32;
        [FormerlySerializedAs("Resolution"), Range(1, 512)] public int MaxResolution = 96;
        [Min(0)] public float NearDistance = 5f;
        [Min(.01f)] public float MaxDistance = 10f;
        [Min(.1f)] public float FrameSize = .64f;
        [Min(0)] public float CenterHeight = .19f;
        [Min(.2f)] public float CaptureDistance = 1f;
        private const int RESOLUTION_TIERS = 9;
        private const float FAR_SCREEN_PIXEL_SIZE = 3f;
        private readonly RenderTexture[] _textures = new RenderTexture[RESOLUTION_TIERS];
        private Material _material;
        private Material _template;
        private Transform _root;
        private Transform _quadTransform;

        private void Awake()
        {
            _root = transform; if (WorldView == null) { SurvivalPlayer player = FindFirstObjectByType<SurvivalPlayer>(); WorldView = player != null ? player.View : Camera.main; }
            _quadTransform = Quad.transform;
            _template = Quad.sharedMaterial;
        }

        private void OnEnable()
        {
            _material = new Material(_template);
            Quad.sharedMaterial = _material;
            LateUpdate();
            Capture.enabled = true;
        }

        private void UpdateResolution(float distance, float screenHeight)
        {
            if (FocusResolutionBlend > 0f)
            {
                int focusTier = Mathf.Clamp(Mathf.RoundToInt(FocusResolutionBlend * 8f), 0, 8);
                RenderTexture focus = _focusTextures[focusTier];
                if (focus != null)
                {
                    Capture.targetTexture = focus;
                    _material.mainTexture = focus;
                    return;
                }
            }
            float distanceBlend = Mathf.InverseLerp(NearDistance, Mathf.Max(NearDistance + .01f, MaxDistance), distance);
            int tier = Mathf.RoundToInt((1f - distanceBlend) * (RESOLUTION_TIERS - 1));
            int maximum = Mathf.Clamp(MaxResolution, 16, 512);
            int minimum = Mathf.Clamp(MinResolution, 16, maximum);
            int resolution = Mathf.RoundToInt(Mathf.Lerp(minimum, maximum, (float)tier / (RESOLUTION_TIERS - 1)));
            if (screenHeight > 0f && distanceBlend > 0f)
            {
                // Compensate for perspective shrink so distant texels remain visible on screen.
                float pixelSize = Mathf.Lerp(1f, FAR_SCREEN_PIXEL_SIZE, distanceBlend);
                int screenLimit = Mathf.FloorToInt(screenHeight / pixelSize);
                int quantizedLimit = screenLimit >= 8 ? screenLimit / 4 * 4 : screenLimit;
                resolution = Mathf.Min(resolution, Mathf.Max(1, quantizedLimit));
            }
            RenderTexture texture = _textures[tier];
            if (texture == null || texture.width != resolution)
            {
                if (texture != null) Destroy(texture);
                texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
                {
                    name = name + " Pixel Capture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false
                };
                texture.Create();
                _textures[tier] = texture;
            }
            if (Capture.targetTexture == texture) return;
            Capture.targetTexture = texture;
            _material.mainTexture = texture;
        }

        // Allocate transition textures once per configuration, outside the per-frame resolution path.
        public void PrepareFocusResolution()
        {
            for (int i = 0; i < _focusTextures.Length; i++)
            {
                int size = Mathf.Clamp(Mathf.RoundToInt(MaxResolution * Mathf.Lerp(1f,
                    Mathf.Max(1f, FocusResolutionMultiplier), i / 8f)), 16, FocusResolutionLimit);
                if (_focusTextures[i] != null && _focusTextures[i].width == size) continue;
                if (_focusTextures[i] != null) { _focusTextures[i].Release(); Destroy(_focusTextures[i]); }
                var texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
                {
                    name = name + " Focus Pixel Capture", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = false
                };
                texture.Create();
                _focusTextures[i] = texture;
            }
        }

        private void LateUpdate()
        {
            if (WorldView == null) return; Quaternion rotation = WorldView.transform.rotation;
            Vector3 center = _root.position + Vector3.up * CenterHeight;
            Vector3 halfFrame = rotation * Vector3.up * (FrameSize * .5f);
            Vector3 top = WorldView.WorldToScreenPoint(center + halfFrame);
            Vector3 bottom = WorldView.WorldToScreenPoint(center - halfFrame);
            float screenHeight = top.z > 0f ? Mathf.Abs(top.y - bottom.y) : 0f;
            UpdateResolution(Vector3.Distance(WorldView.transform.position, center), screenHeight);
            Vector3 cameraPosition = center - rotation * Vector3.forward * CaptureDistance;
            Capture.transform.SetPositionAndRotation(cameraPosition, rotation);
            Capture.orthographicSize = FrameSize * .5f;
            _quadTransform.SetPositionAndRotation(center, rotation);
            _quadTransform.localScale = Vector3.one * (FrameSize / _root.lossyScale.x);
        }

        private void OnDisable()
        {
            FocusResolutionBlend = 0f;
            for (int i = 0; i < _focusTextures.Length; i++)
            {
                if (_focusTextures[i] == null) continue;
                _focusTextures[i].Release();
                Destroy(_focusTextures[i]);
                _focusTextures[i] = null;
            }
            Capture.enabled = false;
            Capture.targetTexture = null;
            Quad.sharedMaterial = _template;
            if (_material != null) Destroy(_material);
            for (int i = 0; i < _textures.Length; i++)
            {
                if (_textures[i] == null) continue;
                _textures[i].Release();
                Destroy(_textures[i]);
                _textures[i] = null;
            }
        }
    }
}

