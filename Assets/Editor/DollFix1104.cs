using System.IO;
using UnityEditor;
using UnityEngine;

// 2026-11-04 인형 보정: 거북이 등껍질/머리 뒤, 호랑이 얼굴 재가져오기 + 텍스처만 바꾼 멧돼지·코뿔소 미리보기
public static class DollFix1104
{
    static readonly string[] Reimport = { "turtle", "tiger" };
    static readonly string[] ReimportBases = { "frog", "lion" };
    static readonly string[] Review = { "turtle", "tiger", "boar", "rhino" };

    [MenuItem("Tools/New Dolls/Apply 1104 Fixes")]
    public static void Apply()
    {
        for (int i = 0; i < Reimport.Length; i++) NewDollImporter.Import(Reimport[i], ReimportBases[i]);
        AssetDatabase.Refresh();
        string output = "_design/dolls/repairs_20261104/unity"; Directory.CreateDirectory(output);
        foreach (string key in Review)
            foreach (string kind in new[] { "full", "mon" })
                NewDollImporter.Preview(key, kind, output, true, new[] { 0f, 65f, 155f, 245f, -25f }, 768, 90);
        AllDollChecks.Run();
        Debug.Log("[DollFix1104] applied and reviewed");
    }
}
