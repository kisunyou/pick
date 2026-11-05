using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 인형 비교 동영상용 프레임 캡처 (게임 셰이더·프리팹 그대로). 결과: _design/dolls/video/<key>_<kind>/f_0000.png ...
// 순서: 정지 자세 360도 회전 -> 애니 클립마다 2회 반복 (30fps). 카메라는 전 구간 고정(점프까지 들어가게 먼저 범위 계산).
public static class DollVideo
{
    const int Fps = 30, Res = 540;

    public static void Record(string key, string kind)
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/video", $"{key}_{kind}");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Prefabs/dollPrefabs/doll_{key}_{kind}_prefab.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene); cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
        cam.orthographic = true; cam.aspect = 1f;
        var lightGo = new GameObject("light"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
        lightGo.transform.rotation = Quaternion.Euler(40, 150, 0); l.intensity = 1.2f;
        var an = go.GetComponentInChildren<Animator>();
        var clips = an.runtimeAnimatorController.animationClips.Distinct().OrderBy(c => c.name).ToArray();

        // 1) 전 구간 범위 (회전 포함하므로 수평은 반지름으로)
        var b = new Bounds(); bool first = true;
        void Grow()
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                foreach (var v in baked.vertices) { var w = smr.transform.TransformPoint(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
        Grow();
        foreach (var c in clips) for (int f = 0; f <= 20; f++) { c.SampleAnimation(an.gameObject, c.length * f / 20f); Grow(); }
        float radius = new Vector2(b.extents.x, b.extents.z).magnitude;
        cam.orthographicSize = Mathf.Max(b.size.y * .5f, radius) * 1.12f;
        var rt = new RenderTexture(Res, Res, 24); cam.targetTexture = rt;
        var tex = new Texture2D(Res, Res, TextureFormat.RGB24, false);
        int n = 0;
        void Shot(float yaw)
        {
            var center = new Vector3(b.center.x, b.center.y, b.center.z);
            cam.transform.position = center + Quaternion.Euler(-12, yaw, 0) * Vector3.forward * (b.extents.magnitude * 4f);
            cam.transform.LookAt(center);
            cam.Render(); RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, $"f_{n++:D4}.png"), tex.EncodeToPNG());
        }
        // 2) 정지 자세 360도 (3초) - 기본 자세는 idle 첫 프레임
        var idle = clips.FirstOrDefault(c => c.name.Contains("idle")) ?? clips[0];
        idle.SampleAnimation(an.gameObject, 0);
        for (int i = 0; i < Fps * 3; i++) Shot(25 + 360f * i / (Fps * 3));
        // 3) 클립마다 2회 반복, 3/4 앞 시점
        foreach (var c in clips)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(c.length * Fps));
            for (int rep = 0; rep < 2; rep++)
                for (int f = 0; f < frames; f++) { c.SampleAnimation(an.gameObject, c.length * f / frames); Shot(25); }
        }
        File.WriteAllText(Path.Combine(outDir, "clips.txt"), string.Join("\n", clips.Select(c => $"{c.name}\t{c.length:F3}")));
        UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo);
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        Debug.Log($"[DollVideo] {key}_{kind}: {n} frames");
    }
}
