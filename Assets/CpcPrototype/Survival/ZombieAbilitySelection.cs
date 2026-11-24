using System;
using UnityEngine;

namespace CpcPrototype
{
    public static class ZombieAbilitySelection
    {
        public static Type GetAbilityType(ZombieKind kind)
        {
            switch (kind)
            {
                case ZombieKind.Spitter: return typeof(SpitterZombieAbility);
                case ZombieKind.Boomer: return typeof(BoomerZombieAbility);
                case ZombieKind.Crawler: return typeof(CrawlerZombieAbility);
                case ZombieKind.Burrower: return typeof(BurrowerZombieAbility);
                case ZombieKind.Jumper: return typeof(JumperZombieAbility);
                default: return null;
            }
        }

        // 에디터에서는 Undo를 지원하고, 빌드에서는 필요한 컴포넌트를 직접 구성한다.
        public static ZombieAbility Synchronize(SurvivalZombie zombie)
        {
            Type selected = GetAbilityType(zombie.Kind);
            ZombieAbility result = null;
            foreach (ZombieAbility ability in zombie.GetComponents<ZombieAbility>())
            {
                if (ability.GetType() == selected && result == null) result = ability;
                else Remove(ability);
            }
            if (selected != null && result == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) result = (ZombieAbility)UnityEditor.Undo.AddComponent(zombie.gameObject, selected);
                else
#endif
                    result = (ZombieAbility)zombie.gameObject.AddComponent(selected);
            }
            ZombieAbilities legacy = zombie.GetComponent<ZombieAbilities>();
            if (legacy != null)
            {
                // 이름이 같은 공개 필드를 복사하여 사용자 수정값을 보존한다.
                if (result != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.Undo.RecordObject(result, "Migrate zombie settings");
#endif
                    foreach (var field in result.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        var oldField = typeof(ZombieAbilities).GetField(field.Name);
                        if (oldField != null && oldField.FieldType == field.FieldType) field.SetValue(result, oldField.GetValue(legacy));
                    }
                    if (result is CrawlerZombieAbility crawler) crawler.MoveSpeedMultiplier = zombie.CrawlerSpeedMultiplier;
#if UNITY_EDITOR
                    if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(result);
#endif
                }
                Remove(legacy);
            }
            return result;
        }

        private static void Remove(MonoBehaviour component)
        {
            component.enabled = false;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(component); return; }
#endif
            UnityEngine.Object.Destroy(component);
        }
    }
}
