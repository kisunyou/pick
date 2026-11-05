using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 신규 인형(_design/dolls/rig/<key>) 을 게임 리소스로 가져온다.
// 베이스 인형의 프리팹(충돌체·소켓·애니 컨트롤러)을 복제하고 메시·아바타·머티리얼만 새 인형 것으로 바꾼다.
// 새 인형은 베이스 인형과 같은 뼈대로 만들어져 있어 베이스 애니 클립을 그대로 쓴다.
// 배치 실행: Unity -batchmode -projectPath D:\pick -executeMethod NewDollImporter.ImportFromArgs -dollKey redpanda -dollBase panda -quit
public static class NewDollImporter
{
    const string DesignRoot = "_design/dolls/rig";
    const string ModelDir = "Assets/Resources/Model";
    const string DollDir = "Assets/Resources/Prefabs/dollPrefabs";

    public static void ImportFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        string keys = Arg("-dollKey"), bases = Arg("-dollBase");
        if (string.IsNullOrEmpty(keys) || string.IsNullOrEmpty(bases))
            throw new Exception("-dollKey / -dollBase 필요");
        string[] k = keys.Split(','), b = bases.Split(',');
        for (int i = 0; i < k.Length; i++)
            Import(k[i].Trim(), b[i].Trim());
    }

    // 신규 동물 24종 (신규 키, 베이스 키): 2바퀴 13~24 + 3바퀴 25~36
    static readonly string[,] Dolls =
    {
        { "sheep", "cow" }, { "boar", "pig" }, { "polarbear", "bear" }, { "squid", "octopus" },
        { "penguin", "duck" }, { "turtle", "frog" }, { "zebra", "horse" }, { "hamster", "koala" },
        { "gorilla", "monk" }, { "redpanda", "panda" }, { "tiger", "lion" }, { "rhino", "elephant" },
        { "deer", "cow" }, { "capybara", "pig" }, { "dog", "bear" }, { "jellyfish", "octopus" },
        { "owl", "duck" }, { "crocodile", "frog" }, { "llama", "horse" }, { "rabbit", "koala" },
        { "sloth", "monk" }, { "raccoon", "panda" }, { "fox", "lion" }, { "hippo", "elephant" },
    };

    // 에디터를 연 상태에서 신규 동물 모델·보스·썸네일을 한 번에 다시 가져오고 미리보기(_design/dolls/rig/_unity_preview)를 남긴다
    [MenuItem("Tools/신규 인형/24종 다시 가져오기")]
    public static void ImportAllMenu()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), DesignRoot, "_unity_preview");
        Directory.CreateDirectory(outDir);
        for (int i = 0; i < Dolls.GetLength(0); i++)
        {
            EditorUtility.DisplayProgressBar("신규 인형 가져오기", Dolls[i, 0], (float)i / Dolls.GetLength(0));
            try
            {
                Import(Dolls[i, 0], Dolls[i, 1]);
                ImportUi(Dolls[i, 0], Dolls[i, 1]);
                foreach (string kind in new[] { "full", "mon" })
                    Preview(Dolls[i, 0], kind, outDir);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
        Debug.Log("[NewDollImporter] 신규 인형 가져오기 완료");
    }

    [Serializable] class BaseInfo { public string fbx, mat, tex, ctrl; }

    static BaseInfo LoadBase(string baseKey)
    {
        string json = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/tools/bases.json"));
        int i = json.IndexOf("\"" + baseKey + "\"");
        int a = json.IndexOf('{', i), b = json.IndexOf('}', a);
        var info = JsonUtility.FromJson<BaseInfo>(json.Substring(a, b - a + 1));
        string Rel(string p) => p == null ? null : p.Replace("D:/pick/", "");
        info.fbx = Rel(info.fbx); info.tex = Rel(info.tex);
        return info;
    }

    public static void Import(string key, string baseKey)
    {
        var binfo = LoadBase(baseKey);
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string src = Path.Combine(projectRoot, DesignRoot, key);
        string fbx = $"{ModelDir}/doll_{key}_full.FBX";
        string tex = $"{DollDir}/doll_{key}_full.png";
        string monTex = $"{DollDir}/doll_{key}_mon.png";   // 보스용: 눈만 빨간 텍스처
        File.Copy(Path.Combine(src, $"doll_{key}_full.fbx"), Path.Combine(projectRoot, fbx), true);
        File.Copy(Path.Combine(src, $"doll_{key}_full.png"), Path.Combine(projectRoot, tex), true);
        File.Copy(Path.Combine(src, $"doll_{key}_mon.png"), Path.Combine(projectRoot, monTex), true);
        AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(monTex, ImportAssetOptions.ForceSynchronousImport);

        // FBX 설정: 베이스 FBX 와 같은 값으로 (Generic 아바타, 애니·머티리얼 가져오지 않음)
        var baseImporter = (ModelImporter)AssetImporter.GetAtPath(binfo.fbx);
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
        mi.globalScale = baseImporter.globalScale;
        mi.useFileScale = baseImporter.useFileScale;
        mi.isReadable = baseImporter.isReadable;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importBlendShapes = false;
        mi.SaveAndReimport();

        // 텍스처: 베이스 텍스처 설정 복사
        var baseTexPath = binfo.tex;
        foreach (string tp in new[] { tex, monTex })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            if (baseTexPath != null)
            {
                var bti = (TextureImporter)AssetImporter.GetAtPath(baseTexPath);
                var settings = new TextureImporterSettings(); bti.ReadTextureSettings(settings); ti.SetTextureSettings(settings);
                ti.SetPlatformTextureSettings(bti.GetPlatformTextureSettings("Android"));
                ti.maxTextureSize = bti.maxTextureSize; ti.textureCompression = bti.textureCompression;
            }
            if (File.Exists(Path.Combine(src, "repair_report.json")))
            {
                ti.maxTextureSize = 1024;
                var android = ti.GetPlatformTextureSettings("Android"); android.maxTextureSize = 1024; ti.SetPlatformTextureSettings(android);
            }
            ti.SaveAndReimport();
        }

        // 머티리얼: 베이스 머티리얼 복제 후 _BaseMap 교체
        string mat = $"{DollDir}/doll_{key}_full.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mat);
        var originalMaterial = AssetDatabase.LoadAssetAtPath<Material>(binfo.mat);
        if (m == null) { m = new Material(originalMaterial); AssetDatabase.CreateAsset(m, mat); }
        else EditorUtility.CopySerialized(originalMaterial, m);
        m.name = $"doll_{key}_full";
        var t2d = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
        m.SetTexture("_BaseMap", t2d);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t2d);
        EditorUtility.SetDirty(m);

        var mesh = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().First();
        // FBX 내보내기마다 뼈 단위·스케일이 달라질 수 있으므로, 새 메시를 베이스 프리팹의 뼈에 맞춰 다시 만든다
        // (새 FBX 를 휴식 자세로 펼친 월드 좌표 -> 베이스 인형과 같은 크기·위치로 맞춤 -> 베이스 뼈 기준 bindpose 재계산)
        var newGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));

        foreach (string kind in new[] { "full", "mon" })
        {
            if (kind == "mon" && File.Exists(Path.Combine(src, $"doll_{key}_mon.fbx")))
            {
                ImportBossOnly(key, baseKey);
                continue;
            }
            string dst = $"{DollDir}/doll_{key}_{kind}_prefab.prefab";
            var root = PrefabUtility.LoadPrefabContents($"{DollDir}/doll_{baseKey}_{kind}_prefab.prefab");
            try
            {
                root.name = $"doll_{key}_{kind}_prefab";
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string meshPath = $"{ModelDir}/doll_{key}_{kind}_mesh.asset";
                    var baseMat = smr.sharedMaterial;
                    var bodyMat = m;
                    if (kind == "mon")
                    {
                        // 보스: 베이스 보스 머티리얼(외곽선·보스 색감) 복제 + 눈이 빨간 텍스처
                        string monMat = $"{DollDir}/doll_{key}_mon.mat";
                        AssetDatabase.DeleteAsset(monMat);
                        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseMat), monMat)) throw new Exception("mon material copy failed: " + monMat);
                        bodyMat = AssetDatabase.LoadAssetAtPath<Material>(monMat);
                        var mt = AssetDatabase.LoadAssetAtPath<Texture2D>(monTex);
                        bodyMat.SetTexture("_BaseMap", mt);
                        if (bodyMat.HasProperty("_MainTex")) bodyMat.SetTexture("_MainTex", mt);
                        EditorUtility.SetDirty(bodyMat);
                    }
                    var rebuilt = RebuildOnBaseBones(newGo.GetComponentInChildren<SkinnedMeshRenderer>(), smr, kind == "mon", false, key);
                    var storedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (storedMesh == null) { storedMesh = rebuilt; AssetDatabase.CreateAsset(storedMesh, meshPath); }
                    else
                    {
                        storedMesh.Clear(); storedMesh.name=rebuilt.name; storedMesh.indexFormat=rebuilt.indexFormat;
                        storedMesh.vertices=rebuilt.vertices;storedMesh.normals=rebuilt.normals;storedMesh.tangents=rebuilt.tangents;storedMesh.uv=rebuilt.uv;
                        storedMesh.boneWeights=rebuilt.boneWeights;storedMesh.bindposes=rebuilt.bindposes;storedMesh.subMeshCount=rebuilt.subMeshCount;
                        for(int sub=0;sub<rebuilt.subMeshCount;sub++)storedMesh.SetTriangles(rebuilt.GetTriangles(sub),sub);
                        storedMesh.bounds=rebuilt.bounds;EditorUtility.SetDirty(storedMesh);
                    }
                    smr.sharedMesh = storedMesh;
                    // 보스: 서브메시 1 = 베이스 보스 뿔 (베이스 머티리얼 그대로)
                    smr.sharedMaterials = rebuilt.subMeshCount > 1 ? new[] { bodyMat, baseMat } : new[] { bodyMat };
                    smr.gameObject.name = $"doll_{key}_full";
                    smr.localBounds = rebuilt.bounds;
                }
                foreach (var an in root.GetComponentsInChildren<Animator>(true))
                {
                    if (an.gameObject.name.StartsWith("doll_")) an.gameObject.name = $"doll_{key}_{kind}";
                }
                PrefabUtility.SaveAsPrefabAsset(root, dst);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        UnityEngine.Object.DestroyImmediate(newGo);
        AssetDatabase.SaveAssets();
        Debug.Log($"[NewDollImporter] {key} (base {baseKey}) imported");
    }


    // A separate boss FBX can contain the original base boss's modeled eyes (submesh 1).
    // Keep the normal doll and existing asset GUIDs intact when revising only the boss.
    public static void ImportBossOnly(string key, string baseKey)
    {
        var info = LoadBase(baseKey);
        string src = Path.Combine(DesignRoot, key);
        string fbx = $"{ModelDir}/doll_{key}_mon.FBX";
        string tex = $"{DollDir}/doll_{key}_mon.png";
        File.Copy(Path.Combine(src, $"doll_{key}_mon.fbx"), fbx, true);
        File.Copy(Path.Combine(src, $"doll_{key}_mon.png"), tex, true);
        AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceSynchronousImport);
        var sourceImporter = (ModelImporter)AssetImporter.GetAtPath(info.fbx);
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(fbx);
        modelImporter.globalScale = sourceImporter.globalScale;
        modelImporter.useFileScale = sourceImporter.useFileScale;
        modelImporter.isReadable = sourceImporter.isReadable;
        modelImporter.animationType = ModelImporterAnimationType.Generic;
        modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        modelImporter.importAnimation = false;
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        modelImporter.importBlendShapes = false;
        modelImporter.SaveAndReimport();
        var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
        var bti = (TextureImporter)AssetImporter.GetAtPath(info.tex);
        var settings = new TextureImporterSettings(); bti.ReadTextureSettings(settings); ti.SetTextureSettings(settings);
        ti.SetPlatformTextureSettings(bti.GetPlatformTextureSettings("Android"));
        ti.maxTextureSize = bti.maxTextureSize; ti.textureCompression = bti.textureCompression;
        if (File.Exists(Path.Combine(src, "repair_report.json")))
        {
            ti.maxTextureSize=1024;
            var android=ti.GetPlatformTextureSettings("Android");android.maxTextureSize=1024;ti.SetPlatformTextureSettings(android);
        }
        ti.SaveAndReimport();

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject model = null;
        var root = PrefabUtility.LoadPrefabContents($"{DollDir}/doll_{baseKey}_mon_prefab.prefab");
        try
        {
            model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), scene);
            var source = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (source.sharedMesh.subMeshCount != 2) throw new Exception("Boss FBX must contain body and original boss eye submeshes");
            root.name = $"doll_{key}_mon_prefab";
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baseMat = skin.sharedMaterial;
                string matPath = $"{DollDir}/doll_{key}_mon.mat";
                var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (bodyMat == null) { bodyMat = new Material(baseMat); AssetDatabase.CreateAsset(bodyMat, matPath); }
                else EditorUtility.CopySerialized(baseMat, bodyMat);
                bodyMat.name = $"doll_{key}_mon";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
                bodyMat.SetTexture("_BaseMap", texture);
                if (bodyMat.HasProperty("_MainTex")) bodyMat.SetTexture("_MainTex", texture);
                EditorUtility.SetDirty(bodyMat);
                var rebuilt = RebuildOnBaseBones(source, skin, true, baseKey != "cow", key);
                string meshPath = $"{ModelDir}/doll_{key}_mon_mesh.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null) { mesh = rebuilt; AssetDatabase.CreateAsset(mesh, meshPath); }
                else
                {
                    // Mesh native/GPU buffers must be refreshed through mesh setters.
                    mesh.Clear(); mesh.name = rebuilt.name; mesh.indexFormat = rebuilt.indexFormat;
                    mesh.vertices = rebuilt.vertices; mesh.normals = rebuilt.normals;
                    mesh.tangents = rebuilt.tangents; mesh.uv = rebuilt.uv;
                    mesh.boneWeights = rebuilt.boneWeights; mesh.bindposes = rebuilt.bindposes;
                    mesh.subMeshCount = rebuilt.subMeshCount;
                    for (int sub = 0; sub < rebuilt.subMeshCount; sub++) mesh.SetTriangles(rebuilt.GetTriangles(sub), sub);
                    mesh.bounds = rebuilt.bounds;
                    UnityEngine.Object.DestroyImmediate(rebuilt); EditorUtility.SetDirty(mesh);
                }
                skin.sharedMesh = mesh;
                var eyeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Model/Materials/doll_cow_full.mat");
                skin.sharedMaterials = mesh.subMeshCount > 2 ? new[] { bodyMat, eyeMat, baseMat } : new[] { bodyMat, eyeMat };
                skin.localBounds = mesh.bounds;
                skin.gameObject.name = $"doll_{key}_full";
            }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                if (animator.gameObject.name.StartsWith("doll_")) animator.gameObject.name = $"doll_{key}_mon";
            PrefabUtility.SaveAsPrefabAsset(root, $"{DollDir}/doll_{key}_mon_prefab.prefab");
            AssetDatabase.SaveAssets();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            if (model != null) UnityEngine.Object.DestroyImmediate(model);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log($"[NewDollImporter] {key} boss updated with original modeled {baseKey} boss eyes");
    }


    // 머리 뼈: 베이스 메시에서 가중치를 가장 많이 받는 뼈 중 정점들이 가장 높은 뼈
    static string HeadBoneName(SkinnedMeshRenderer smr)
    {
        var baked = new Mesh(); smr.BakeMesh(baked, true); var v = baked.vertices; var w = smr.sharedMesh.boneWeights;
        return Enumerable.Range(0, v.Length).GroupBy(i => w[i].boneIndex0)
            .Where(g => g.Count() > v.Length / 8).OrderByDescending(g => g.Average(i => (smr.transform.rotation * v[i]).y))
            .Select(g => smr.bones[g.Key].name).First();
    }

    static Vector3[] WorldVerts(SkinnedMeshRenderer smr, out Mesh baked)
    {
        baked = new Mesh();
        smr.BakeMesh(baked, true);
        var t = smr.transform;
        return baked.vertices.Select(v => t.position + t.rotation * v).ToArray();
    }

    // 보스 모델의 뿔: 머리 위쪽에 있는 작은 떨어진 조각(정점 60개 미만) 중 텍스처 색이 빨간 것. 조각 번호(정점 -> 조각 대표 정점)도 돌려준다
    // (말 보스의 갈기처럼 머리 위 작은 조각이 뿔 말고도 있으므로 색으로 거른다)
    static HashSet<int> FindHorns(Mesh mesh, Vector3[] world, Texture tex, out Dictionary<int, int> comp)
    {
        var pixels = ReadPixels(tex, out int tw, out int th);
        var uv = mesh.uv;
        bool IsRed(List<int> idx)
        {
            if (pixels == null) return true;
            Color c = Color.black;
            foreach (int i in idx) c += pixels[Mathf.Clamp((int)(Mathf.Repeat(uv[i].y, 1f) * th), 0, th - 1) * tw + Mathf.Clamp((int)(Mathf.Repeat(uv[i].x, 1f) * tw), 0, tw - 1)];
            c /= idx.Count;
            return c.r > 0.4f && c.r > 2f * c.g && c.r > 2f * c.b;
        }
        var parent = Enumerable.Range(0, mesh.vertexCount).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3) { parent[Find(tris[i])] = Find(tris[i + 1]); parent[Find(tris[i + 1])] = Find(tris[i + 2]); }
        // 같은 위치 정점(UV 이음새) 묶기
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < world.Length; i++)
        {
            var k = Vector3Int.RoundToInt(world[i] * 10000f);
            if (byPos.TryGetValue(k, out int j)) parent[Find(i)] = Find(j); else byPos[k] = i;
        }
        float y0 = world.Min(v => v.y), y1 = world.Max(v => v.y);
        var horns = new HashSet<int>();
        foreach (var g in Enumerable.Range(0, world.Length).GroupBy(Find))
        {
            var idx = g.ToList();
            float cy = idx.Average(i => world[i].y);
            if (idx.Count < 60 && cy > y0 + 0.7f * (y1 - y0) && IsRed(idx)) foreach (var i in idx) horns.Add(i);
        }
        comp = horns.ToDictionary(i => i, Find);
        return horns;
    }

    // 읽기 불가 텍스처도 렌더 텍스처로 복사해서 픽셀을 읽는다
    static Color[] ReadPixels(Texture tex, out int w, out int h)
    {
        w = h = 0;
        if (tex == null) return null;
        w = tex.width; h = tex.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t2.ReadPixels(new Rect(0, 0, w, h), 0, 0); t2.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = t2.GetPixels(); UnityEngine.Object.DestroyImmediate(t2);
        return px;
    }

    static Mesh RebuildOnBaseBones(SkinnedMeshRenderer src, SkinnedMeshRenderer dst, bool keepHorns, bool separateHorns = false, string dollKey = null)
    {
        var sv = WorldVerts(src, out var sb);
        var dv = WorldVerts(dst, out _);
        Dictionary<int, int> hornComp = null;
        var horns = keepHorns ? FindHorns(dst.sharedMesh, dv, dst.sharedMaterial != null ? dst.sharedMaterial.mainTexture : null, out hornComp) : new HashSet<int>();
        Bounds Box(IEnumerable<Vector3> vs) { var b = new Bounds(vs.First(), Vector3.zero); foreach (var v in vs) b.Encapsulate(v); return b; }
        var bs = Box(sv); var bd = Box(dv.Where((v, i) => !horns.Contains(i)));
        float s = bd.size.y / bs.size.y;
        Vector3 Fit(Vector3 v) => new Vector3(bd.center.x + (v.x - bs.center.x) * s, bd.min.y + (v.y - bs.min.y) * s, bd.center.z + (v.z - bs.center.z) * s);
        var t = dst.transform;
        var srcMesh = src.sharedMesh;
        var m = new Mesh { name = srcMesh.name, indexFormat = srcMesh.indexFormat };
        m.vertices = sv.Select(v => t.InverseTransformPoint(Fit(v))).ToArray();
        m.normals = sb.normals.Select(n => t.InverseTransformDirection(src.transform.rotation * n).normalized).ToArray();
        m.uv = srcMesh.uv;
        m.subMeshCount = srcMesh.subMeshCount;
        for (int i = 0; i < srcMesh.subMeshCount; i++) m.SetTriangles(srcMesh.GetTriangles(i), i);
        // 베이스 프리팹 스킨에 없는 뼈(가중치 없는 보조 뼈)는 부모를 따라 올라가 있는 뼈로 연결
        int FindDst(Transform b)
        {
            for (var x = b; x != null; x = x.parent)
            {
                int k = Array.FindIndex(dst.bones, d => d.name == x.name);
                if (k >= 0) return k;
            }
            return -1;
        }
        var map = src.bones.Select(FindDst).ToArray();
        if (map.Any(i => i < 0)) throw new Exception("베이스 프리팹에 없는 뼈: " + string.Join(",", src.bones.Where((b, i) => map[i] < 0).Select(b => b.name)));
        m.boneWeights = srcMesh.boneWeights.Select(w => new BoneWeight
        {
            boneIndex0 = map[w.boneIndex0], weight0 = w.weight0, boneIndex1 = map[w.boneIndex1], weight1 = w.weight1,
            boneIndex2 = map[w.boneIndex2], weight2 = w.weight2, boneIndex3 = map[w.boneIndex3], weight3 = w.weight3
        }).ToArray();
        m.bindposes = dst.bones.Select(b => b.worldToLocalMatrix * t.localToWorldMatrix).ToArray();
        if (horns.Count > 0 && dollKey != "rabbit")
        {
            // 뿔 정점을 베이스 보스 메시에서 그대로 붙인다 (베이스 뼈·bindpose 기준이라 좌표 변환 불필요)
            var dm = dst.sharedMesh; var dmv = dm.vertices; var dmn = dm.normals; var dmuv = dm.uv; var dmw = dm.boneWeights;
            var hl = horns.OrderBy(i => i).ToList(); var remap = new Dictionary<int, int>();
            int baseCount = m.vertexCount;
            for (int i = 0; i < hl.Count; i++) remap[hl[i]] = baseCount + i;
            // 새 머리 모양이 베이스와 달라 뿔이 묻히거나 떠 있으므로(오징어 원뿔 머리 등), 뿔 조각마다 밑동을 새 머리 표면의 가장 가까운 점에 붙이고 조금 박아 넣는다
            var newWorld = sv.Select(Fit).ToArray();
            var baseBody = dv.Where((v, i) => !horns.Contains(i)).ToArray();
            var hornOffset = new Dictionary<int, Vector3>();
            var hornPosition = new Dictionary<int, Vector3>();
            var hornRotation = new Dictionary<int, Quaternion>();
            float yTop = newWorld.Max(v => v.y), yBot = newWorld.Min(v => v.y);
            var headPts = newWorld.Where(v => v.y > yBot + 0.6f * (yTop - yBot)).ToArray();
            var headCenter = headPts.Aggregate(Vector3.zero, (acc, v) => acc + v) / headPts.Length;
            int crownBone = Array.FindIndex(dst.bones, bone => bone.name == HeadBoneName(dst));
            if (dollKey == "turtle")
            {
                var weights = m.boneWeights;
                headPts = newWorld.Where((v,i) => weights[i].boneIndex0 == crownBone && weights[i].weight0 > .95f).ToArray();
                headCenter = Box(headPts).center;
            }
            foreach (var grp in hl.GroupBy(i => hornComp[i]))
            {
                var gi = grp.ToList();
                // 밑동 = 베이스 머리 표면에 가장 가까운 뿔 정점들(20%)의 평균
                var byDist = gi.OrderBy(i => baseBody.Min(v => (v - dv[i]).sqrMagnitude)).ToList();
                var rootIdx = byDist.Take(Mathf.Max(1, gi.Count / 5)).ToList();
                var root = rootIdx.Aggregate(Vector3.zero, (acc, i) => acc + dv[i]) / rootIdx.Count;
                var surf = newWorld.OrderBy(v => (v - root).sqrMagnitude).First();
                if (dollKey == "turtle")
                {
                    var headBounds = Box(headPts);
                    float side = Mathf.Sign(root.x - bd.center.x);
                    var desired = new Vector3(headCenter.x + side * headBounds.size.x * .24f, headBounds.max.y - headBounds.size.y * .06f, headCenter.z);
                    surf = headPts.OrderBy(v => (v-desired).sqrMagnitude).First();
                    var tip = gi.Select(i => dv[i]).OrderByDescending(v => (v-root).sqrMagnitude).First();
                    var rotation = Quaternion.FromToRotation(tip-root, new Vector3(side*.22f,1,0).normalized);
                    var anchor = surf + (headCenter-surf).normalized * (.025f*bd.size.y);
                    foreach (int i in gi)
                    {
                        hornPosition[i] = anchor + rotation * (dv[i]-root) * .65f;
                        hornRotation[i] = rotation;
                    }
                }
                var sink = (headCenter - surf).normalized * (0.03f * bd.size.y);
                var off = surf - root + sink;
                foreach (var i in gi) hornOffset[i] = off;
            }
            // 원본 bindpose 가 재계산한 것과 다를 수 있으므로, 뿔도 월드 좌표에서 새 기준으로 다시 변환한다
            // (정점 수가 바뀌면 Unity 가 기존 가중치를 버리므로 모든 배열을 먼저 합친 뒤 한 번에 넣는다)
            var dbaked = new Mesh(); dst.BakeMesh(dbaked, true); var dbn = dbaked.normals;
            int headIdx = Array.FindIndex(dst.bones, b => b.name == HeadBoneName(dst));   // 뿔은 머리 뼈 하나에 붙인다
            var allV = m.vertices.Concat(hl.Select(i => t.InverseTransformPoint(hornPosition.TryGetValue(i,out var fixedPoint) ? fixedPoint : dv[i] + (hornOffset.TryGetValue(i, out var o) ? o : Vector3.zero)))).ToArray();
            var allN = m.normals.Concat(hl.Select(i => t.InverseTransformDirection((hornRotation.TryGetValue(i,out var rotation) ? rotation : Quaternion.identity) * (t.rotation * dbn[i])).normalized)).ToArray();
            var allUV = m.uv.Concat(hl.Select(i => dmuv[i])).ToArray();
            var allW = m.boneWeights.Concat(hl.Select(i => new BoneWeight { boneIndex0 = headIdx, weight0 = 1f })).ToArray();
            var bodyTriangles = m.GetTriangles(0);
            var eyeTriangles = m.subMeshCount > 1 ? m.GetTriangles(1) : new int[0];
            var bp = m.bindposes;
            m.Clear();
            m.vertices = allV; m.normals = allN; m.uv = allUV; m.boneWeights = allW; m.bindposes = bp;
            m.subMeshCount = 2; m.SetTriangles(bodyTriangles, 0);
            Debug.Log($"[NewDollImporter] horn head bone {headIdx}, weights {m.boneWeights.Length}/{m.vertexCount}");
            var ht = new List<int>(); var dt = dm.triangles;
            for (int i = 0; i < dt.Length; i += 3)
                if (horns.Contains(dt[i]) && horns.Contains(dt[i + 1]) && horns.Contains(dt[i + 2]))
                    ht.AddRange(new[] { remap[dt[i]], remap[dt[i + 1]], remap[dt[i + 2]] });
            // Original boss eyes and horns share the same base material/UV atlas.
            if (separateHorns && eyeTriangles.Length > 0)
            {
                m.subMeshCount = 3; m.SetTriangles(eyeTriangles, 1); m.SetTriangles(ht, 2);
            }
            else m.SetTriangles(eyeTriangles.Concat(ht).ToArray(), 1);
            foreach (var kv in hornOffset.GroupBy(k => k.Value)) Debug.Log($"[NewDollImporter] horn group offset {kv.Key} count {kv.Count()}");
            Debug.Log($"[NewDollImporter] horn tris {ht.Count / 3}, horn y range {hl.Min(i => dv[i].y):F3}..{hl.Max(i => dv[i].y):F3}, body top {baseBody.Max(v => v.y):F3}, new top {newWorld.Max(v => v.y):F3}");
            Debug.Log($"[NewDollImporter] boss horns kept: {hl.Count} verts");
        }
        m.RecalculateBounds(); m.RecalculateTangents();
        Debug.Log($"[NewDollImporter] rebuilt {m.name}: scale fit {s:F4}, verts {m.vertexCount}");
        return m;
    }

    // 게임과 같은 셰이더로 정지·애니 프레임 미리보기 렌더 (배치 모드 확인용)
    public static void PreviewFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-dollKey");
        string keys = args[i + 1];
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), DesignRoot, "_unity_preview");
        Directory.CreateDirectory(outDir);
        foreach (string key in keys.Split(','))
            foreach (string kind in new[] { "full", "mon" })
                Preview(key.Trim(), kind, outDir);
    }

    public static void Preview(string key, string kind, string outDir, bool animate = true, float[] yaws = null, int resolution = 512, float animationYaw = 25)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_{kind}_prefab.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.position = Vector3.zero;
        var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene); cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
        var lightGo = new GameObject("light"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
        lightGo.transform.rotation = Quaternion.Euler(40, 150, 0); l.intensity = 1.2f;
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Skin culling bounds include animation padding. Frame the actual rest mesh instead.
            var verts = WorldVerts(r, out var baked);
            foreach (var v in verts)
            {
                if (first) { b = new Bounds(v, Vector3.zero); first = false; }
                else b.Encapsulate(v);
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        cam.orthographic = true;
        cam.aspect = 1f;
        cam.orthographicSize = Mathf.Max(b.size.x, b.size.y, b.size.z) * .72f;
        File.WriteAllText(Path.Combine(outDir, $"{key}_{kind}_bounds.txt"), b.ToString("F5") + "\n" + Application.consoleLogPath);
        var rt = new RenderTexture(resolution, resolution, 24); cam.targetTexture = rt;
        var an = go.GetComponentInChildren<Animator>();
        var clips = an != null && an.runtimeAnimatorController != null ? an.runtimeAnimatorController.animationClips : new AnimationClip[0];
        if (!animate) clips = new AnimationClip[0];
        void FrameVisibleMesh(float yaw)
        {
            cam.transform.position = b.center + Quaternion.Euler(-12, yaw, 0) * Vector3.forward * (b.extents.magnitude * 3.2f);
            cam.transform.LookAt(b.center);
            float xmin=float.PositiveInfinity, xmax=float.NegativeInfinity, ymin=float.PositiveInfinity, ymax=float.NegativeInfinity;
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var points=WorldVerts(skin,out var baked);
                foreach (var p in points)
                {
                    var v=cam.transform.InverseTransformPoint(p);
                    xmin=Mathf.Min(xmin,v.x);xmax=Mathf.Max(xmax,v.x);ymin=Mathf.Min(ymin,v.y);ymax=Mathf.Max(ymax,v.y);
                }
                UnityEngine.Object.DestroyImmediate(baked);
            }
            cam.orthographicSize=Mathf.Max(ymax-ymin,(xmax-xmin)/cam.aspect)*.62f;
            cam.transform.position+=cam.transform.right*((xmin+xmax)*.5f)+cam.transform.up*((ymin+ymax)*.5f);
        }
        int shot = 0;
        foreach (float yaw in yaws ?? new[] { 0f, 180f, -25f })
        {
            FrameVisibleMesh(yaw + 25);
            Save(cam, rt, Path.Combine(outDir, $"{key}_{kind}_still_{shot++}.png"));
        }
        int clipIndex = 0;
        foreach (var clip in clips.Distinct())
        {
            for (int f = 0; f < 3; f++)
            {
                clip.SampleAnimation(an.gameObject, clip.length * (f + 1) / 4f);
                FrameVisibleMesh(animationYaw);
                Save(cam, rt, Path.Combine(outDir, $"{key}_{kind}_clip{clipIndex}_{clip.name}_{f}.png"));
            }
            clipIndex++;
        }
        UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo);
        rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }

    static void Save(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        var pixels = tex.GetPixels32(); var background = pixels[0];
        int foreground = pixels.Count(p => Mathf.Abs(p.r-background.r) + Mathf.Abs(p.g-background.g) + Mathf.Abs(p.b-background.b) > 12);
        if (foreground < pixels.Length / 100)
        {
            RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(tex);
            throw new Exception("Empty doll capture: " + path);
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(tex);
    }

    // 썸네일(일반·보스)과 미션 아이콘 프리팹: 베이스 동물 것의 설정을 복사해 새 이미지로
    public static void ImportUiFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string[] k = Arg("-dollKey").Split(','), b = Arg("-dollBase").Split(',');
        for (int i = 0; i < k.Length; i++) ImportUi(k[i].Trim(), b[i].Trim());
    }

    public static void ImportUi(string key, string baseKey)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        const string thumbDir = "Assets/Resources/UI2/Thumbnail";
        foreach (string suffix in new[] { "", "_boss" })
        {
            string dst = $"{thumbDir}/{key}{suffix}.png";
            File.Copy(Path.Combine(projectRoot, $"_design/dolls/thumb/{key}{suffix}.png"), Path.Combine(projectRoot, dst), true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            var bti = (TextureImporter)AssetImporter.GetAtPath($"{thumbDir}/{baseKey}{suffix}.png");
            var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
            var st = new TextureImporterSettings(); bti.ReadTextureSettings(st); ti.SetTextureSettings(st);
            ti.textureType = bti.textureType; ti.spriteImportMode = bti.spriteImportMode;
            ti.maxTextureSize = bti.maxTextureSize; ti.textureCompression = bti.textureCompression;
            ti.SetPlatformTextureSettings(bti.GetPlatformTextureSettings("Android"));
            ti.SaveAndReimport();
        }
        const string iconDir = "Assets/Resources/UI2/Prefabs/MissionIconPrefab";
        string icon = $"{iconDir}/{key}MissionIcon.prefab";
        AssetDatabase.DeleteAsset(icon);
        if (!AssetDatabase.CopyAsset($"{iconDir}/{baseKey}MissionIcon.prefab", icon)) throw new Exception("icon copy failed " + icon);
        var root = PrefabUtility.LoadPrefabContents(icon);
        try
        {
            root.name = $"{key}MissionIcon";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{thumbDir}/{key}.png");
            var baseSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{thumbDir}/{baseKey}.png");
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (img.sprite == baseSprite) img.sprite = sprite;
            PrefabUtility.SaveAsPrefabAsset(root, icon);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log($"[NewDollImporter] UI {key} imported");
    }
}
