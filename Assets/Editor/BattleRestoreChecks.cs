using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BattleRestoreChecks
{
    static int _passed;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool condition) { if (!condition) throw new Exception("Battle restore assertion failed."); }
    static void Test(string name, Action test) { test(); _passed++; Debug.Log("[BattleRestoreChecks] " + name); }
    static string Read(string path) => File.ReadAllText("Assets/Script/FunRabbit/" + path);
    static string Section(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Check(start >= 0 && end > start);
        return source.Substring(start, end - start);
    }

    [MenuItem("Tools/Release Safety/Check battle restoration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionOperation.IsBusy)
            throw new Exception("Use idle Edit Mode.");
        _passed = 0;
        var strings = new Dictionary<string, string>
        {
            { "AllySlotAnimalKey0", "bear" }, { "AllySlotAnimalKey1", "pig_b" },
            { "AllyPendingQueue", "bear,pig,cow,duck,frog,horse,koala,monk,panda,lion" }
        };
        var ints = new Dictionary<string, int> { { "AllySlotHp0", 131 }, { "AllySlotHp1", 237 } };
        string GetString(string key, string fallback) => strings.TryGetValue(key, out string value) ? value : fallback;
        int GetInt(string key, int fallback) => ints.TryGetValue(key, out int value) ? value : fallback;
        var snapshot = AllyBattleSaveSnapshot.Read(2, GetString, GetInt);

        Test("Two fighting allies and ten queued allies are captured in order", () =>
        {
            Check(snapshot.AnimalKeys.SequenceEqual(new[] { "bear", "pig_b" }));
            Check(snapshot.HitPoints.SequenceEqual(new[] { 131, 237 }));
            Check(snapshot.PendingAnimalKeys.Length == 10 && snapshot.PendingAnimalKeys[0] == "bear" &&
                snapshot.PendingAnimalKeys[9] == "lion");
        });
        Test("Reproduce the former first-spawn overwrite without losing the captured army", () =>
        {
            strings.Remove("AllySlotAnimalKey1");
            ints.Remove("AllySlotHp1");
            strings["AllyPendingQueue"] = "";
            var restored = snapshot.AnimalKeys.Where(k => !string.IsNullOrEmpty(k)).ToList();
            restored.AddRange(snapshot.PendingAnimalKeys);
            Check(restored.Count == 12 && restored[1] == "pig_b" && snapshot.HitPoints[1] == 237);
        });
        Test("Dead HP remains zero while a legacy missing HP is distinct", () =>
        {
            ints["AllySlotHp0"] = 0;
            var saved = AllyBattleSaveSnapshot.Read(2, GetString, GetInt);
            Check(saved.HitPoints[0] == 0 && saved.HitPoints[1] == AllyBattleSaveSnapshot.MissingHp);
        });
        Test("Empty saves and empty queue entries are handled", () =>
        {
            var empty = AllyBattleSaveSnapshot.Read(2, (k, d) => d, (k, d) => d);
            Check(empty.AnimalKeys.All(string.IsNullOrEmpty) && empty.PendingAnimalKeys.Length == 0);
            strings["AllyPendingQueue"] = ",bear,,pig,";
            Check(AllyBattleSaveSnapshot.Read(2, GetString, GetInt).PendingAnimalKeys.SequenceEqual(new[] { "bear", "pig" }));
        });
        Test("Repeated reads do not duplicate queue entries", () =>
        {
            var a = AllyBattleSaveSnapshot.Read(2, GetString, GetInt);
            var b = AllyBattleSaveSnapshot.Read(2, GetString, GetInt);
            Check(a.PendingAnimalKeys.SequenceEqual(b.PendingAnimalKeys) && b.PendingAnimalKeys.Length == 2);
        });
        Test("Cloud snapshot keys and army reader remain compatible", () =>
        {
            var cloud = new CloudSaveSnapshot { version = 1 };
            foreach (var pair in strings)
                cloud.strings.Add(new CloudSaveSnapshot.StringEntry { k = pair.Key, v = pair.Value });
            foreach (var pair in ints)
                cloud.ints.Add(new CloudSaveSnapshot.IntEntry { k = pair.Key, v = pair.Value });
            cloud.Validate();
            var restored = JsonUtility.FromJson<CloudSaveSnapshot>(JsonUtility.ToJson(cloud));
            restored.Validate();
            var army = AllyBattleSaveSnapshot.Read(2,
                (k, d) => restored.strings.FirstOrDefault(e => e.k == k)?.v ?? d, restored.GetInt);
            Check(army.HitPoints[0] == 0 && army.PendingAnimalKeys.Length == 2);
        });

        string battle = Read("Actor/ActorBattleSystem.cs");
        string quest = Read("GameQuestManager.cs");
        string hud = Read("UI/HUD/UIHud.cs");
        Test("Runtime restoration captures before clearing and suppresses writes until complete", () =>
        {
            string restore = Section(battle, "private void RestoreAllyBattleState()", "private void OnApplicationPause");
            Check(restore.IndexOf("AllyBattleSaveSnapshot.Read", StringComparison.Ordinal) <
                restore.IndexOf("ClearAllAllies();", StringComparison.Ordinal));
            Check(restore.IndexOf("_restoringState = true;", StringComparison.Ordinal) <
                restore.IndexOf("ClearAllAllies();", StringComparison.Ordinal));
            Check(restore.Contains("finally { _restoringState = false; }") && restore.Contains("saved.HitPoints[i] <= 0"));
            string save = Section(battle, "private void SaveAllyBattleState()", "private void RestoreAllyBattleState()");
            Check(save.Contains("if (_restoringState || _slotActors == null) return;"));
        });
        Test("Individual spawns no longer persist partially restored slots", () =>
        {
            string spawn = Section(battle, "private void SpawnAllyAtSlot(", "private void SaveAllyBattleState()");
            Check(!spawn.Contains("SaveAllyBattleState();"));
        });
        Test("Save reads live HP and lifecycle flushes avoid incomplete restoration", () =>
        {
            string save = Section(battle, "private void SaveAllyBattleState()", "private void RestoreAllyBattleState()");
            Check(save.Contains("_slotActors[i].Hp"));
            Check(battle.Contains("paused && _stateRestored && !SessionOperation.IsBusy") &&
                battle.Contains("private void OnApplicationQuit()"));
        });
        Test("An early cloud restore is not discarded by the later Start callback", () =>
        {
            string start = Section(battle, "private void Start()", "private void GrantPendingAllyRewards()");
            Check(start.Contains("if (_slotActors == null) InitSlots();") &&
                start.Contains("if (!_stateRestored) RestoreAllyBattleState();"));
        });
        Test("Loading and suspended sessions cannot fight or dequeue", () =>
        {
            Check(battle.Contains("CloudSaveManager.Instance.GameplayStarted"));
            Check(Read("Actor/BattleActor.cs").Contains("if (CanFight) base.Update();"));
            Check(Read("Actor/ActorAttackState.cs").Contains("!BattleActor.CanFight"));
            Check(Read("Actor/BossAttackState.cs").Contains("!BattleActor.CanFight"));
            Check(Section(battle, "private void TryDequeuePendingAlly()", "private void AddStackUIItem(")
                .Contains("if (!BattleActor.CanFight) return;"));
        });
        Test("Clear listener is subscribed during HUD initialization, not only after entering gameplay", () =>
        {
            Check(Section(hud, "public void Initialize(UIHud hud)", "public void Deinitialize()")
                .Contains("questManager.OnStageClear += OnStageClear;"));
            Check(!Section(hud, "private void OnChangedGameStatus(", "private void SubscribeCrane()")
                .Contains("OnStageClear +="));
        });
        Test("Stored zero-HP clears are retried after loading and cloud restore", () =>
        {
            Check(quest.Contains("private bool _checkRestoredClear = true;"));
            Check(Section(quest, "public void NotifyRestoredBossHp()", "private void Update()")
                .Contains("_checkRestoredClear = true;"));
            Check(quest.Contains("if (MaxBossHp > 0 && BossHp <= 0) QueueStageClear();"));
        });
        Test("A new paid pull cannot race the pending clear popup", () =>
        {
            string play = Section(hud, "public void OnClickPlayBtn()", "private void ShowCoinShortPopup()");
            Check(play.IndexOf("GameQuestManager.Instance.IsStageClear()", StringComparison.Ordinal) <
                play.IndexOf("PlayerContext.TrySpendCoin", StringComparison.Ordinal));
        });
        Test("Popup creation is required before advancing and READY deferral retains the duplicate guard", () =>
        {
            Check(Section(quest, "private bool FireStageClear()", "public bool IsStageClear()")
                .Contains("if (UIMissionClearPanel.Get() == null) return false;"));
            string waiting = Section(quest, "private System.Collections.IEnumerator FireStageClearWhenReady()", "private bool FireStageClear()");
            Check(waiting.Contains("crane.Status != CraneStatus.READY") && waiting.Contains("yield return null;"));
            Check(!waiting.Contains("_pendingStageClear = false"));
        });
        Test("Pending clear coroutine stays blocked during restore and cancels without firing", () =>
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var busy = typeof(SessionOperation).GetField("_count", PrivateStatic);
            object original = busy.GetValue(null);
            try
            {
                var go = new GameObject("Clear gate test", typeof(GameQuestManager));
                SceneManager.MoveGameObjectToScene(go, scene);
                var manager = go.GetComponent<GameQuestManager>();
                var pending = typeof(GameQuestManager).GetField("_pendingStageClear", PrivateInstance);
                pending.SetValue(manager, true);
                busy.SetValue(null, 1);
                Check(!ActorBattleSystem.CanAdvanceBattle && !BattleActor.CanFight);
                int fired = 0;
                manager.OnStageClear += _ => fired++;
                var coroutine = (IEnumerator)typeof(GameQuestManager)
                    .GetMethod("FireStageClearWhenReady", PrivateInstance).Invoke(manager, null);
                Check(coroutine.MoveNext() && coroutine.MoveNext() && fired == 0 && (bool)pending.GetValue(manager));
                manager.PrepareForCloudRestore();
                Check(!coroutine.MoveNext() && fired == 0);
            }
            finally
            {
                busy.SetValue(null, original);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        });
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/battle_restore_result.txt", "PASS: " + _passed +
            " checks; fake save dictionaries and isolated preview objects only; no player-save writes or network calls.\n");
        Debug.Log("BATTLE_RESTORE_CHECKS_PASSED=" + _passed);
    }
}
