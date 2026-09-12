using System;
using System.IO;
using System.Linq;
using FunRabbit;
using UnityEditor;
using UnityEngine;

public static class WaveBalanceChecks
{
    private static int _passed;
    private static void Require(bool condition)
    {
        if (!condition) throw new Exception("Wave balance assertion failed.");
    }

    private static void Test(string name, Action test)
    {
        test();
        _passed++;
        Debug.Log("[WaveBalanceChecks] " + name);
    }

    [MenuItem("Tools/Gameplay Balance/Run wave balance checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _passed = 0;
        var before = JsonUtility.FromJson<ActorDataList>(File.ReadAllText("_report/2026-09-12_wave_balance_before.json"));
        var expected = JsonUtility.FromJson<ActorDataList>(File.ReadAllText("_report/2026-09-12_wave_balance_candidate.json"));
        GameActorData.Load();
        var actors = GameActorData.Actors;
        Test("All 36 calibrated rows are loaded by Unity", () =>
        {
            Require(actors.Count == 36 && actors.Select(a => a.stage).Distinct().Count() == 36);
            for (int i = 0; i < actors.Count; i++)
                Require(JsonUtility.ToJson(actors[i]) == JsonUtility.ToJson(expected.actors[i]));
        });
        Test("Stages 1-3 are completely unchanged, including clear rewards", () =>
        {
            for (int i = 0; i < 3; i++)
            {
                Require(JsonUtility.ToJson(actors[i]) == JsonUtility.ToJson(before.actors[i]));
                Require(GameActorData.GetClearCoinReward(actors[i].animalKey) == 500);
            }
        });
        Test("Ally stats, identities, models and all other fields are unchanged", () =>
        {
            for (int i = 0; i < actors.Count; i++)
            {
                var copy = JsonUtility.FromJson<ActorData>(JsonUtility.ToJson(actors[i]));
                var original = before.actors[i];
                copy.bossHp = original.bossHp;
                copy.bossAttackPower = original.bossAttackPower;
                copy.bossAttackSpeed = original.bossAttackSpeed;
                copy.clearCoinReward = original.clearCoinReward;
                Require(JsonUtility.ToJson(copy) == JsonUtility.ToJson(original));
            }
        });
        Test("Clear rewards are bounded and crest stages include bonuses", () =>
        {
            Require(GameActorData.GetClearCoinReward("unknown") == 500);
            Require(GameActorData.GetClearCoinReward("bear_g") == GameActorData.GetClearCoinReward("bear_b"));
            foreach (var a in actors.Where(a => a.stage >= 4))
                Require(a.clearCoinReward >= 500 && a.clearCoinReward <= 2000 && a.clearCoinReward % 100 == 0);
            foreach (int stage in new[] { 9, 20, 36 })
                Require(actors[stage - 1].clearCoinReward > actors[stage - 2].clearCoinReward);
        });
        Test("Legacy max HP lookup matches every row of the pre-change table", () =>
        {
            foreach (var original in before.actors)
                Require(BossHpBalance.ResolvePreviousMax(original.stage, 0, 999999) == original.bossHp);
        });
        Test("Saved scale overrides legacy data and unknown stages use fallback", () =>
        {
            Require(BossHpBalance.ResolvePreviousMax(13, 20000, 30000) == 20000);
            Require(BossHpBalance.ResolvePreviousMax(100, 0, 30000) == 30000);
        });
        Test("Tier boundaries preserve half-cleared boss progress", () =>
        {
            Require(BossHpBalance.RescaleRemaining(410, 820, actors[12].bossHp) == (actors[12].bossHp + 1) / 2);
            Require(BossHpBalance.RescaleRemaining(615, 1230, actors[24].bossHp) == (actors[24].bossHp + 1) / 2);
        });
        Test("Cleared bosses remain cleared and a living boss cannot round to zero", () =>
        {
            Require(BossHpBalance.RescaleRemaining(0, 100, 10000) == 0);
            Require(BossHpBalance.RescaleRemaining(-5, 100, 10000) == 0);
            Require(BossHpBalance.RescaleRemaining(1, 10000, 100) == 1);
        });
        Test("HP rescale handles downscales, out-of-range and maximum integers", () =>
        {
            Require(BossHpBalance.RescaleRemaining(50, 100, 20) == 10);
            Require(BossHpBalance.RescaleRemaining(200, 100, 20) == 20);
            Require(BossHpBalance.RescaleRemaining(int.MaxValue, int.MaxValue, int.MaxValue) == int.MaxValue);
            Require(BossHpBalance.RescaleRemaining(100, 0, 20) == 20);
            Require(BossHpBalance.RescaleRemaining(100, 100, 0) == 0);
        });
        Test("Applying the same HP scale repeatedly is idempotent", () =>
        {
            int migrated = BossHpBalance.RescaleRemaining(1, 820, actors[12].bossHp);
            Require(BossHpBalance.RescaleRemaining(migrated, actors[12].bossHp, actors[12].bossHp) == migrated);
        });
        Test("Old cloud snapshots remain valid without maximum HP metadata", () =>
        {
            var snapshot = new CloudSaveSnapshot { version = 1 };
            snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "currentStage", v = 13 });
            snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "bossHp", v = 410 });
            snapshot.Validate();
            Require(!snapshot.TryGetInt("bossHpMax", out _));
        });
        Test("Cloud maximum HP metadata survives JSON round trip", () =>
        {
            var snapshot = new CloudSaveSnapshot { version = 1 };
            snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "bossHpMax", v = 820 });
            var copy = JsonUtility.FromJson<CloudSaveSnapshot>(JsonUtility.ToJson(snapshot));
            copy.Validate();
            Require(copy.GetInt("bossHpMax", 0) == 820);
        });
        Test("Invalid cloud maximum HP is rejected", () =>
        {
            foreach (int invalid in new[] { 0, -1 })
            {
                var snapshot = new CloudSaveSnapshot { version = 1 };
                snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "bossHpMax", v = invalid });
                bool rejected = false;
                try { snapshot.Validate(); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        });
        Test("Mission target exclusion stays uniform and within the recent pool", () =>
        {
            for (int min = 1; min <= 30; min++)
            {
                int max = min + 5;
                for (int previous = min; previous <= max; previous++)
                {
                    var selected = Enumerable.Range(0, 5).Select(i => MissionSystem.SelectTargetStage(min, max, previous, i)).ToArray();
                    Require(selected.Distinct().Count() == 5);
                    Require(selected.All(s => s >= min && s <= max && s != previous));
                }
            }
        });
        Test("Mission target selector supports no prior target and one candidate", () =>
        {
            Require(MissionSystem.SelectTargetStage(5, 5, 5, 0) == 5);
            for (int i = 0; i < 6; i++)
                Require(MissionSystem.SelectTargetStage(10, 15, 0, i) == 10 + i);
        });
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/wave_balance_result.txt", "PASS: " + _passed + " checks; no player-save writes or network calls.\n");
        Debug.Log("WAVE_BALANCE_CHECKS_PASSED=" + _passed);
    }
}
