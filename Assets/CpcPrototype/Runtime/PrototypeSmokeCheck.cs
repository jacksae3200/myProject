#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CpcPrototype
{
    // 에디터 플레이 모드에서만 명시적으로 추가하는 통합 검증 도구. 빌드에는 포함되지 않는다.
    public sealed class PrototypeSmokeCheck : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string Name; public bool Passed; public string Detail; }
        [Serializable] public sealed class Report { public List<Check> Checks = new List<Check>(); public bool Completed; }
        private Keyboard _keyboard;
        private Mouse _mouse;
        private readonly Report _report = new Report();
        public string Result => JsonUtility.ToJson(_report, true);
        public bool Completed => _report.Completed;
        private IEnumerator Start()
        {
            ThirdPersonPlayer player = FindFirstObjectByType<ThirdPersonPlayer>();
            CompanionCpc companion = FindFirstObjectByType<CompanionCpc>();
            CubeMonsterBrain[] monsters = FindObjectsByType<CubeMonsterBrain>(FindObjectsSortMode.None);
            foreach (CubeMonsterBrain monster in monsters) monster.enabled = false;
            Health playerHealth = player.GetComponent<Health>();
            _keyboard = InputSystem.AddDevice<Keyboard>(); _mouse = InputSystem.AddDevice<Mouse>();
            yield return new WaitForSeconds(0.3f);
            Vector3 initial = player.transform.position;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.08f);
            float walk = Vector3.Distance(initial, player.transform.position);
            Record("WASD movement", walk > 1.3f && walk < 2.8f, "distance=" + walk);
            initial = player.transform.position;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.08f);
            float run = Vector3.Distance(initial, player.transform.position);
            Record("Shift run", run > walk * 1.4f, "walk=" + walk + ", run=" + run);
            yield return new WaitForSeconds(0.5f);
            float playerY = player.transform.position.y;
            float cpcY = companion.transform.position.y;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space));
            yield return new WaitForSeconds(0.16f);
            Record("Space jump", player.transform.position.y > playerY + 0.25f, "height=" + (player.transform.position.y - playerY));
            Record("CPC synchronized jump", companion.transform.position.y > cpcY + 0.25f, "height=" + (companion.transform.position.y - cpcY));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSeconds(1f);
            CubeMonsterBrain target = monsters[0];
            Teleport(target.gameObject, player.transform.position + player.transform.forward * 1.2f);
            float before = target.Health.Current;
            InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 1 });
            yield return new WaitForSeconds(0.12f);
            InputSystem.QueueStateEvent(_mouse, new MouseState());
            Record("Left click melee", target.Health.Current == before - 25f, "before=" + before + ", after=" + target.Health.Current);
            yield return new WaitForSeconds(0.5f);
            Record("CPC follow", Vector3.Distance(player.transform.position, companion.transform.position) < 3.3f, "distance=" + Vector3.Distance(player.transform.position, companion.transform.position));
            // 원래 자리와 떨어진 곳에서 시작하여 추적 → 이탈 → 복귀 우선순위를 검증한다.
            Teleport(target.gameObject, target.Home);
            Teleport(player.gameObject, target.Home - Vector3.forward * 5f);
            Teleport(companion.gameObject, target.Home - Vector3.forward * 7f + Vector3.right * 2f);
            target.enabled = true;
            yield return new WaitForSeconds(0.35f);
            Record("BT chase", target.State == CubeMonsterBrain.MonsterState.Chase, target.State.ToString());
            Teleport(player.gameObject, target.Home - Vector3.forward * 24f);
            Teleport(companion.gameObject, target.Home - Vector3.forward * 25f);
            yield return new WaitForSeconds(0.12f);
            Record("BT return", target.State == CubeMonsterBrain.MonsterState.Return, target.State.ToString());
            yield return new WaitForSeconds(2f);
            Vector3 homeDelta = target.transform.position - target.Home; homeDelta.y = 0f;
            Record("BT home idle", target.State == CubeMonsterBrain.MonsterState.Idle && homeDelta.magnitude < 0.35f, target.State + ", home distance=" + homeDelta.magnitude);
            Teleport(player.gameObject, target.transform.position - Vector3.forward * 1.2f);
            before = playerHealth.Current;
            yield return new WaitForSeconds(0.4f);
            Record("BT attack / player damage", target.State == CubeMonsterBrain.MonsterState.Attack && playerHealth.Current < before, "state=" + target.State + ", HP=" + playerHealth.Current);
            float cpcBefore = companion.Health.Current;
            companion.Health.TakeDamage(5f, target.gameObject);
            Record("CPC damage", companion.Health.Current == cpcBefore - 5f, "HP=" + companion.Health.Current);
            target.Health.TakeDamage(1000f, player.gameObject);
            yield return new WaitForSeconds(0.1f);
            Record("Monster death", !target.Health.IsAlive && target.State == CubeMonsterBrain.MonsterState.Dead, target.State.ToString());
            before = playerHealth.Current;
            yield return new WaitForSeconds(1.5f);
            Record("Dead monster stops attacking", playerHealth.Current == before, "HP=" + playerHealth.Current);
            playerHealth.TakeDamage(1000f, target.gameObject);
            initial = player.transform.position;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(0.25f);
            Record("Player death stops movement", !playerHealth.IsAlive && Vector3.Distance(initial, player.transform.position) < 0.1f, "HP=" + playerHealth.Current);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            _report.Completed = true;
            Directory.CreateDirectory("Screenshots");
            File.WriteAllText("Screenshots/CpcSmokeReport.json", Result);
            Debug.Log("[CPC/Smoke] COMPLETE " + Result, this);
            CleanupDevices();
        }
        private void Record(string name, bool passed, string detail)
        {
            _report.Checks.Add(new Check { Name = name, Passed = passed, Detail = detail });
            if (passed) Debug.Log("[CPC/Smoke] PASS " + name + " / " + detail, this);
            else Debug.LogError("[CPC/Smoke] FAIL " + name + " / " + detail, this);
        }
        private static void Teleport(GameObject character, Vector3 position)
        {
            CharacterController controller = character.GetComponent<CharacterController>();
            controller.enabled = false; character.transform.position = position; controller.enabled = true;
            Physics.SyncTransforms();
        }
        private void CleanupDevices()
        {
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
        }
        private void OnDisable() { StopAllCoroutines(); CleanupDevices(); }
    }
}
#endif
