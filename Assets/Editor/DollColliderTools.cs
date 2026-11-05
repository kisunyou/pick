using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 인형 충돌체(ConcaveCollider 볼록 헐) 재생성 + 물리 검증.
// 원본 인형 설정(Fast, MaxHulls 24, Precision 0.8)에서 MaxHullVertices 만 10 -> 24 (2026-11-04 하마 시험: 표면 이탈 p95 7.3% -> 2.9%, 더미 파고듦 4.3 -> 0.6cm)
public static class DollColliderTools
{
    const string DollDir = "Assets/Resources/Prefabs/dollPrefabs";
    const string ModelDir = "Assets/Resources/Model";
    internal static readonly List<string> lines = new List<string>();
    static void Log(string s) { lines.Add(s); Debug.Log("[DollCollider] " + s); }

    static Mesh RestWorldMesh(GameObject root, out SkinnedMeshRenderer smr)
    {
        smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var baked = new Mesh(); smr.BakeMesh(baked, true);
        var v = baked.vertices; for (int i = 0; i < v.Length; i++) v[i] = smr.transform.TransformPoint(v[i]);
        baked.vertices = v; baked.RecalculateBounds(); return baked;
    }

    public static void Build(string key)
    {
        string path = $"{DollDir}/doll_{key}_full_prefab.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var cc = root.GetComponentInChildren<ConcaveCollider>(true);
            var colT = cc.transform;
            var world = RestWorldMesh(root, out _);
            // ConcaveCollider 는 자기 MeshFilter 메시(로컬 좌표)를 분해한다.
            // UV 이음새로 갈라진 정점을 위치 기준으로 합쳐 닫힌 메시로 넘긴다 (안 합치면 표면 조각마다 얇은 판 헐이 생긴다)
            var map = new Dictionary<Vector3Int, int>(); var welded = new List<Vector3>(); var src = world.vertices;
            var remap = new int[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var k = Vector3Int.RoundToInt(src[i] * 10000f);
                if (!map.TryGetValue(k, out int idx)) { idx = welded.Count; map[k] = idx; welded.Add(colT.InverseTransformPoint(src[i])); }
                remap[i] = idx;
            }
            var tris = new List<int>(); var wt = world.triangles;
            for (int i = 0; i < wt.Length; i += 3)
            {
                int a = remap[wt[i]], b = remap[wt[i + 1]], c = remap[wt[i + 2]];
                if (a != b && b != c && a != c) { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            var colMesh = new Mesh { name = $"doll_{key}_col" };
            colMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; colMesh.SetVertices(welded); colMesh.SetTriangles(tris, 0); colMesh.RecalculateNormals(); colMesh.RecalculateBounds();
            Log($"{key}: 충돌 원본 메시 정점 {src.Length} -> 합친 뒤 {welded.Count}, 삼각형 {tris.Count / 3}");
            string colPath = $"{ModelDir}/doll_{key}_col_mesh.asset";
            AssetDatabase.DeleteAsset(colPath); AssetDatabase.CreateAsset(colMesh, colPath);
            colT.GetComponent<MeshFilter>().sharedMesh = colMesh;

            var old = colT.Cast<Transform>().Where(t => t.name.StartsWith("Hull ")).ToList();
            var oldCol = old.Select(t => t.GetComponent<Collider>()).FirstOrDefault(c => c != null);
            var mat = oldCol != null ? oldCol.sharedMaterial : null; int layer = old.Count > 0 ? old[0].gameObject.layer : colT.gameObject.layer;
            foreach (var t in old) UnityEngine.Object.DestroyImmediate(t.gameObject);

            cc.Algorithm = ConcaveCollider.EAlgorithm.Fast; cc.MaxHulls = 24; cc.MaxHullVertices = 24; cc.Precision = .8f;
            cc.CreateMeshAssets = false; cc.CreateHullMesh = false; cc.PhysMaterial = mat;
            // 배치 실행에서는 부피가 -1(미계산)로 돌아와 전부 박스로 근사되므로 박스 근사를 끈다
            cc.MinHullVolume = -2f; cc.DebugLog = true;
            cc.ComputeHulls(m => Debug.Log("[ConcaveCollider] " + m), null);

            string hullPath = $"{ModelDir}/doll_{key}_hulls.asset";
            AssetDatabase.DeleteAsset(hullPath);
            int n = 0;
            foreach (var go in cc.m_aGoHulls.Where(g => g != null))
            {
                go.layer = layer;
                var mc = go.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    var m = mc.sharedMesh; m.name = go.name; m.RecalculateNormals();
                    if (n == 0) AssetDatabase.CreateAsset(m, hullPath); else AssetDatabase.AddObjectToAsset(m, hullPath);
                    mc.sharedMesh = m; mc.sharedMaterial = mat; n++;
                }
            }
            // Actor.colliders 가 지운 옛 헐을 가리키지 않게 다시 연결 (DisablePhysics 가 이 목록을 끈다)
            var actor = root.GetComponent<FunRabbit.Actor>(); if (actor != null) FunRabbit.ActorEditor.ApplyReferences(actor);
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Log($"{key}: 헐 {cc.m_aGoHulls.Length}개 생성 (메시 헐 {n}), 재질 {(mat ? mat.name : "none")}, 레이어 {LayerMask.LayerToName(layer)}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // 메시 정점이 헐 밖으로 얼마나 나가는지 / 헐이 메시보다 얼마나 큰지
    public static void Coverage(string key)
    {
        var root = PrefabUtility.LoadPrefabContents($"{DollDir}/doll_{key}_full_prefab.prefab");
        try
        {
            var mesh = RestWorldMesh(root, out _);
            var cols = root.GetComponentsInChildren<Collider>(true).Where(c => c.transform.name.StartsWith("Hull ")).ToArray();
            float h = mesh.bounds.size.y;
            var outside = new List<float>();
            foreach (var p in mesh.vertices.Distinct())
            {
                float d = cols.Min(c => (Physics.ClosestPoint(p, c, c.transform.position, c.transform.rotation) - p).magnitude);
                outside.Add(d);
            }
            outside.Sort();
            float p95 = outside[(int)(outside.Count * .95f)], max = outside.Last();
            float within = outside.Count(d => d < .01f * h) / (float)outside.Count;
            // 헐 표면이 메시에서 얼마나 떨어져 있나 (헐 꼭짓점 -> 가장 가까운 메시 정점)
            var mv = mesh.vertices;
            var hullPts = cols.OfType<MeshCollider>().SelectMany(c => c.sharedMesh.vertices.Select(v => c.transform.TransformPoint(v))).ToArray();
            float hullGap = hullPts.Length == 0 ? 0 : hullPts.Select(q => mv.Min(v => (v - q).sqrMagnitude)).Select(Mathf.Sqrt).OrderBy(x => x).ElementAt((int)(hullPts.Length * .95f));
            Log($"{key}: 헐 {cols.Length}개 | 키 {h:F3} | 표면이 헐 안(키 1% 이내) {within:P0} | 밖으로 나간 거리 p95 {p95 / h:P1}, 최대 {max / h:P1} | 헐 꼭짓점-표면 거리 p95 {hullGap / h:P1}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static GameObject Spawn(string key, Vector3 pos, Quaternion rot)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        go.transform.SetPositionAndRotation(pos, rot);
        var rb = go.GetComponent<Rigidbody>();
        // Actor.cs 런타임 오버라이드와 같은 값
        rb.linearDamping = .5f; rb.angularDamping = 1f; rb.maxDepenetrationVelocity = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.isKinematic = false; rb.useGravity = true;
        foreach (var an in go.GetComponentsInChildren<Animator>()) an.enabled = false;
        return go;
    }

    static GameObject Box(Vector3 c, Vector3 s, string name)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.position = c; g.transform.localScale = s; return g;
    }

    public static void DropTest(string key)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // 시험 중에만 수동 시뮬레이션 - 이 값은 ProjectSettings 에 저장되므로 끝나면 반드시 되돌린다 (안 되돌리면 게임에서 인형이 공중에 멈춤)
        var prevSim = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
        var floor = Box(new Vector3(0, -.5f, 0), new Vector3(10, 1, 10), "floor").GetComponent<Collider>();
        // 시작 높이 = 인형 가장 긴 변의 절반 + 여유 (긴 인형이 기울어진 채 바닥에 박혀 시작하지 않게)
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        var sz = RestWorldMesh(probe, out _).bounds.size; UnityEngine.Object.DestroyImmediate(probe);
        float dropY = Mathf.Max(.8f, Mathf.Max(sz.x, sz.y, sz.z) * .6f + .2f);
        var rnd = new System.Random(7);
        int stable = 0, sunk = 0; float settle = 0, maxSink = 0; var tilts = new List<float>();
        for (int trial = 0; trial < 10; trial++)
        {
            var rot = trial == 0 ? Quaternion.identity : Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360));
            var go = Spawn(key, new Vector3(0, dropY, 0), rot); var rb = go.GetComponent<Rigidbody>();
            float t = 0, rest = -1;
            for (int i = 0; i < 250; i++)
            {
                Physics.Simulate(.02f); t += .02f;
                if (rest < 0 && i > 10 && rb.linearVelocity.magnitude < .02f && rb.angularVelocity.magnitude < .05f) rest = t;
                // 실제 헐-바닥 겹침 깊이 (AABB 는 기울어진 긴 헐(악어 꼬리)에서 바닥 아래로 커져 과대 측정됨)
                foreach (var c in go.GetComponentsInChildren<Collider>().Where(c => c.name.StartsWith("Hull ")))
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, floor, floor.transform.position, floor.transform.rotation, out _, out float pen))
                        maxSink = Mathf.Max(maxSink, pen);
            }
            if (rest > 0) { stable++; settle += rest; }
            if (go.transform.position.y < -.2f) sunk++;
            tilts.Add(Vector3.Angle(go.transform.up, Vector3.up));
            UnityEngine.Object.DestroyImmediate(go);
        }
        Physics.simulationMode = prevSim;
        Log($"{key}: 멈춤 {stable}/10 (평균 {settle / Mathf.Max(1, stable):F2}초) | 바닥 관통 {sunk}회 | 최대 바닥 파고듦 {maxSink * 100:F1}cm | 정지 자세 기울기 {string.Join(",", tilts.Select(a => a.ToString("F0")))}°");
    }

    // 인형뽑기 통처럼 상자에 12마리를 쏟는다. 서로 파고든 깊이를 재고 프레임을 남긴다.
    public static void PileTest(string key, bool record)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // 시험 중에만 수동 시뮬레이션 - 이 값은 ProjectSettings 에 저장되므로 끝나면 반드시 되돌린다 (안 되돌리면 게임에서 인형이 공중에 멈춤)
        var prevSim = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
        // 간격 = 인형의 가장 긴 변 기준 (악어처럼 긴 인형도 생성 순간 겹치지 않게)
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        var size = RestWorldMesh(probe, out _).bounds.size; UnityEngine.Object.DestroyImmediate(probe);
        float gap = Mathf.Max(1.5f, Mathf.Max(size.x, size.y, size.z) * 1.05f);
        float W = 2 * gap + .2f;
        Box(new Vector3(0, -.5f, 0), new Vector3(W + 1, 1, W + 1), "floor");
        // 벽은 높게(투명 앞벽), 인형은 겹치지 않게 2x2 격자 · 층 간격 1.3m 로 생성 (생성 순간 겹침이 폭발적 분리를 만들지 않도록)
        Box(new Vector3(-W / 2 - .05f, 4, 0), new Vector3(.1f, 8, W), "wallL"); Box(new Vector3(W / 2 + .05f, 4, 0), new Vector3(.1f, 8, W), "wallR");
        Box(new Vector3(0, 4, W / 2 + .05f), new Vector3(W, 8, .1f), "wallB");
        var front = Box(new Vector3(0, 4, -W / 2 - .05f), new Vector3(W, 8, .1f), "wallF"); front.GetComponent<MeshRenderer>().enabled = false;
        var rnd = new System.Random(3); var dolls = new List<GameObject>();
        for (int i = 0; i < 12; i++)
            dolls.Add(Spawn(key, new Vector3((i % 2 - .5f) * gap + (float)(rnd.NextDouble() - .5) * .1f, 1.0f + (i / 4) * gap, ((i / 2) % 2 - .5f) * gap),
                Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360))));
        Camera cam = null; RenderTexture rt = null; Texture2D tex = null; string outDir = null;
        if (record)
        {
            outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/video", $"pile_{key}");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true); Directory.CreateDirectory(outDir);
            cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
            cam.transform.position = new Vector3(0, 4.6f, -5.6f); cam.transform.LookAt(new Vector3(0, .5f, 0)); cam.fieldOfView = 40;
            var l = new GameObject("light").AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(50, 150, 0); l.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(.5f, .5f, .55f);
            rt = new RenderTexture(540, 540, 24); cam.targetTexture = rt; tex = new Texture2D(540, 540, TextureFormat.RGB24, false);
        }
        int frame = 0;
        // 생성 직후 겹침 확인 (0 이어야 정상 시험)
        int startOverlap = 0;
        var hs0 = dolls.Select(d => d.GetComponentsInChildren<Collider>().Where(c => c.name.StartsWith("Hull ")).ToArray()).ToArray();
        for (int a = 0; a < hs0.Length; a++) for (int b = a + 1; b < hs0.Length; b++)
                foreach (var ca in hs0[a]) foreach (var cb in hs0[b])
                        if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb, cb.transform.position, cb.transform.rotation, out _, out _)) startOverlap++;
        Log($"{key}: 생성 직후 겹친 헐 쌍 {startOverlap}");
        for (int i = 0; i < 400; i++)   // 8초
        {
            Physics.Simulate(.02f);
            if (record && i % 2 == 1)
            {
                cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 540, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"f_{frame++:D4}.png"), tex.EncodeToPNG());
            }
        }
        // 인형끼리 파고든 최대 깊이 (헐 쌍)
        float maxPen = 0; int pairs = 0;
        var hulls = dolls.Select(d => d.GetComponentsInChildren<Collider>().Where(c => c.name.StartsWith("Hull ")).ToArray()).ToArray();
        for (int a = 0; a < hulls.Length; a++)
            for (int b = a + 1; b < hulls.Length; b++)
                foreach (var ca in hulls[a]) foreach (var cb in hulls[b])
                        if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb, cb.transform.position, cb.transform.rotation, out _, out float dist))
                        { pairs++; maxPen = Mathf.Max(maxPen, dist); }
        int escaped = dolls.Count(d => d.transform.position.y < -.3f || Mathf.Abs(d.transform.position.x) > W / 2 + .3f || Mathf.Abs(d.transform.position.z) > W / 2 + .3f);
        float moving = dolls.Max(d => d.GetComponent<Rigidbody>().linearVelocity.magnitude);
        float top = dolls.Max(d => d.GetComponentsInChildren<Collider>().Where(c => c.name.StartsWith("Hull ")).Max(c => c.bounds.max.y));
        Log($"{key}: 상자 이탈/관통 {escaped}마리 | 8초 후 최대 속도 {moving:F3} | 겹친 헐 쌍 {pairs}, 최대 파고듦 {maxPen * 100:F1}cm | 더미 높이 {top:F2}");
        if (record) { UnityEngine.Object.DestroyImmediate(tex); rt.Release(); }
        Physics.simulationMode = prevSim;
    }

    // 인형 | 충돌체(색칠) 나란히 정지 그림
    public static void HullPictures(string key)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var a = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        var b = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        b.transform.position = new Vector3(1.4f, 0, 0);
        foreach (var r in b.GetComponentsInChildren<Renderer>()) r.enabled = false;
        var sh = Shader.Find("Universal Render Pipeline/Lit"); int i = 0;
        foreach (var mc in b.GetComponentsInChildren<MeshCollider>().Where(c => c.name.StartsWith("Hull ")))
        {
            var g = new GameObject("vis"); g.transform.SetParent(mc.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mc.sharedMesh;
            var m = new Material(sh); m.color = Color.HSVToRGB((i++ * .618f) % 1, .55f, .95f); g.AddComponent<MeshRenderer>().sharedMaterial = m;
        }
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
        var l = new GameObject("light").AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(40, 150, 0); l.intensity = 1.2f;
        cam.orthographic = true; cam.orthographicSize = .85f; cam.aspect = 2;
        var rt = new RenderTexture(1080, 540, 24); cam.targetTexture = rt; var tex = new Texture2D(1080, 540, TextureFormat.RGB24, false);
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/video");
        int shot = 0;
        foreach (float yaw in new[] { 25f, 115f, 205f })
        {
            cam.transform.position = new Vector3(.7f, .4f, 0) + Quaternion.Euler(-15, yaw, 0) * Vector3.forward * 5f;
            cam.transform.LookAt(new Vector3(.7f, .4f, 0));
            // 두 인형을 각각 같은 각도로 보이게 회전
            a.transform.rotation = b.transform.rotation = Quaternion.Euler(0, yaw - 25, 0);
            cam.transform.position = new Vector3(.7f, .45f, -5f); cam.transform.LookAt(new Vector3(.7f, .45f, 0));
            cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1080, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, $"hulls_{key}_{shot++}.png"), tex.EncodeToPNG());
        }
        Log($"{key}: 충돌체 그림 저장");
    }


    // Unity 씬 뷰 기즈모처럼 충돌체를 초록 선으로 (가려진 선도 보이게 ZTest Always). 결과: gallery_<key>/<각도>_wire.png, _wireonly.png
    public static void HullWire(string key)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        var bodyRenderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        var b = RestWorldMesh(go, out _).bounds;
        var lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); lineMat.SetInt("_ZWrite", 0);
        lineMat.SetInt("_Cull", 0); lineMat.color = new Color(.35f, 1f, .35f, 1f); lineMat.renderQueue = 4000;
        var lines = new List<Renderer>();
        foreach (var mc in go.GetComponentsInChildren<MeshCollider>().Where(c => c.name.StartsWith("Hull ")))
        {
            // 평면 위 대각선(두 삼각형 법선이 같은 공유 변)은 빼고 헐의 실제 모서리만
            var m = mc.sharedMesh; var v = m.vertices; var t = m.triangles;
            var faceN = new Dictionary<(int, int), List<Vector3>>();
            for (int i = 0; i < t.Length; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                for (int e = 0; e < 3; e++)
                {
                    int a = t[i + e], c = t[i + (e + 1) % 3]; var k = a < c ? (a, c) : (c, a);
                    if (!faceN.TryGetValue(k, out var lst)) faceN[k] = lst = new List<Vector3>(); lst.Add(n);
                }
            }
            var idx = new List<int>();
            foreach (var kv in faceN)
                if (kv.Value.Count != 2 || Vector3.Dot(kv.Value[0], kv.Value[1]) < .999f) { idx.Add(kv.Key.Item1); idx.Add(kv.Key.Item2); }
            var lm = new Mesh(); lm.vertices = v; lm.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            var g = new GameObject("wire"); g.transform.SetParent(mc.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = lm; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = lineMat; lines.Add(r);
        }
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.32f, .34f, .38f);
        var l = new GameObject("light").AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(40, 150, 0); l.intensity = 1.2f;
        RenderSettings.ambientLight = new Color(.55f, .55f, .6f);
        cam.orthographic = true; cam.aspect = 1; cam.orthographicSize = b.extents.magnitude * .82f;
        var rt = new RenderTexture(800, 800, 24) { antiAliasing = 8 }; cam.targetTexture = rt; var tex = new Texture2D(800, 800, TextureFormat.RGB24, false);
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/video", $"gallery_{key}");
        Directory.CreateDirectory(outDir);
        var views = new (string name, float pitch, float yaw)[] { ("back", -10, 0), ("q34", -20, 145), ("side", -5, 90), ("front", -10, 180), ("bottom", 75, 180), ("top", -80, 180) };
        void Shot(string file)
        {
            cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        }
        foreach (var vw in views)
        {
            cam.transform.position = b.center + Quaternion.Euler(-vw.pitch, vw.yaw, 0) * Vector3.forward * 5f; cam.transform.LookAt(b.center);
            foreach (var r in bodyRenderers) r.enabled = true; Shot($"{vw.name}_wire.png");
            foreach (var r in bodyRenderers) r.enabled = false; Shot($"{vw.name}_wireonly.png");
        }
        Debug.Log($"[DollCollider] wire {key}: {lines.Count} hulls");
    }

    // 각도별 [인형 | 충돌체만 | 반투명 인형 + 충돌체] 그림. 결과: _design/dolls/video/gallery_<key>/<각도>_<doll|hull|mix>.png
    public static void HullGallery(string key)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{DollDir}/doll_{key}_full_prefab.prefab"));
        // 원래 켜져 있던 렌더러만 (doll_<base>_col 의 충돌 원본 메시 렌더러는 꺼져 있어야 한다)
        var bodyRenderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        var world = RestWorldMesh(go, out _); var b = world.bounds;
        var lit = Shader.Find("Universal Render Pipeline/Lit"); int i = 0; var hullRenderers = new List<Renderer>();
        foreach (var mc in go.GetComponentsInChildren<MeshCollider>().Where(c => c.name.StartsWith("Hull ")))
        {
            var g = new GameObject("vis"); g.transform.SetParent(mc.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mc.sharedMesh;
            var m = new Material(lit); m.color = Color.HSVToRGB((i++ * .618f) % 1, .6f, .95f);
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; hullRenderers.Add(r);
        }
        // 반투명 인형 재질 (URP Lit transparent)
        var ghostMats = bodyRenderers.Select(r => r.sharedMaterials).ToArray();
        var ghost = new Material(lit); ghost.SetFloat("_Surface", 1); ghost.SetFloat("_Blend", 0);
        ghost.SetOverrideTag("RenderType", "Transparent"); ghost.SetFloat("_ZWrite", 0);
        ghost.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); ghost.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); ghost.renderQueue = 3000; ghost.color = new Color(1, 1, 1, .35f);
        var cam = new GameObject("cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
        var l = new GameObject("light").AddComponent<Light>(); l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(40, 150, 0); l.intensity = 1.2f;
        RenderSettings.ambientLight = new Color(.55f, .55f, .6f);
        cam.orthographic = true; cam.aspect = 1; cam.orthographicSize = b.extents.magnitude * .82f;
        var rt = new RenderTexture(600, 600, 24); cam.targetTexture = rt; var tex = new Texture2D(600, 600, TextureFormat.RGB24, false);
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/video", $"gallery_{key}");
        Directory.CreateDirectory(outDir);
        var views = new (string name, float pitch, float yaw)[] { ("front", -10, 180), ("q34", -20, 145), ("side", -5, 90), ("back", -10, 0), ("top", -80, 180), ("bottom", 75, 180) };
        void Shot(string file)
        {
            cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 600, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        }
        foreach (var v in views)
        {
            cam.transform.position = b.center + Quaternion.Euler(-v.pitch, v.yaw, 0) * Vector3.forward * 5f;
            cam.transform.LookAt(b.center);
            foreach (var r in bodyRenderers) r.enabled = true; foreach (var r in hullRenderers) r.enabled = false;
            for (int k = 0; k < bodyRenderers.Length; k++) bodyRenderers[k].sharedMaterials = ghostMats[k];
            Shot($"{v.name}_doll.png");
            foreach (var r in bodyRenderers) r.enabled = false; foreach (var r in hullRenderers) r.enabled = true;
            Shot($"{v.name}_hull.png");
            foreach (var r in bodyRenderers) { r.enabled = true; r.sharedMaterials = Enumerable.Repeat(ghost, r.sharedMaterials.Length).ToArray(); }
            Shot($"{v.name}_mix.png");
        }
        for (int k = 0; k < bodyRenderers.Length; k++) bodyRenderers[k].sharedMaterials = ghostMats[k];
        Debug.Log($"[DollCollider] gallery {key}: {hullRenderers.Count} hulls");
    }
}

