using System;
using System.Collections.Generic;
using FunRabbit;
using UnityEditor;
using UnityEngine;

// Pure-data regressions: does not read/write player prefs, contact Firebase or buy.
public static class ReleaseSafetyChecks
{
    static int _passed;
    [MenuItem("Tools/Release Safety/Build Android validation APK")]
    public static void RequestAndroidBuild()
    {
        EditorApplication.update -= RunScheduledBuild;
        EditorApplication.update += RunScheduledBuild;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    static void RunScheduledBuild()
    {
        EditorApplication.update -= RunScheduledBuild;
        BuildAndroid();
    }

    static void BuildAndroid()
    {
        const string resultPath = "Logs/release_safety_build_result.txt";
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Validation build requires Edit Mode and the Android target.");
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);
            // Uses on-disk scenes; never saves the user's open scene or exits their editor.
            var options = new BuildPlayerOptions { scenes = scenes.ToArray(),
                locationPathName = "build/pick_release_safety_validation.apk", target = BuildTarget.Android,
                options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            System.IO.File.WriteAllText(resultPath, $"{summary.result}; errors={summary.totalErrors}; output={summary.outputPath}");
        }
        catch (Exception e)
        {
            System.IO.File.WriteAllText(resultPath, "FAIL: " + e);
            Debug.LogException(e);
        }
    }
    const string RequestFile = ".utmp/run_release_safety_checks";
    [InitializeOnLoadMethod]
    static void CheckRequestedRun()
    {
        if (!System.IO.File.Exists(RequestFile)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            System.IO.File.Delete(RequestFile);
            Run();
        };
    }

    [MenuItem("Tools/Release Safety/Run regression checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Release safety checks require Edit Mode.");
            return;
        }
        _passed = 0;
        try
        {
            Test("Replayed purchase after restart preserves spent balance", () =>
            {
                var wallet = new CoinWallet { balance = 2000 };
                Require(wallet.Grant("order-1", 10000));
                wallet.balance -= 300;
                var restored = CoinWallet.Parse(JsonUtility.ToJson(wallet));
                Require(!restored.Grant("order-1", 10000));
                Require(restored.balance == 11700 && restored.transactions.Count == 1);
                Require(restored.Grant("order-2", 10000) && restored.balance == 21700);
            });
            Test("Missing transaction cannot grant", () =>
            {
                var wallet = new CoinWallet { balance = 2000 };
                Throws(() => wallet.Grant("", 10000));
                Require(wallet.balance == 2000 && wallet.transactions.Count == 0);
            });
            Test("Overflow leaves both balance and ledger unchanged", () =>
            {
                var wallet = new CoinWallet { balance = long.MaxValue };
                Throws(() => wallet.Grant("overflow", 1));
                Require(wallet.balance == long.MaxValue && wallet.transactions.Count == 0);
            });
            Test("Large balances survive serialization", () =>
            {
                var wallet = new CoinWallet { balance = (long)int.MaxValue + 10000 };
                Require(CoinWallet.Parse(JsonUtility.ToJson(wallet)).balance == wallet.balance);
            });
            Test("V1 save remains migratable", () =>
            {
                new CloudSaveSnapshot { version = 1 }.Validate();
            });
            Test("V2 must carry purchase history", () => Throws(() => new CloudSaveSnapshot().Validate()));
            Test("Cloud snapshot roundtrip preserves transaction and coins", () =>
            {
                var wallet = new CoinWallet { balance = 2000 };
                wallet.Grant("pending-order", 50000);
                var snapshot = new CloudSaveSnapshot();
                snapshot.strings.Add(new CloudSaveSnapshot.StringEntry { k = CoinWallet.PrefsKey, v = JsonUtility.ToJson(wallet) });
                var restored = JsonUtility.FromJson<CloudSaveSnapshot>(JsonUtility.ToJson(snapshot));
                restored.Validate();
                var restoredWallet = CoinWallet.Parse(restored.strings[0].v);
                Require(!restoredWallet.Grant("pending-order", 50000) && restoredWallet.balance == 52000);
            });
            Test("Duplicate snapshot fields are rejected before apply", () =>
            {
                var snapshot = new CloudSaveSnapshot { version = 1 };
                snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "bossHp", v = 1 });
                snapshot.ints.Add(new CloudSaveSnapshot.IntEntry { k = "bossHp", v = 2 });
                Throws(snapshot.Validate);
            });
            Test("Corrupted purchase history is not silently reset", () =>
                Throws(() => CoinWallet.Parse("{\"version\":2,\"balance\":100,\"transactions\":null}")));
            Test("Unmanaged snapshot fields cannot overwrite account identity", () =>
            {
                var snapshot = new CloudSaveSnapshot { version = 1 };
                snapshot.strings.Add(new CloudSaveSnapshot.StringEntry { k = "CloudSave_LocalOwner", v = "other" });
                Throws(snapshot.Validate);
            });
            Test("Cancel completes once and invalidates late restore; gameplay cannot restart restore", () =>
            {
                var go = new GameObject("ReleaseSafety cancellation test");
                try
                {
                    var cloud = go.AddComponent<CloudSaveManager>();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var version = typeof(CloudSaveManager).GetField("_generation", flags);
                    var callback = typeof(CloudSaveManager).GetField("_syncDone", flags);
                    var current = typeof(CloudSaveManager).GetMethod("Current", flags);
                    int completed = 0;
                    callback.SetValue(cloud, (Action)(() => completed++));
                    int oldGeneration = (int)version.GetValue(cloud);
                    Require((bool)current.Invoke(cloud, new object[] { oldGeneration, null }));
                    cloud.CancelLoginSync();
                    cloud.CancelLoginSync();
                    Require(completed == 1 && !(bool)current.Invoke(cloud, new object[] { oldGeneration, null }));
                    cloud.MarkGameplayStarted();
                    cloud.SyncOnLogin(() => completed++);
                    Require(completed == 2 && !cloud.IsSyncing);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    CloudSaveManager.ClearVariablesSingleton();
                }
            });
            Debug.Log($"RELEASE_SAFETY_CHECKS_PASSED={_passed}");
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/release_safety_result.txt", $"PASS: {_passed} regression checks");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/release_safety_result.txt", "FAIL: " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
    static void Test(string name, Action test) { test(); _passed++; Debug.Log("[ReleaseSafety] " + name); }
    static void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Throws(Action action)
    {
        try { action(); } catch { return; }
        throw new Exception("Expected rejection");
    }
}
