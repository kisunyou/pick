using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LapThreeDollReview
{
    public static readonly string[] Keys = { "deer", "capybara", "dog", "jellyfish", "owl", "crocodile", "llama", "rabbit", "sloth", "raccoon", "fox", "hippo" };
    public static readonly string[] Bases = { "cow", "pig", "bear", "octopus", "duck", "frog", "horse", "koala", "monk", "panda", "lion", "elephant" };
    [Serializable] class Request { public string[] keys; public bool ui; }

    [MenuItem("Tools/New Dolls/Import Requested Lap Three")]
    public static void ImportRequested()
    {
        var request = JsonUtility.FromJson<Request>(File.ReadAllText("_design/dolls/import_request_lap3.json"));
        string output = "_design/dolls/rig/_unity_preview";
        Directory.CreateDirectory(output);
        foreach (string key in request.keys)
        {
            int i = Array.IndexOf(Keys, key);
            if (i < 0) throw new Exception("Not a stage 25-36 doll: " + key);
            NewDollImporter.Import(key, Bases[i]);
            if (request.ui) NewDollImporter.ImportUi(key, Bases[i]);
            foreach (string kind in new[] { "full", "mon" }) NewDollImporter.Preview(key, kind, output);
            File.AppendAllText("_design/dolls/logs/lap3_import_progress.txt", key + " model=" + DateTime.Now.ToString("HH:mm:ss") + " ui=" + request.ui + "\n");
        }
        Debug.Log("[LapThreeDollReview] requested dolls imported and captured");
    }

    [MenuItem("Tools/New Dolls/Import Lap Three UI")]
    public static void ImportUi()
    {
        for (int i = 0; i < Keys.Length; i++) NewDollImporter.ImportUi(Keys[i], Bases[i]);
        Debug.Log("[LapThreeDollReview] lap three UI imported");
    }

    [MenuItem("Tools/New Dolls/Finalize Lap Three")]
    public static void FinalizeLapThree()
    {
        ImportUi();
        AllDollChecks.Run();
        LapTwoCollectionPreview.Capture(30, new[] { 25, 31 });
        Debug.Log("[LapThreeDollReview] COMPLETE stages 25-36");
    }

    [MenuItem("Tools/New Dolls/Capture All 36")]
    public static void CaptureAll()
    {
        FunRabbit.GameActorData.Load();
        string output = "_design/dolls/rig/_unity_preview";
        foreach (var row in FunRabbit.GameActorData.Actors.OrderBy(r => r.stage))
            NewDollImporter.Preview(row.animalKey, "full", output, false);
        Debug.Log("[LapThreeDollReview] all 36 models captured");
    }
}
