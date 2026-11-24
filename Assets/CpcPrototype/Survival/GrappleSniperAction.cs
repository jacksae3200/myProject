using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public enum GrappleSniperPhase { Idle, Entering, Ready, Returning }

    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(SurvivalPlayer), typeof(PlayerGrappleAction))]
    public sealed class GrappleSniperAction : MonoBehaviour
    {
        public SurvivalPlayer Player;
        public PlayerGrappleAction Grapple;
        public Transform Sniper;
        public Transform ScopeEyePoint;
        public Camera ScopeCamera;
        public Transform SniperMuzzle;
        public Transform SniperGripPoint;
        public Transform Head;
        public Transform BackSocket;
        public Transform HandSocket;
        public EquipmentGrip SniperHandGrip;
        public ActorPixelQuad PlayerPixels;
        public ActorPixelQuad[] PixelActors = Array.Empty<ActorPixelQuad>();
        public Material[] ScreenPixelMaterials = Array.Empty<Material>();
        public string ScreenPixelProperty = "_PixelHeight";
        [Min(1f)] public float ScreenPixelMultiplier = 2f;
        public Transform CinematicTarget;
        public bool AutoSelectTarget = true;
        [Min(.1f)] public float SlowMotionSeconds = 5f;
        [Range(.01f, 1f)] public float SlowMotionScale = .2f;
        [Min(.01f)] public float CameraTransitionSeconds = .55f;
        [Min(.01f)] public float AimSeconds = .2f;
        [Range(1f, 120f)] public float FirstPersonFov = 65f;
        [Range(1f, 120f)] public float ScopedFov = 32f;
        [Min(.001f)] public float NearClip = .003f;
        public Vector3 FirstPersonEyeOffset = new Vector3(0f, .01f, .025f);
        public Vector3 HipScopeOffset = new Vector3(.066f, -.004f, .107f);
        public Vector3 HipWeaponAngles = new Vector3(0f, -7f, 0f);
        [Min(0f)] public float EyeRelief = .025f;
        public Vector3 SniperBackPosition = new Vector3(-.12f, .03f, -.08f);
        public Vector3 SniperBackRotation = new Vector3(10f, 0f, -35f);
        public Vector3 RifleBackPosition = new Vector3(.12f, .03f, -.08f);
        public Vector3 RifleBackRotation = new Vector3(10f, 180f, 35f);
        [Min(.001f)] public float SniperWorldScale = .18f;
        [Min(0f)] public float BodyDamage = 60f;
        [Min(0f)] public float HeadDamage = 120f;
        [Min(.01f)] public float ShotInterval = 1f;
        [Min(.1f)] public float ShotRange = 30f;
        [Min(0f)] public float LookSensitivity = .12f;
        [Range(1f, 89f)] public float PitchLimit = 75f;
        [Min(0f)] public float RecoilDegrees = 4f;
        [Min(0f)] public float RecoilDistance = .015f;
        [Min(.01f)] public float RecoilRecovery = 9f;
        [Min(1f)] public float PixelResolutionMultiplier = 2f;
        [Range(0, 31)] public int WeaponLayer;
        public bool RestoreCursorOnExit = true;
        public GrappleSniperPhase Phase { get; private set; }
        public bool IsActive => Phase != GrappleSniperPhase.Idle;
        public bool ControlsView => IsActive;
        public float ViewBlend { get; private set; }
        public float AimBlend { get; private set; }
        public float RecoilAmount => _recoil;
        public int ShotsFired { get; private set; }
        public event Action Shot;
        private Transform _root, _view, _rifleParent;
        private Transform[] _sniperTransforms;
        private int[] _sniperLayers;
        private Vector3 _riflePosition, _rifleScale, _sniperScale;
        private Quaternion _rifleRotation, _scopeLocalRotation;
        private Vector3 _cameraStart, _returnStart;
        private Quaternion _cameraStartRotation, _returnRotation;
        private float _startFov, _returnFov, _phaseTime, _deadline, _nextShot;
        private float _yaw, _pitch, _recoil, _originalScale, _originalFixed, _originalNear;
        private float[] _pixelBlends, _pixelMultipliers;
        private float[] _screenPixelValues;
        private int _screenPixelPropertyId;
        private bool _scopeWasEnabled, _ownsTime, _equipmentCached;
        private bool _ownQuadEnabled, _viewRifleEnabled;
        private int _originalMask;
        private CursorLockMode _cursorLock;
        private bool _cursorVisible;
        private Transform _selectedTarget;
        private CombatStatus _status;
        private readonly RaycastHit[] _hits = new RaycastHit[64];

        private void Awake()
        {
            _root = transform;
            if (Player == null) Player = GetComponent<SurvivalPlayer>();
            if (Grapple == null) Grapple = GetComponent<PlayerGrappleAction>();
            _view = Player.View.transform;
            _status = CombatStatus.Get(gameObject);
            _sniperTransforms = Sniper.GetComponentsInChildren<Transform>(true);
            _sniperLayers = new int[_sniperTransforms.Length];
            for (int i = 0; i < _sniperTransforms.Length; i++) _sniperLayers[i] = _sniperTransforms[i].gameObject.layer;
            _scopeLocalRotation = Quaternion.Inverse(Sniper.rotation) * ScopeCamera.transform.rotation;
            _sniperScale = Sniper.localScale;
            _pixelBlends = new float[PixelActors.Length];
            _pixelMultipliers = new float[PixelActors.Length];
            _screenPixelValues = new float[ScreenPixelMaterials.Length];
            _screenPixelPropertyId = Shader.PropertyToID(ScreenPixelProperty);
            CacheRifle();
        }

        private void CacheRifle()
        {
            if (_equipmentCached || Player.Rifle == null) return;
            _rifleParent = Player.Rifle.parent;
            _riflePosition = Player.Rifle.localPosition;
            _rifleRotation = Player.Rifle.localRotation;
            _rifleScale = Player.Rifle.localScale;
            _equipmentCached = true;
        }

        private void OnEnable()
        {
            Grapple.Arrived += BeginSniper;
            Grapple.Released += GrappleReleased;
        }

        private void OnDisable()
        {
            Grapple.Arrived -= BeginSniper;
            Grapple.Released -= GrappleReleased;
            RestoreImmediately();
        }

        private void SelectTarget()
        {
            _selectedTarget = CinematicTarget;
            if (_selectedTarget != null || !AutoSelectTarget) return;
            float nearest = float.PositiveInfinity;
            foreach (SurvivalZombie zombie in SurvivalZombie.Active)
            {
                if (zombie == null || !zombie.isActiveAndEnabled || zombie.IsHidden ||
                    zombie.HitPoints == null || !zombie.HitPoints.IsAlive) continue;
                float distance = (zombie.AimPoint - _root.position).sqrMagnitude;
                if (distance >= nearest || !HasSight(zombie)) continue;
                _selectedTarget = zombie.transform;
                nearest = distance;
            }
            if (_selectedTarget == null && Player.Companion != null) _selectedTarget = Player.Companion.transform;
        }

        private bool HasSight(SurvivalZombie zombie)
        {
            Vector3 start = Head.position;
            Vector3 offset = zombie.AimPoint - start;
            int count = Physics.RaycastNonAlloc(start, offset.normalized, _hits, offset.magnitude,
                Player.ShotLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].transform;
                if (hit.IsChildOf(_root) || hit.IsChildOf(zombie.transform)) continue;
                return false;
            }
            return true;
        }

        public void BeginSniper()
        {
            if (IsActive || !isActiveAndEnabled || Grapple.Phase != GrapplePhase.Holding ||
                Player.Health == null || !Player.Health.IsAlive) return;
            SelectTarget();
            CacheRifle();
            _cameraStart = _view.position;
            _cameraStartRotation = _view.rotation;
            _startFov = Player.View.fieldOfView;
            _originalNear = Player.View.nearClipPlane;
            _originalMask = Player.View.cullingMask;
            _cursorLock = Cursor.lockState;
            _cursorVisible = Cursor.visible;
            _ownQuadEnabled = PlayerPixels != null && PlayerPixels.Quad.enabled;
            _viewRifleEnabled = Player.ViewRifle != null && Player.ViewRifle.activeSelf;
            _scopeWasEnabled = ScopeCamera.enabled;
            _originalScale = Time.timeScale;
            _originalFixed = Time.fixedDeltaTime;
            _ownsTime = true;
            Time.timeScale = _originalScale * Mathf.Clamp(SlowMotionScale, .01f, 1f);
            Time.fixedDeltaTime = _originalFixed * Mathf.Clamp(SlowMotionScale, .01f, 1f);
            _deadline = Time.unscaledTime + Mathf.Max(.1f, SlowMotionSeconds);
            for (int i = 0; i < ScreenPixelMaterials.Length; i++)
            {
                Material material = ScreenPixelMaterials[i];
                if (material != null && material.HasProperty(_screenPixelPropertyId))
                    _screenPixelValues[i] = material.GetFloat(_screenPixelPropertyId);
            }
            for (int i = 0; i < PixelActors.Length; i++)
            {
                ActorPixelQuad actor = PixelActors[i];
                if (actor == null) continue;
                _pixelBlends[i] = actor.FocusResolutionBlend;
                _pixelMultipliers[i] = actor.FocusResolutionMultiplier;
                actor.FocusResolutionMultiplier = PixelResolutionMultiplier;
                actor.PrepareFocusResolution();
            }
            MoveToBack(Player.Rifle, RifleBackPosition, RifleBackRotation);
            Sniper.SetParent(HandSocket, true);
            Sniper.localScale = Vector3.one * (SniperWorldScale / Mathf.Max(.001f, HandSocket.lossyScale.x));
            for (int i = 0; i < _sniperTransforms.Length; i++) _sniperTransforms[i].gameObject.layer = WeaponLayer;
            Player.View.cullingMask |= 1 << WeaponLayer;
            Player.View.nearClipPlane = Mathf.Max(.001f, NearClip);
            if (Player.ViewRifle != null) Player.ViewRifle.SetActive(false);
            if (PlayerPixels != null) PlayerPixels.Quad.enabled = false;
            ScopeCamera.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Vector3 direction = _selectedTarget != null ? TargetPoint(_selectedTarget) - Head.position : _root.forward;
            Quaternion look = direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction) : _root.rotation;
            _yaw = look.eulerAngles.y;
            _pitch = Mathf.DeltaAngle(0f, look.eulerAngles.x);
            ViewBlend = AimBlend = _recoil = 0f;
            _nextShot = 0f;
            Phase = GrappleSniperPhase.Entering;
            _phaseTime = 0f;
        }

        private static Vector3 TargetPoint(Transform target)
        {
            if (target.TryGetComponent(out SurvivalZombie zombie)) return zombie.AimPoint;
            return target.position + Vector3.up * .25f;
        }

        private void MoveToBack(Transform equipment, Vector3 position, Vector3 rotation)
        {
            equipment.SetParent(BackSocket, false);
            equipment.localPosition = position;
            equipment.localRotation = Quaternion.Euler(rotation);
        }

        private void Update()
        {
            if (!IsActive) return;
            if (Player.IsChatOpen || !Player.Health.IsAlive || _status.IsStunned ||
                Grapple.Phase != GrapplePhase.Holding)
            {
                RestoreImmediately();
                return;
            }
            if (Phase == GrappleSniperPhase.Returning) return;
            if (Time.unscaledTime >= _deadline || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                EndSniper();
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            // Mouse delta and all aiming/transition timers deliberately ignore timeScale.
            Vector2 delta = mouse.delta.ReadValue() * LookSensitivity;
            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, -PitchLimit, PitchLimit);
            float desiredAim = Phase == GrappleSniperPhase.Ready && mouse.rightButton.isPressed ? 1f : 0f;
            AimBlend = Mathf.MoveTowards(AimBlend, desiredAim, Time.unscaledDeltaTime / Mathf.Max(.01f, AimSeconds));
            _recoil = Mathf.MoveTowards(_recoil, 0f, RecoilRecovery * Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            _phaseTime += Time.unscaledDeltaTime;
            if (Phase == GrappleSniperPhase.Returning)
            {
                float t = Mathf.Clamp01(_phaseTime / Mathf.Max(.01f, CameraTransitionSeconds));
                float blend = Smooth(t);
                GetThirdPersonPose(out Vector3 target, out Quaternion rotation, out float fov);
                _view.SetPositionAndRotation(Vector3.Lerp(_returnStart, target, blend), Quaternion.Slerp(_returnRotation, rotation, blend));
                Player.View.fieldOfView = Mathf.Lerp(_returnFov, fov, blend);
                ViewBlend = 1f - blend;
                SetPixelFocus(ViewBlend);
                if (t >= 1f) RestoreImmediately();
                return;
            }
            float entry = Mathf.Clamp01(_phaseTime / Mathf.Max(.01f, CameraTransitionSeconds));
            ViewBlend = Phase == GrappleSniperPhase.Entering ? Smooth(entry) : 1f;
            Quaternion baseLook = Quaternion.Euler(_pitch, _yaw, 0f);
            Quaternion recoilLook = Quaternion.Euler(_pitch - _recoil * RecoilDegrees, _yaw, 0f);
            Vector3 head = Head.position + baseLook * FirstPersonEyeOffset;
            float aim = Smooth(AimBlend);
            PositionSniper(head, recoilLook, aim);
            _view.SetPositionAndRotation(Vector3.Lerp(_cameraStart, head, ViewBlend),
                Quaternion.Slerp(_cameraStartRotation, recoilLook, ViewBlend));
            Player.View.fieldOfView = Mathf.Lerp(_startFov, Mathf.Lerp(FirstPersonFov, ScopedFov, aim), ViewBlend);
            SetPixelFocus(ViewBlend);
            if (Phase == GrappleSniperPhase.Entering && entry >= 1f) Phase = GrappleSniperPhase.Ready;
            if (Phase == GrappleSniperPhase.Ready && Mouse.current != null && Mouse.current.leftButton.isPressed)
                TryFire();
        }

        private void PositionSniper(Vector3 eye, Quaternion look, float aim)
        {
            Vector3 hipScope = HipScopeOffset;
            // Limit the kick so the rear of the scope stays in front of the unscoped camera.
            hipScope.z -= _recoil * Mathf.Min(RecoilDistance, Mathf.Max(0f, hipScope.z * .25f));
            Vector3 scopePosition = eye + look * Vector3.Lerp(hipScope, Vector3.forward * EyeRelief, aim);
            Quaternion scopeRotation = look * Quaternion.Slerp(Quaternion.Euler(HipWeaponAngles), Quaternion.identity, aim);
            Sniper.rotation = scopeRotation * Quaternion.Inverse(_scopeLocalRotation);
            Sniper.position += scopePosition - ScopeEyePoint.position;
            if (SniperHandGrip == null) return;

            // Aim stays centered even when the animated arms cannot reach the requested grip.
            Pose weaponPose = new Pose(Sniper.position, Sniper.rotation);
            SniperHandGrip.AlignAt(SniperGripPoint.position);
            Sniper.SetPositionAndRotation(weaponPose.position, weaponPose.rotation);
        }

        public bool TryFire()
        {
            if (Phase != GrappleSniperPhase.Ready || Time.unscaledTime < _nextShot ||
                Time.unscaledTime >= _deadline || Player.IsChatOpen) return false;
            Transform previousMuzzle = Player.Muzzle;
            Player.Muzzle = SniperMuzzle;
            bool fired;
            try { fired = Player.FireSniper(SniperMuzzle, BodyDamage, HeadDamage, ShotRange); }
            finally { Player.Muzzle = previousMuzzle; }
            if (!fired) return false;
            _nextShot = Time.unscaledTime + Mathf.Max(.01f, ShotInterval);
            _recoil = 1f;
            ShotsFired++;
            Shot?.Invoke();
            return true;
        }

        public void EndSniper()
        {
            if (!IsActive || Phase == GrappleSniperPhase.Returning) return;
            RestoreTime();
            _returnStart = _view.position;
            _returnRotation = _view.rotation;
            _returnFov = Player.View.fieldOfView;
            Phase = GrappleSniperPhase.Returning;
            _phaseTime = 0f;
            AimBlend = 0f;
        }

        private void GetThirdPersonPose(out Vector3 position, out Quaternion rotation, out float fov)
        {
            if (Player.DepthView)
            {
                float height = Mathf.Approximately(Player.DepthCameraOffset.y, 0f) ? Mathf.Max(Player.CameraPivot.y, Player.DepthViewSize - .16f) : Player.CameraPivot.y;
                position = new Vector3(_root.position.x, height, _root.position.z * .35f) + Player.DepthCameraOffset;
                rotation = Quaternion.LookRotation(-Player.DepthCameraOffset);
            }
            else
            {
                rotation = _cameraStartRotation;
                position = _root.position + Player.CameraPivot + rotation * Player.ThirdPersonOffset;
            }
            fov = _startFov;
        }

        private void SetPixelFocus(float blend)
        {
            // Override the actual fullscreen pass as well as actor captures; restore its entry value.
            for (int i = 0; i < ScreenPixelMaterials.Length; i++)
            {
                Material material = ScreenPixelMaterials[i];
                if (material != null && material.HasProperty(_screenPixelPropertyId))
                    material.SetFloat(_screenPixelPropertyId, Mathf.Lerp(_screenPixelValues[i],
                        _screenPixelValues[i] * ScreenPixelMultiplier, blend));
            }
            for (int i = 0; i < PixelActors.Length; i++)
                if (PixelActors[i] != null) PixelActors[i].FocusResolutionBlend = Mathf.Lerp(_pixelBlends[i], 1f, blend);
        }

        private void RestoreTime()
        {
            if (!_ownsTime) return;
            Time.timeScale = _originalScale;
            Time.fixedDeltaTime = _originalFixed;
            _ownsTime = false;
        }

        public void RestoreImmediately()
        {
            if (!IsActive) return;
            RestoreTime();
            for (int i = 0; i < ScreenPixelMaterials.Length; i++)
                if (ScreenPixelMaterials[i] != null && ScreenPixelMaterials[i].HasProperty(_screenPixelPropertyId))
                    ScreenPixelMaterials[i].SetFloat(_screenPixelPropertyId, _screenPixelValues[i]);
            Phase = GrappleSniperPhase.Idle;
            ViewBlend = AimBlend = _recoil = 0f;
            Player.View.nearClipPlane = _originalNear;
            Player.View.cullingMask = _originalMask;
            ScopeCamera.enabled = _scopeWasEnabled;
            if (PlayerPixels != null) PlayerPixels.Quad.enabled = _ownQuadEnabled;
            if (Player.ViewRifle != null) Player.ViewRifle.SetActive(_viewRifleEnabled);
            for (int i = 0; i < PixelActors.Length; i++)
            {
                if (PixelActors[i] == null) continue;
                PixelActors[i].FocusResolutionBlend = _pixelBlends[i];
                PixelActors[i].FocusResolutionMultiplier = _pixelMultipliers[i];
            }
            for (int i = 0; i < _sniperTransforms.Length; i++)
                if (_sniperTransforms[i] != null) _sniperTransforms[i].gameObject.layer = _sniperLayers[i];
            if (Player.Rifle != null && _equipmentCached)
            {
                Player.Rifle.SetParent(_rifleParent, false);
                Player.Rifle.localPosition = _riflePosition;
                Player.Rifle.localRotation = _rifleRotation;
                Player.Rifle.localScale = _rifleScale;
            }
            MoveToBack(Sniper, SniperBackPosition, SniperBackRotation);
            Sniper.localScale = _sniperScale;
            if (RestoreCursorOnExit && !Player.IsChatOpen)
            {
                Cursor.lockState = _cursorLock;
                Cursor.visible = _cursorVisible;
            }
            Player.ResetMotion();
            Grapple.Release();
        }

        private void GrappleReleased()
        {
            if (IsActive) RestoreImmediately();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) RestoreImmediately(); }
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
