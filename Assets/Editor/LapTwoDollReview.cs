using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LapTwoDollReview
{
    public static readonly string[] Keys = { "sheep", "boar", "polarbear", "squid", "penguin", "turtle", "zebra", "hamster", "gorilla", "redpanda", "tiger", "rhino" };
    public static readonly string[] Bases = { "cow", "pig", "bear", "octopus", "duck", "frog", "horse", "koala", "monk", "panda", "lion", "elephant" };
    [Serializable] class Request { public string[] keys; public bool ui; }

    [MenuItem("Tools/New Dolls/Import Requested Lap Two")]
    public static void ImportRequested()
    {
        var request = JsonUtility.FromJson<Request>(File.ReadAllText("_design/dolls/import_request.json"));
        string output = "_design/dolls/rig/_unity_preview";
        Directory.CreateDirectory(output);
        foreach (string key in request.keys)
        {
            int i = Array.IndexOf(Keys, key);
            if (i < 0) throw new Exception("Not a stage 13-24 doll: " + key);
            NewDollImporter.Import(key, Bases[i]);
            if (request.ui) NewDollImporter.ImportUi(key, Bases[i]);
            foreach (string kind in new[] { "full", "mon" }) NewDollImporter.Preview(key, kind, output);
            File.AppendAllText("_design/dolls/logs/lap2_import_progress.txt", key + " model=" + DateTime.Now.ToString("HH:mm:ss") + " ui=" + request.ui + "\n");
        }
        Debug.Log("[LapTwoDollReview] requested dolls imported and captured");
    }

    [MenuItem("Tools/New Dolls/Import Lap Two UI")]
    public static void ImportUi()
    {
        for (int i = 1; i < Keys.Length; i++) NewDollImporter.ImportUi(Keys[i], Bases[i]);
        Debug.Log("[LapTwoDollReview] lap two UI imported");
    }

    [MenuItem("Tools/New Dolls/Finalize Lap Two")]
    public static void FinalizeLapTwo()
    {
        NewDollImporter.Import("rhino", "elephant");
        foreach (string kind in new[] { "full", "mon" }) NewDollImporter.Preview("rhino", kind, "_design/dolls/rig/_unity_preview");
        ImportUi();
        LapTwoDollChecks.Run();
        LapTwoCollectionPreview.Run();
        Debug.Log("[LapTwoDollReview] COMPLETE stages 13-24");
    }
}
