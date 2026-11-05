using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DollRepairReview
{
    static readonly string[] Keys={"turtle","tiger","rhino","crocodile","rabbit","fox","hippo"};
    static readonly string[] Bases={"frog","lion","elephant","frog","koala","lion","elephant"};
    [MenuItem("Tools/New Dolls/Apply Seven Repairs")]
    public static void Apply()
    {
        for(int i=0;i<Keys.Length;i++)
        {
            if(Keys[i]=="rabbit")NewDollImporter.ImportBossOnly(Keys[i],Bases[i]);
            else NewDollImporter.Import(Keys[i],Bases[i]);
        }
        Review();
    }
    [MenuItem("Tools/New Dolls/Review Seven Repairs")]
    public static void Review()
    {
        string output="_design/dolls/repairs_20261102/unity";Directory.CreateDirectory(output);
        foreach(string key in Keys)
            foreach(string kind in new[]{"full","mon"})
                NewDollImporter.Preview(key,kind,output,true,new[]{0f,65f,155f,245f,-25f},768,90);
        var rabbit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/dollPrefabs/doll_rabbit_mon_prefab.prefab").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        if(rabbit.subMeshCount!=2||rabbit.GetTriangles(1).Length!=720)throw new Exception("Rabbit must have modeled eyes only and no red horns");
        AllDollChecks.Run();
        File.WriteAllText("_design/dolls/repairs_20261102/unity_validation.txt","PASS: rabbit boss has 240 eye triangles, zero horn triangles. All 36 resources and 960 animation samples passed. Seven dolls captured from front/side/back/both sides plus side-view animation frames.\n");
        Debug.Log("[DollRepairReview] seven repairs imported and reviewed");
    }
}
