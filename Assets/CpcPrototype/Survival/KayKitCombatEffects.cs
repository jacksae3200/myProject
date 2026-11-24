using UnityEngine;
using UnityEngine.Rendering;

namespace CpcPrototype
{
    public sealed class KayKitCombatEffects : MonoBehaviour
    {
        private const int MARK_CAPACITY = 10;
        private const int SPLASH_CAPACITY = 8;
        private const int FOOTPRINT_CAPACITY = 48;
        private static readonly int TINT = Shader.PropertyToID("_Tint");

        public SurvivalPlayer Player;
        public Material MuzzleMaterial;
        public Material SplashMaterial;
        public Material BulletHoleMaterial;
        public Material BloodMaterial;
        public Material FootprintMaterial;
        public AudioClip Gunfire;
        [Range(0f, 1f)] public float GunVolume = .8f;
        [Range(0f, .3f)] public float GunPitchVariation = .08f;
        public Color FootprintColor = new Color(.67f, .006f, .012f);
        [Min(.01f)] public float FootprintLength = .115f;
        private Transform[] _footprints;
        private MeshRenderer[] _footprintRenderers;
        private MaterialPropertyBlock _footprintProperties;
        private AudioSource _gunAudio;
        private int _nextFootprint;
        [Min(.01f)] public float MuzzleDuration = .065f;
        [Min(.01f)] public float SplashDuration = .15f;
        [Min(.01f)] public float MuzzleSize = .27f;
        [Min(.01f)] public float SplashSize = .3f;
        [Min(.01f)] public float BulletHoleSize = .045f;
        [Min(.01f)] public float BloodSize = .32f;
        [Min(0f)] public float MuzzleLightIntensity = 3f;
        [Min(.01f)] public float MuzzleLightRange = 2f;

        private Transform _muzzleFlash;
        private Light _muzzleLight;
        private Transform[] _holes, _blood, _splashes;
        private readonly float[] _splashEnds = new float[SPLASH_CAPACITY];
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private int _nextHole, _nextBlood, _nextSplash;
        private float _muzzleEnd;
        private Vector3 _shotDirection;
        private Transform _flashMuzzle;
        private SurvivalZombie[] _monsters;

        private void Awake()
        {
            _muzzleFlash = CreateQuad("Muzzle Flash", MuzzleMaterial);
            _muzzleLight = _muzzleFlash.gameObject.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.color = new Color(1f, .63f, .24f);
            _holes = CreatePool("Bullet Hole", BulletHoleMaterial, MARK_CAPACITY);
            _blood = CreatePool("Blood Mark", BloodMaterial, MARK_CAPACITY);
            _splashes = CreatePool("Blood Splash", SplashMaterial, SPLASH_CAPACITY);
            if (FootprintMaterial != null)
            {
                _footprints = CreatePool("Bloody Footprint", FootprintMaterial, FOOTPRINT_CAPACITY);
                _footprintRenderers = new MeshRenderer[FOOTPRINT_CAPACITY];
                for (int i = 0; i < _footprints.Length; i++)
                    _footprintRenderers[i] = _footprints[i].GetComponent<MeshRenderer>();
                _footprintProperties = new MaterialPropertyBlock();
            }
            _gunAudio = gameObject.AddComponent<AudioSource>();
            _gunAudio.playOnAwake = false;
            _monsters = Player.Companion.Monsters;
        }

        private bool _started;

        private void Start()
        {
            _started = true;
            OnEnable();
        }

        private void OnEnable()
        {
            if (!_started) return;
            foreach (SurvivalZombie zombie in _monsters)
                if (zombie != null) zombie.HitPoints.Damaged += OnMonsterDamaged;
        }

        private void OnDisable()
        {
            foreach (SurvivalZombie zombie in _monsters)
                if (zombie != null && zombie.HitPoints != null) zombie.HitPoints.Damaged -= OnMonsterDamaged;
            _muzzleFlash.gameObject.SetActive(false);
            foreach (Transform splash in _splashes) splash.gameObject.SetActive(false);
        }

        public void Flash(Vector3 direction) => Flash(direction, Player.Muzzle);

        public void Flash(Vector3 direction, Transform muzzle)
        {
            _flashMuzzle = muzzle;
            if (Gunfire != null)
            {
                _gunAudio.pitch = 1f + Random.Range(-GunPitchVariation, GunPitchVariation);
                _gunAudio.PlayOneShot(Gunfire, GunVolume);
            }
            _shotDirection = direction;
            _muzzleEnd = Time.time + MuzzleDuration;
            _muzzleLight.intensity = MuzzleLightIntensity;
            _muzzleLight.range = MuzzleLightRange;
            PlaceMuzzle();
            _muzzleFlash.gameObject.SetActive(true);
        }

