using UnityEngine;
using UnityEngine.Rendering;

namespace CpcPrototype
{
    [DefaultExecutionOrder(200)]
    public sealed class PixelGroundShadow : MonoBehaviour
    {
        public MeshRenderer Silhouette;
        public Light DirectionalLight;
        public Light[] Streetlights = System.Array.Empty<Light>();
        [Min(.1f)] public float MaxStreetlightShadowLength = 1.2f;
        public Material ShadowMaterial;
        public Transform ActorRoot;
        [Range(0f, 1f)] public float Opacity = .32f;

        private static readonly int TINT = Shader.PropertyToID("_Tint");
        private static readonly int MAIN_TEX = Shader.PropertyToID("_MainTex");
        private readonly Vector3[] _vertices = new Vector3[4];
        private readonly RaycastHit[] _groundHits = new RaycastHit[12];
        private Transform _sprite;
        private Mesh _mesh;
        private MeshRenderer _shadow;
        private MaterialPropertyBlock _properties;

        private void Awake()
        {
            _sprite = Silhouette.transform;
            var projection = new GameObject("Projected Pixel Shadow");
            projection.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "Projected Pixel Shadow" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            projection.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _shadow = projection.AddComponent<MeshRenderer>();
            _shadow.sharedMaterial = ShadowMaterial;
            _shadow.shadowCastingMode = ShadowCastingMode.Off;
            _shadow.receiveShadows = false;
            _properties = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            Vector3 direction = DirectionalLight.transform.forward;
            Light lamp = FindStreetlight(out float influence);
            _shadow.enabled = lamp != null || direction.y < -.1f && DirectionalLight.isActiveAndEnabled &&
                DirectionalLight.shadows != LightShadows.None && Silhouette.enabled;
            if (!_shadow.enabled) return;
            float floor = ActorRoot != null ? FindGround() : _sprite.position.y - _sprite.lossyScale.y * .5f;
            // Baked sprites have transparent padding below their physical base.
            if (ActorRoot == null) floor = Mathf.Max(0f, floor);
            for (int i = 0; i < _vertices.Length; i++)
            {
                float x = i == 0 || i == 3 ? -.5f : .5f;
                float y = i < 2 ? -.5f : .5f;
                Vector3 point = _sprite.TransformPoint(new Vector3(x, y, 0f));
                Vector3 projected = point + direction * Mathf.Max(0f, (floor - point.y) / Mathf.Min(-.1f, direction.y));
                if (lamp != null)
                {
                    Vector3 ray = (point - lamp.transform.position).normalized;
                    Vector3 shift = ray * Mathf.Max(0f, (point.y - floor) / Mathf.Max(.15f, -ray.y));
                    projected = Vector3.Lerp(projected, point + Vector3.ClampMagnitude(shift, MaxStreetlightShadowLength), influence);
                }
                projected.y = floor + .0018f;
                _vertices[i] = _shadow.transform.InverseTransformPoint(projected);
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
            _properties.SetTexture(MAIN_TEX, Silhouette.sharedMaterial.mainTexture);
            _properties.SetColor(TINT, new Color(.012f, .019f, .04f, Opacity));
            _shadow.SetPropertyBlock(_properties);
        }

        private Light FindStreetlight(out float influence)
        {
            influence = 0f;
            Light nearest = null;
            foreach (Light lamp in Streetlights)
            {
                if (lamp == null || !lamp.isActiveAndEnabled || transform.IsChildOf(lamp.transform.parent.parent))
                    continue;
                float strength = 1f - Vector3.Distance(lamp.transform.position, _sprite.position) / lamp.range;
                if (strength <= influence) continue;
                influence = strength;
                nearest = lamp;
            }
            influence = Mathf.SmoothStep(0f, 1f, influence * 3f);
            return nearest;
        }

        private float FindGround()
        {
            Vector3 position = ActorRoot.position;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * .14f, Vector3.down, _groundHits, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            float height = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.distance >= closest || hit.normal.y < .5f || hit.collider.GetComponentInParent<Health>() != null)
                    continue;
                closest = hit.distance;
                height = hit.point.y;
            }
            return height;
        }

        private void OnDisable()
        {
            if (_shadow != null) _shadow.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_shadow != null) Destroy(_shadow.gameObject);
        }
    }
}

