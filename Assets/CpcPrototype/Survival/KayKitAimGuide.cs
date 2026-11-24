using UnityEngine;
using UnityEngine.InputSystem;

namespace CpcPrototype
{
    public sealed class KayKitAimGuide : MonoBehaviour
    {
        public SurvivalPlayer Player;
        [Min(.05f)] public float Radius = .17f;
        [Min(.01f)] public float HeadRadius = .047f;
        [Min(1f)] public float HeadshotMultiplier = 2f;
        public Color GuideColor = new Color(.45f, .86f, 1f, .55f);
        public Color LockColor = new Color(1f, .55f, .24f);

        public bool TryGetTarget(Vector2 cursor, out Vector3 target)
        {
            if (TryGetHeadTarget(cursor, out SurvivalZombie head))
            {
                target = head.HeadAimPoint;
                return true;
            }
            target = default;
            float nearest = float.PositiveInfinity;
            foreach (SurvivalZombie zombie in Player.Companion.Monsters)
            {
                if (!GetRing(zombie, out Vector3 world, out Vector2 center, out float radius)) continue;
                float distance = (cursor - center).sqrMagnitude;
                if (distance > radius * radius || distance >= nearest) continue;
                nearest = distance;
                target = world;
            }
            return nearest < float.PositiveInfinity;
        }

        public bool TryGetHeadTarget(Vector2 cursor, out SurvivalZombie target)
        {
            target = null;
            float nearest = float.PositiveInfinity;
            foreach (SurvivalZombie zombie in Player.Companion.Monsters)
            {
                if (zombie == null || !zombie.HasHead ||
                    !GetRing(zombie, out _, out Vector2 center, out float radius, true)) continue;
                float distance = (cursor - center).sqrMagnitude;
                if (distance > radius * radius || distance >= nearest) continue;
                target = zombie;
                nearest = distance;
            }
            return target != null;
        }

        private bool GetRing(SurvivalZombie zombie, out Vector3 world, out Vector2 center, out float radius, bool head = false)
        {
            world = default;
            center = default;
            radius = 0;
            if (zombie == null || !zombie.isActiveAndEnabled || zombie.HitPoints == null || !zombie.HitPoints.IsAlive) return false;
            world = head ? zombie.HeadAimPoint : zombie.AimPoint;
            Vector3 screen = Player.View.WorldToScreenPoint(world);
            if (screen.z <= Player.View.nearClipPlane || !Player.View.pixelRect.Contains(screen)) return false;
            center = screen;
            float worldRadius = head ? HeadRadius : Radius;
            radius = Player.View.WorldToScreenPoint(world + Player.View.transform.right * worldRadius).x - screen.x;
            return true;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Mouse.current == null ||
                Player.IsChatOpen || Player.GrappleSniper != null && Player.GrappleSniper.ControlsView) return;
            Vector2 mouse = Mouse.current.position.ReadValue();
            if (!Player.View.pixelRect.Contains(mouse)) return;
            bool locked = TryGetTarget(mouse, out Vector3 lockedPoint);
            Color previous = GUI.color;
            if (Player.GetGuideTarget(mouse, out Vector3 target))
            {
                GUI.color = locked ? LockColor : GuideColor;
                DrawLine(ToGui(Player.View.WorldToScreenPoint(Player.Muzzle.position)),
                    ToGui(Player.View.WorldToScreenPoint(target)));
            }
            foreach (SurvivalZombie zombie in Player.Companion.Monsters)
            {
                if (!GetRing(zombie, out Vector3 world, out Vector2 center, out float radius)) continue;
                GUI.color = locked && world == lockedPoint ? LockColor : GuideColor;
                DrawRing(ToGui(center), radius);
                if (zombie.HasHead && GetRing(zombie, out Vector3 head, out Vector2 headCenter, out float headRadius, true))
                {
                    GUI.color = locked && head == lockedPoint ? LockColor : GuideColor;
                    DrawRing(ToGui(headCenter), headRadius);
                }
            }
            GUI.color = Player.Grapple != null && Player.Grapple.HoveredAnchor != null
                ? Player.Grapple.ReadyColor : locked ? LockColor : Color.white;
            Vector2 cursor = ToGui(mouse);
            DrawLine(cursor - Vector2.right * 8, cursor + Vector2.right * 8);
            DrawLine(cursor - Vector2.up * 8, cursor + Vector2.up * 8);
            GUI.color = previous;
        }

        public static void DrawRing(Vector2 center, float radius)
        {
            Vector2 last = center + Vector2.right * radius;
            for (int i = 1; i <= 32; i++)
            {
                float angle = i * (2f * Mathf.PI / 32f);
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(last, next);
                last = next;
            }
        }

        private static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        private static void DrawLine(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - .75f, delta.magnitude, 1.5f), Texture2D.whiteTexture);
            GUI.matrix = previous;
        }
    }
}


