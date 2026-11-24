using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CpcPrototype.Editor
{
    public static class CpcTacticsSceneSetup
    {
        public const string TEST_SCENE = "Assets/Scenes/KayKit_TacticsTest.unity";
        [MenuItem("Tools/CPC/Install Extended Tactics")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            var player = UnityEngine.Object.FindFirstObjectByType<SurvivalPlayer>();
            if (player == null || player.Companion == null || player.Chat == null) throw new InvalidOperationException("Survival player/chat/CPC not found.");
            ShieldCompanion companion = player.Companion;
            if (companion.Tactics == null)
            {
                if (!companion.TryGetComponent<CpcTactics>(out var created)) created = Undo.AddComponent<CpcTactics>(companion.gameObject);
                companion.Tactics = created;
            }
            CpcTactics tactics = companion.Tactics;
            if (!companion.TryGetComponent<CpcThreatSensor>(out var sensor)) sensor = Undo.AddComponent<CpcThreatSensor>(companion.gameObject);
            if (!companion.TryGetComponent<CpcTacticalMotor>(out var motor)) motor = Undo.AddComponent<CpcTacticalMotor>(companion.gameObject);
            if (!companion.TryGetComponent<CpcCrowdProtection>(out _)) Undo.AddComponent<CpcCrowdProtection>(companion.gameObject);
            Undo.RecordObjects(new UnityEngine.Object[] { companion, tactics, sensor, motor }, "Connect extended CPC tactics");
            tactics.Companion = companion;
            tactics.AllowedActions |= CpcAction.Intercept | CpcAction.Evade | CpcAction.Counterattack | CpcAction.ProtectPlayer;
            tactics.EventInterval = .5f;
            sensor.Companion = companion;
            motor.Companion = companion; motor.Sensor = sensor;
            foreach (var item in new UnityEngine.Object[] { companion, tactics, sensor, motor }) EditorUtility.SetDirty(item);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        [MenuItem("Tools/CPC/Create Tactics Test Scene")]
        public static void CreateTestScene()
        {
            Install();
            Scene original = SceneManager.GetActiveScene();
            if (original.path == TEST_SCENE) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TEST_SCENE) != null)
                throw new InvalidOperationException("Test scene already exists; open it rather than overwriting it.");
            EditorSceneManager.SaveScene(original, TEST_SCENE, true);
            Scene scene = EditorSceneManager.OpenScene(TEST_SCENE);
            var player = UnityEngine.Object.FindFirstObjectByType<SurvivalPlayer>();
            var all = UnityEngine.Object.FindObjectsByType<SurvivalZombie>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var selected = new SurvivalZombie[3];
            ZombieKind[] kinds = { ZombieKind.Spitter, ZombieKind.Burrower, ZombieKind.Crawler };
            Vector3[] offsets = { new Vector3(1.5f, 0f, -.25f), new Vector3(.8f, 0f, .4f), new Vector3(1.1f, 0f, -.45f) };
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.SetActive(i < 3);
                if (i >= 3) continue;
                SurvivalZombie zombie = all[i];
                selected[i] = zombie;
                zombie.Kind = kinds[i]; zombie.RefreshAbility();
                zombie.name = "Tactics Test " + kinds[i];
                if (NavMesh.SamplePosition(player.transform.position + offsets[i], out NavMeshHit hit, .5f, NavMesh.AllAreas)) zombie.transform.position = hit.position;
                zombie.Player = player; zombie.Companion = player.Companion;
                zombie.DetectionRange = 8f; zombie.HomeLeash = 20f;
                if (zombie.TryGetComponent(out SpitterZombieAbility spit))
                { spit.AimDuration = 1.2f; spit.SpitInterval = 8f; spit.SpitRange = 5f; spit.SpitSpeed = 2f; spit.SpitSpawnHeight = .25f; spit.SpitTargetHeight = .25f; }
                if (zombie.TryGetComponent(out BurrowerZombieAbility burrow))
                { burrow.TriggerRange = .55f; burrow.BurrowInitialDelay = 5f; burrow.EmergeWarning = 1f; }
            }
            player.Companion.Monsters = selected;
            Install();
            EditorSceneManager.SaveScene(scene);
        }
    }
}
