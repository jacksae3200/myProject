#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype.Editor
{
    public sealed class CpcCrowdProtectionChecks : MonoBehaviour
    {
        public static string Result { get; private set; } = "Not run";
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<string> _passed = new List<string>();
        private readonly List<SurvivalZombie> _spawned = new List<SurvivalZombie>();
        private SurvivalPlayer _player;
        private ShieldCompanion _cpc;
        private CpcCrowdProtection _crowd;
        private CpcLlmSettings _original, _settings;
        private GameObject _wall;
        [MenuItem("Tools/CPC/Check Crowd Protection (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/KayKit_TacticsTest.unity")
                throw new Exception("Use Play mode in KayKit_TacticsTest.");
            var runner = new GameObject("Crowd Protection Checks").AddComponent<CpcCrowdProtectionChecks>();
            runner.StartCoroutine(runner.Execute());
        }
        private IEnumerator Execute()
        {
            Result = "Running";
            IEnumerator checks = Checks();
            while (true)
            {
                bool more; object next = null;
                try { more = checks.MoveNext(); if (more) next = checks.Current; }
                catch (Exception exception) { Result = "FAIL: " + exception; break; }
                if (!more) { Result = "PASS"; break; }
                yield return next;
            }
            foreach (var zombie in _spawned) if (zombie != null) Destroy(zombie.gameObject);
            if (_wall != null) Destroy(_wall);
            if (_cpc != null) _cpc.Llm.Configure(_original);
            if (_settings != null) Destroy(_settings);
            Result += "\n" + string.Join("\n", _passed);
            System.IO.Directory.CreateDirectory("Tools/Tactics");
            System.IO.File.WriteAllText("Tools/Tactics/CrowdProtectionChecks.txt", Result);
            Debug.Log("[CPC/CrowdChecks] " + Result);
            Destroy(gameObject);
        }
        private IEnumerator Checks()
        {
            _player = FindFirstObjectByType<SurvivalPlayer>(); _cpc = _player.Companion; _crowd = _cpc.CrowdProtection;
            Check(_crowd != null, "crowd protection wired");
            _cpc.Llm.enabled = false; _cpc.Llm.enabled = true;
            _original = _cpc.Llm.Settings; _settings = Instantiate(_original); _settings.EnableLlm = false;
            _cpc.Llm.Configure(_settings);
            _cpc.Tactics.OnThreat = _cpc.Tactics.OnDamage = false; _cpc.Tactics.UseAutonomousFallback = false;
            _cpc.Tactics.ReleasePlayerMovementOrder();
            _crowd.UseTaunt = false;
            _player.Health.Configure(CombatTeam.Player, 100f); _cpc.HitPoints.Configure(CombatTeam.Companion, 200f);
            foreach (var zombie in SurvivalZombie.Active.ToArray()) zombie.gameObject.SetActive(false);
            Vector3 player = _player.transform.position;
            Vector3 anchor = player - Vector3.right;
            _cpc.GetComponent<NavMeshAgent>().Warp(anchor);
            int generation = (int)typeof(CpcTactics).GetField("_chatGeneration", PRIVATE).GetValue(_cpc.Tactics);
            typeof(CpcTactics).GetMethod("Decode", PRIVATE).Invoke(_cpc.Tactics, new object[]
            { "{\"action\":\"hold\",\"targetId\":\"\",\"speech\":\"기다릴게\"}", new bool[0], generation, Time.unscaledTime, true, false });
            Spawn(player + new Vector3(.45f, 0f, 0f));
            Spawn(player + new Vector3(.65f, 0f, .25f));
            yield return null; yield return null;
            _crowd.Refresh(true);
            Check(_crowd.NearbyCount == 2 && !_crowd.IsProtecting, "two enemies stay below three-enemy threshold");
            Spawn(player + new Vector3(.8f, 0f, -.25f));
            yield return new WaitForSecondsRealtime(1.5f);
            Check(_crowd.NearbyCount == 3 && _crowd.IsProtecting && _cpc.IsBlocking &&
                Vector3.Distance(_cpc.transform.position, _player.transform.position) < .4f &&
                Vector3.Dot(_cpc.transform.forward, Vector3.right) > .7f,
                "three enemies trigger real NavMesh return, crowd-facing shield and preserved Hold");
            Check(_cpc.Tactics.IsHolding, "emergency guard suspends rather than replaces player directive");
            SurvivalZombie attacker = _spawned[0];
            attacker.AttackRange = .5f; attacker.AttackWindup = .15f; attacker.AttackCooldown = 999f; attacker.Damage = 10f;
            attacker.ReactToShot(_player);
            typeof(SurvivalZombie).GetField("_nextAttack", PRIVATE).SetValue(attacker, Time.time);
            float playerHealth = _player.Health.Current, cpcHealth = _cpc.HitPoints.Current;
            yield return new WaitForSecondsRealtime(.4f);
            Check(_player.Health.Current == playerHealth && Mathf.Abs(cpcHealth - _cpc.HitPoints.Current - 2f) < .01f,
                "real player-targeted melee is intercepted: player loses 0, CPC loses 2 of 10");
            _cpc.HitPoints.Configure(CombatTeam.Companion, 200f);
            _cpc.HitPoints.TakeDamage(150f, _player.gameObject); yield return null; yield return null;
            Check(_cpc.IsRetreating && !_crowd.IsProtecting && _cpc.Tactics.IsHolding, "low-health retreat outranks crowd guard and retains directive");
            _cpc.HitPoints.Heal(200f); yield return new WaitForSecondsRealtime(.3f);
            Check(!_cpc.IsRetreating && _crowd.IsProtecting, "crowd guard can resume after health recovery");
            _crowd.enabled = false; yield return null; yield return null;
            Check(!_crowd.IsProtecting, "disabled component stops automatic crowd protection");
            _crowd.enabled = true; yield return new WaitForSecondsRealtime(.2f);
            _cpc.GetComponent<NavMeshAgent>().enabled = false; yield return null; yield return null;
            Check(!_crowd.IsProtecting, "lost navigation cancels crowd state without movement");
            _cpc.GetComponent<NavMeshAgent>().enabled = true;
            _cpc.GetComponent<NavMeshAgent>().Warp(player + Vector3.right * .22f);
            yield return null; yield return null;
            _spawned[1].gameObject.SetActive(false);
            _spawned[2].gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(!_crowd.IsProtecting && _cpc.Tactics.IsHolding && Vector3.Distance(_cpc.transform.position, anchor) < .15f,
                "pressure clears with hysteresis and CPC returns to original Hold anchor");
            foreach (var zombie in _spawned) zombie.gameObject.SetActive(false);
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = player + new Vector3(.25f, .25f, 0f);
            _wall.transform.localScale = new Vector3(.06f, 1f, 2f);
            foreach (var zombie in _spawned) zombie.gameObject.SetActive(true);
            yield return null; yield return null;
            _crowd.Refresh(true);
            Check(_crowd.NearbyCount == 0 && !_crowd.IsProtecting, "wall-occluded enemies do not cause hidden crowd detection");
            Check(!_cpc.GetComponent<CpcTacticalMotor>().TryMoveToProtectionPoint(new Vector3(10000f, 0f, 10000f), .07f),
                "unreachable protection destination rejected");
        }
        private void Spawn(Vector3 point)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CpcPrototype/Survival/CombatPrefabs/Normal.prefab");
            GameObject clone = Instantiate(prefab, point, Quaternion.identity);
            SurvivalZombie zombie = clone.GetComponent<SurvivalZombie>();
            zombie.name = "Crowd Check " + _spawned.Count;
            zombie.MoveSpeed = 0f; zombie.AttackRange = 0f; zombie.Damage = 0f;
            zombie.Player = _player; zombie.Companion = _cpc;
            zombie.GetComponent<NavMeshAgent>().Warp(point);
            _spawned.Add(zombie);
        }
        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message + " count=" + (_crowd != null ? _crowd.NearbyCount : -1));
            _passed.Add(message); Result = "Running\n" + string.Join("\n", _passed);
        }
    }
}
#endif
