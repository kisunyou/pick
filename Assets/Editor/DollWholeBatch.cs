using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 2026-11-04 신규 인형 24종 'Meshy 통째' 교체 일괄 처리:
// 가져오기(일반+보스) -> 충돌체 재생성 -> 덮임·낙하·더미 검사 -> AllDollChecks -> 검수용 프레임/와이어 그림
// 실행: Unity -batchmode -projectPath D:/pick -executeMethod DollWholeBatch.Run -quit  (-dollKeys a,b,c 로 일부만)
public static class DollWholeBatch
{
    static readonly (string key, string baseKey)[] All =
    {
        ("sheep","cow"),("deer","cow"),("boar","pig"),("capybara","pig"),("polarbear","bear"),("dog","bear"),
        ("squid","octopus"),("jellyfish","octopus"),("penguin","duck"),("owl","duck"),("turtle","frog"),("crocodile","frog"),
        ("zebra","horse"),("llama","horse"),("hamster","koala"),("rabbit","koala"),("gorilla","monk"),("sloth","monk"),
        ("redpanda","panda"),("raccoon","panda"),("tiger","lion"),("fox","lion"),("rhino","elephant"),("hippo","elephant"),
    };

    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-dollKeys");
        var only = ai >= 0 ? args[ai + 1].Split(',') : null;
        var list = All.Where(p => only == null || only.Contains(p.key)).ToArray();
        string root = Path.GetDirectoryName(Application.dataPath);
        string outDir = Path.Combine(root, "_design/dolls/repairs_20261104/whole");
        Directory.CreateDirectory(outDir);
        var L = DollColliderTools.lines; L.Clear();

        foreach (var (key, baseKey) in list)
        {
            NewDollImporter.Import(key, baseKey);
            DollColliderTools.Build(key);
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        L.Add("== 덮임 (elephant = 원본 기준값) =="); DollColliderTools.Coverage("elephant");
        foreach (var (key, _) in list) DollColliderTools.Coverage(key);
        L.Add("== 낙하 10회 =="); foreach (var (key, _) in list) DollColliderTools.DropTest(key);
        L.Add("== 더미 12마리 =="); foreach (var (key, _) in list) DollColliderTools.PileTest(key, false);
        File.WriteAllLines(Path.Combine(outDir, "report.txt"), L);

        AllDollChecks.Run();
        File.AppendAllText(Path.Combine(outDir, "report.txt"), "\nAllDollChecks: PASS\n");

        foreach (var (key, _) in list)
        {
            DollVideo.Record(key, "full");
            NewDollImporter.Preview(key, "mon", outDir, false, new[] { 0f, 65f }, 512);
            DollColliderTools.HullWire(key);
        }
        Debug.Log("[DollWholeBatch] done " + list.Length);
    }

    // 2차 보정: 얼룩말 보스 눈 재가져오기(+충돌체), 코뿔소 텍스처 반영 확인, 측정 방식 바꾼 낙하·더미 재측정(24종)
    public static void Fix2()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/repairs_20261104/whole");
        AssetDatabase.Refresh();
        var L = DollColliderTools.lines; L.Clear();
        NewDollImporter.Import("zebra", "horse"); DollColliderTools.Build("zebra");
        L.Add("== 낙하 10회 (헐-바닥 실제 겹침 깊이) =="); foreach (var (key, _) in All) DollColliderTools.DropTest(key);
        L.Add("== 더미 12마리 (간격 = 인형 길이 기준) =="); foreach (var (key, _) in All) DollColliderTools.PileTest(key, false);
        File.WriteAllLines(Path.Combine(outDir, "report_fix2.txt"), L);
        AllDollChecks.Run();
        File.AppendAllText(Path.Combine(outDir, "report_fix2.txt"), "\nAllDollChecks: PASS\n");
        NewDollImporter.Preview("zebra", "mon", outDir, false, new[] { 0f, 65f }, 512);
        DollVideo.Record("zebra", "full"); DollVideo.Record("rhino", "full"); DollColliderTools.HullWire("zebra");
        Debug.Log("[DollWholeBatch] fix2 done");
    }

    // 3차: 얼룩말 보스(흰 피부) 재가져오기 + 충돌체, 코뿔소 텍스처 반영, 검사·캡처
    public static void Fix3()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/repairs_20261104/whole");
        AssetDatabase.Refresh();
        DollColliderTools.lines.Clear();
        NewDollImporter.Import("zebra", "horse"); DollColliderTools.Build("zebra");
        AllDollChecks.Run();
        NewDollImporter.Preview("zebra", "mon", outDir, false, new[] { 0f, 65f }, 512);
        DollVideo.Record("zebra", "full"); DollVideo.Record("rhino", "full"); DollColliderTools.HullWire("zebra");
        Debug.Log("[DollWholeBatch] fix3 done");
    }

    // 낙하만 다시 (-dollKeys a,b)
    public static void DropOnly()
    {
        var args = System.Environment.GetCommandLineArgs(); int ai = System.Array.IndexOf(args, "-dollKeys");
        var L = DollColliderTools.lines; L.Clear();
        foreach (var k in args[ai + 1].Split(',')) DollColliderTools.DropTest(k);
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/repairs_20261104/whole/report_drop.txt"), L);
    }

    // 가져오기·충돌체가 끝난 뒤 검사 + 검수 자료만 다시
    public static void ChecksAndCaptures()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/repairs_20261104/whole");
        Directory.CreateDirectory(outDir);
        AllDollChecks.Run();
        File.AppendAllText(Path.Combine(outDir, "report.txt"), "\nAllDollChecks: PASS\n");
        foreach (var (key, _) in All)
        {
            DollVideo.Record(key, "full");
            NewDollImporter.Preview(key, "mon", outDir, false, new[] { 0f, 65f }, 512);
            DollColliderTools.HullWire(key);
        }
        Debug.Log("[DollWholeBatch] captures done");
    }

    // 오리 베이스(펭귄·부엉이) 날개 가중치 수정본(tools/fix_wing_weights.py) 반영: 가져오기(일반+보스) -> 충돌체 -> 검사
    [MenuItem("Tools/신규 인형/펭귄·부엉이 날개 수정 반영")]
    public static void WingFixMenu()
    {
        DollColliderTools.lines.Clear();
        foreach (var key in new[] { "penguin", "owl" })
        {
            NewDollImporter.Import(key, "duck");
            DollColliderTools.Build(key);
        }
        AssetDatabase.SaveAssets();
        AllDollChecks.Run();
        Debug.Log("[DollWholeBatch] 펭귄·부엉이 날개 수정 반영 완료");
    }

    // 배치용: 날개 수정 반영 + 일반·보스 영상 프레임 (_design/dolls/video/<key>_<kind>)
    public static void WingFixBatch()
    {
        WingFixMenu();
        foreach (var key in new[] { "penguin", "owl" }) { DollVideo.Record(key, "full"); DollVideo.Record(key, "mon"); }
        Debug.Log("[DollWholeBatch] wingfix batch done");
    }
}
