using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CpcPrototype.Editor
{
    public static class CombatFeatureSetup
    {
        [MenuItem("Tools/CPC/Apply Combat Features")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            foreach (SurvivalPlayer player in UnityEngine.Object.FindObjectsByType<SurvivalPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(player, "Combat balance");
                player.ShotDamage = 10f;
                player.HeadDamage = 20f;
                player.GetComponent<Health>().Configure(CombatTeam.Player, 100f);
                EditorUtility.SetDirty(player);
                EditorUtility.SetDirty(player.GetComponent<Health>());
            }
            foreach (ShieldCompanion companion in UnityEngine.Object.FindObjectsByType<ShieldCompanion>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(companion, "Combat balance");
                companion.MeleeDamage = 20f;
                companion.BlockDamageMultiplier = .2f;
                companion.GetComponent<Health>().Configure(CombatTeam.Companion, 200f);
                if (!companion.TryGetComponent<CpcSkills>(out _)) Undo.AddComponent<CpcSkills>(companion.gameObject);
                EditorUtility.SetDirty(companion);
                EditorUtility.SetDirty(companion.GetComponent<Health>());
            }
            SurvivalZombie[] zombies = UnityEngine.Object.FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SurvivalZombie zombie in zombies)
            {
                Undo.RecordObject(zombie, "Combat balance");
                zombie.Damage = 10f;
                zombie.GetComponent<Health>().Configure(CombatTeam.Monster, zombie.Kind == ZombieKind.Crawler ? 50f : zombie.Kind == ZombieKind.Normal || zombie.Kind == ZombieKind.Burrower ? 100f : 80f);
                EditorUtility.SetDirty(zombie);
                EditorUtility.SetDirty(zombie.GetComponent<Health>());
            }
            // 기존 모델과 애니메이터를 재사용하며 새 애니메이션은 요구하지 않는다.
            if (zombies.Length > 0)
            {
                const string FOLDER = "Assets/CpcPrototype/Survival/CombatPrefabs";
                if (!AssetDatabase.IsValidFolder(FOLDER)) AssetDatabase.CreateFolder("Assets/CpcPrototype/Survival", "CombatPrefabs");
                foreach (ZombieKind kind in Enum.GetValues(typeof(ZombieKind)))
                {
                    string path = FOLDER + "/" + kind + ".prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                    GameObject clone = UnityEngine.Object.Instantiate(zombies[0].gameObject);
                    clone.name = kind + " Zombie";
                    SurvivalZombie variant = clone.GetComponent<SurvivalZombie>();
                    variant.Kind = kind;
                    variant.RefreshAbility();
                    variant.Player = null;
                    variant.Companion = null;
                    PrefabUtility.SaveAsPrefabAsset(clone, path);
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }
}
