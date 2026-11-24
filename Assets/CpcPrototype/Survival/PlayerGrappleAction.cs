using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public enum GrapplePhase { Idle, Extending, Swinging, Holding = 4 }

    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(SurvivalPlayer), typeof(CharacterController))]
    public sealed class PlayerGrappleAction : MonoBehaviour
    {
        public SurvivalPlayer Player;
        public GrappleAnchor[] Anchors = Array.Empty<GrappleAnchor>();
        public Transform RopeOrigin;
        public LineRenderer Rope;
        [Tooltip("Maximum world-space distance from the player to a bulb for targeting and firing.")]
        [Min(.01f)] public float MaxHookDistance = 8f;
        [Min(.001f)] public float MinHookDistance = .12f;
        [Min(1f)] public float MinimumRingPixels = 14f;
        [Min(.01f)] public float ExtensionSpeed = 12f;
        [Min(.01f)] public float SwingDuration = 1.1f;
        [Min(.01f)] public float MaximumHoldSeconds = 8f;
        [Min(0f)] public float CollisionTolerance = .04f;
        [Min(.001f)] public float RopeWidth = .008f;
        public LayerMask ObstacleLayers = ~0;
        public Color ReadyColor = new Color(.1f, 1f, .3f, 1f);
        public Color InactiveColor = new Color(.45f, .86f, 1f, .6f);
        public Color RopeColor = new Color(.3f, 1f, .6f, 1f);
        public GrapplePhase Phase { get; private set; }
        public GrappleAnchor HoveredAnchor { get; private set; }
        public GrappleAnchor CurrentAnchor { get; private set; }
        public bool IsControllingMovement => Phase != GrapplePhase.Idle;
        public bool IsTravelling => Phase != GrapplePhase.Idle && Phase != GrapplePhase.Holding;
        public float InitialLength { get; private set; }
        public float CurrentLength { get; private set; }
        public event Action Started;
        public event Action Arrived;
        public event Action Released;
        private CharacterController _controller;
        private CombatStatus _status;
        private Transform _root;
        private Material _ropeMaterial;
        private Vector3 _anchorPosition, _radial, _swingTangent;
        private float _elapsed, _extensionDuration, _holdStarted, _swingFloor;
        private readonly RaycastHit[] _hits = new RaycastHit[64];

        private void Awake()
        {
            _root = transform;
            _controller = GetComponent<CharacterController>();
            _status = CombatStatus.Get(gameObject);
            if (Player == null) Player = GetComponent<SurvivalPlayer>();
            if (Rope == null)
            {
                var ropeObject = new GameObject("Grapple Rope");
                ropeObject.transform.SetParent(_root, false);
                Rope = ropeObject.AddComponent<LineRenderer>();
                _ropeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Rope.sharedMaterial = _ropeMaterial;
            }
            Rope.useWorldSpace = true;
            Rope.positionCount = 2;
            Rope.enabled = false;
        }

        private void Update()
        {
            if (Player.IsChatOpen || Player.Health == null || !Player.Health.IsAlive || _status.CannotMove)
            {
                HoveredAnchor = null;
                Release();
                return;
            }
            if (Phase == GrapplePhase.Holding && Time.unscaledTime - _holdStarted > MaximumHoldSeconds &&
                (Player.GrappleSniper == null || !Player.GrappleSniper.IsActive))
                Release();
            if (IsControllingMovement || Player.GrappleSniper != null && Player.GrappleSniper.IsActive)
            {
                HoveredAnchor = null;
                return;
            }
            Mouse mouse = Mouse.current;
            HoveredAnchor = mouse != null && TryGetAnchor(mouse.position.ReadValue(), out GrappleAnchor target) ? target : null;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && HoveredAnchor != null)
                TryBegin(HoveredAnchor);
        }

        public bool TryGetAnchor(Vector2 screen, out GrappleAnchor target)
        {
            target = null;
            float nearest = float.PositiveInfinity;
            if (!Player.View.pixelRect.Contains(screen)) return false;
            foreach (GrappleAnchor anchor in Anchors)
            {
                if (!CanHook(anchor) || !GetRing(anchor, out Vector2 center, out float radius)) continue;
                float distance = (screen - center).sqrMagnitude;
                if (distance <= radius * radius && distance < nearest)
                {
                    nearest = distance;
                    target = anchor;
                }
            }
            return target != null;
        }

        public bool CanHook(GrappleAnchor anchor)
        {
            if (anchor == null || !anchor.isActiveAndEnabled) return false;
            Vector3 offset = anchor.Position - _root.position;
            float distance = offset.magnitude;
            if (distance < MinHookDistance || distance > MaxHookDistance) return false;
            Vector3 origin = _controller.bounds.center;
            Vector3 ray = anchor.Position - origin;
            int count = Physics.RaycastNonAlloc(origin, ray.normalized, _hits, ray.magnitude,
                ObstacleLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i].collider;
                if (hit.transform.IsChildOf(_root) || hit == anchor.Platform ||
                    hit.transform.IsChildOf(anchor.transform)) continue;
                return false;
            }
            return true;
        }

        public bool TryBegin(GrappleAnchor anchor)
        {
            if (IsControllingMovement || !isActiveAndEnabled || Player.IsChatOpen ||
                Player.Health == null || !Player.Health.IsAlive || _status.CannotMove ||
                Player.GrappleSniper != null && Player.GrappleSniper.IsActive || !CanHook(anchor)) return false;
            CurrentAnchor = anchor;
            _anchorPosition = anchor.Position;
            _radial = _root.position - _anchorPosition;
            InitialLength = CurrentLength = _radial.magnitude;
            // Keep the arc in the vertical plane through the player and bulb. A vertical start has no sideways direction.
            Vector3 radialDirection = _radial / InitialLength;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.down, radialDirection);
            _swingTangent = tangent.sqrMagnitude > .00000001f ? tangent.normalized : Vector3.zero;
            if (_radial.y > 0f) _swingTangent = -_swingTangent;
            _swingFloor = GetSwingFloor();
            _elapsed = 0f;
            _extensionDuration = InitialLength / Mathf.Max(.01f, ExtensionSpeed);
            Phase = GrapplePhase.Extending;
            Player.ResetMotion();
            Rope.enabled = true;
            Started?.Invoke();
            return true;
        }

        private float GetSwingFloor()
        {
            Bounds bounds = _controller.bounds;
            float clearance = Mathf.Max(0f, _radial.y + InitialLength);
            int count = Physics.RaycastNonAlloc(bounds.center, Vector3.down, _hits,
                bounds.extents.y + clearance, ObstacleLayers, QueryTriggerInteraction.Ignore);
            float floor = _controller.isGrounded ? _root.position.y : float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(_root)) continue;
                float groundClearance = Mathf.Max(0f, _hits[i].distance - bounds.extents.y);
                floor = Mathf.Max(floor, _root.position.y - groundClearance);
            }

            return floor;
        }

        private static float GetRadiusScale(float progress)
        {
            float remaining = 1f - progress;
            return .5f + .5f * remaining * remaining * remaining;
        }

        private void FixedUpdate()
        {
            if (!CanContinueTravel()) return;

            _elapsed += Time.fixedDeltaTime;
            if (!TryEnterSwing()) return;

            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(.01f, SwingDuration));
            Vector3 desired = CalculateSwingTarget(progress);
            if (!TryMoveToSwingTarget(desired)) return;

            if (progress >= 1f) CompleteSwing();
        }

        private bool CanContinueTravel()
        {
            if (!IsTravelling) return false;
            if (CurrentAnchor != null && _controller.enabled) return true;

            Release();
            return false;
        }

        private bool TryEnterSwing()
        {
            if (Phase != GrapplePhase.Extending) return true;
            if (_elapsed < _extensionDuration) return false;

            // Recheck the hook after the rope has actually reached the bulb.
            if (!CanHook(CurrentAnchor))
            {
                Release();
                return false;
            }

            // The swing timer starts at zero on the rope's arrival tick.
            _elapsed = 0f;
            Phase = GrapplePhase.Swinging;
            return true;
        }

        private Vector3 CalculateSwingTarget(float progress)
        {
            float eased = progress * progress * (3f - 2f * progress);
            float angle = Mathf.PI * eased;
            float endLength = InitialLength * .5f;
            Vector3 direction = Mathf.Cos(angle) * (_radial / InitialLength) + Mathf.Sin(angle) * _swingTangent;
            float length = Mathf.Min(InitialLength * GetRadiusScale(eased), Mathf.Max(endLength, CurrentLength));
            if (direction.y < 0f && _anchorPosition.y + direction.y * length < _swingFloor)
                length = Mathf.Clamp((_swingFloor - _anchorPosition.y) / direction.y, endLength, length);

            Vector3 desired = _anchorPosition + length * direction;
            // Shallow hooks can run out of vertical room even at half length. Skim the floor in the same plane.
            desired.y = Mathf.Max(desired.y, _swingFloor);
            if (progress >= 1f) desired = _anchorPosition - .5f * _radial;
            CurrentLength = Vector3.Distance(_anchorPosition, desired);
            Debug.Log(progress);
            Debug.Log(eased);
            Debug.Log(angle);
            return desired;
        }

        private bool TryMoveToSwingTarget(Vector3 desired)
        {
            _controller.Move(desired - _root.position);
            if (Vector3.Distance(desired, _root.position) > CollisionTolerance)
            {
                // Collision prevented the controller from reaching this tick's target.
                Release();
                return false;
            }

            return true;
        }

        private void CompleteSwing()
        {
            Phase = GrapplePhase.Holding;
            _holdStarted = Time.unscaledTime;
            Player.ResetMotion();
            Arrived?.Invoke();
        }

        private void LateUpdate()
        {
            if (Rope == null || !Rope.enabled) return;
            Vector3 origin = RopeOrigin != null ? RopeOrigin.position : _controller.bounds.center;
            float extend = Phase == GrapplePhase.Extending ? Mathf.Clamp01(_elapsed / _extensionDuration) : 1f;
            Rope.widthMultiplier = RopeWidth;
            Rope.startColor = Rope.endColor = RopeColor;
            if (_ropeMaterial != null) _ropeMaterial.SetColor("_BaseColor", RopeColor);
            Rope.SetPosition(0, origin);
            Rope.SetPosition(1, Vector3.Lerp(origin, _anchorPosition, extend));
        }

        public void Release()
        {
            if (Phase == GrapplePhase.Idle) return;
            Phase = GrapplePhase.Idle;
            CurrentAnchor = HoveredAnchor = null;
            if (Rope != null) Rope.enabled = false;
            Player.ResetMotion();
            Released?.Invoke();
        }

        private bool GetRing(GrappleAnchor anchor, out Vector2 center, out float radius)
        {
            Vector3 screen = Player.View.WorldToScreenPoint(anchor.Position);
            center = screen;
            radius = Mathf.Max(MinimumRingPixels, Mathf.Abs(Player.View.WorldToScreenPoint(
                anchor.Position + Player.View.transform.right * anchor.RingRadius).x - screen.x));
            return screen.z > Player.View.nearClipPlane && Player.View.pixelRect.Contains(screen);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Player.IsChatOpen || IsControllingMovement ||
                Player.GrappleSniper != null && Player.GrappleSniper.IsActive) return;
            Color previous = GUI.color;
            foreach (GrappleAnchor anchor in Anchors)
            {
                if (!CanHook(anchor) || !GetRing(anchor, out Vector2 center, out float radius)) continue;
                GUI.color = anchor == HoveredAnchor ? ReadyColor : InactiveColor;
                KayKitAimGuide.DrawRing(new Vector2(center.x, Screen.height - center.y), radius);
            }
            GUI.color = previous;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        private void OnDisable() => Release();
        private void OnDestroy() { if (_ropeMaterial != null) Destroy(_ropeMaterial); }
    }
}