        public void MarkSurface(RaycastHit hit)
        {
            PlaceMark(Next(_holes, ref _nextHole), hit, BulletHoleSize, .001f);
        }

        public void ShowMonsterImpact(Vector3 point)
        {
            int slot = _nextSplash;
            Transform splash = Next(_splashes, ref _nextSplash);
            _splashEnds[slot] = Time.time + SplashDuration;
            // Offset toward the camera so the character Quad cannot hide the impact.
            splash.SetPositionAndRotation(point - Player.View.transform.forward * .09f, Player.View.transform.rotation);
            splash.localScale = Vector3.one * SplashSize;
            splash.gameObject.SetActive(true);
        }

        private void OnMonsterDamaged(Health victim, float damage, GameObject source)
        {
            if (source != Player.gameObject && source != Player.Companion.gameObject) return;
            if (TryGetGround(victim.transform.position, out RaycastHit ground))
                PlaceMark(Next(_blood, ref _nextBlood), ground, BloodSize, .003f);
        }

        public bool IsOnBlood(Vector3 position)
        {
            float radiusSquared = BloodSize * BloodSize * .25f;
            foreach (Transform mark in _blood)
            {
                if (!mark.gameObject.activeSelf) continue;
                Vector3 offset = mark.position - position;
                if (Mathf.Abs(offset.y) <= .11f && offset.x * offset.x + offset.z * offset.z <= radiusSquared)
                    return true;
            }
            return false;
        }

        public bool StampFootprint(Vector3 position, Vector3 direction, bool left, float alpha)
        {
            if (_footprints == null || !TryGetGround(position, out RaycastHit ground)) return false;
            int slot = _nextFootprint;
            Transform mark = Next(_footprints, ref _nextFootprint);
            Vector3 forward = Vector3.ProjectOnPlane(direction, ground.normal);
            mark.SetPositionAndRotation(ground.point + ground.normal * .005f,
                Quaternion.LookRotation(-ground.normal, forward));
            mark.localScale = new Vector3(FootprintLength * .39f * (left ? -1f : 1f), FootprintLength, 1f);
            Color color = FootprintColor;
            color.a = alpha;
            _footprintProperties.SetColor(TINT, color);
            _footprintRenderers[slot].SetPropertyBlock(_footprintProperties);
            mark.gameObject.SetActive(true);
            return true;
        }

        private bool TryGetGround(Vector3 position, out RaycastHit ground)
        {
            Vector3 origin = position + Vector3.up * .18f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, .46f, Player.ShotLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            ground = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.normal.y < .5f || hit.distance >= nearest ||
                    hit.collider.GetComponentInParent<Health>() != null) continue;
                ground = hit;
                nearest = hit.distance;
            }
            return ground.collider != null;
        }

        private void LateUpdate()
        {
            if (_muzzleFlash.gameObject.activeSelf)
            {
                if (Time.time >= _muzzleEnd) _muzzleFlash.gameObject.SetActive(false);
                else PlaceMuzzle();
            }
            for (int i = 0; i < _splashes.Length; i++)
                if (_splashes[i].gameObject.activeSelf && Time.time >= _splashEnds[i])
                    _splashes[i].gameObject.SetActive(false);
        }

        private void PlaceMuzzle()
        {
            Vector3 forward = Player.View.transform.forward;
            Vector3 up = Vector3.Cross(forward, _shotDirection);
            Quaternion rotation = up.sqrMagnitude > .001f ? Quaternion.LookRotation(forward, up) : Player.View.transform.rotation;
            _muzzleFlash.SetPositionAndRotation((_flashMuzzle != null ? _flashMuzzle.position : Player.Muzzle.position) + _shotDirection * (MuzzleSize * .32f), rotation);
            _muzzleFlash.localScale = Vector3.one * MuzzleSize;
        }

        private static void PlaceMark(Transform mark, RaycastHit hit, float size, float offset)
        {
            Quaternion rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            mark.SetPositionAndRotation(hit.point + hit.normal * offset, rotation);
            mark.localScale = Vector3.one * size;
            mark.gameObject.SetActive(true);
        }

        private static Transform Next(Transform[] pool, ref int index)
        {
            Transform effect = pool[index];
            index = (index + 1) % pool.Length;
            return effect;
        }

        private Transform[] CreatePool(string label, Material material, int count)
        {
            var pool = new Transform[count];
            for (int i = 0; i < count; i++) pool[i] = CreateQuad(label, material);
            return pool;
        }

        private Transform CreateQuad(string label, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = label;
            var collider = quad.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            quad.transform.SetParent(transform, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            quad.SetActive(false);
            return quad.transform;
        }
    }
}



