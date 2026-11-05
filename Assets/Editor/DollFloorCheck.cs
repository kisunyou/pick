﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using FunRabbit;
using UnityEditor;
using UnityEngine;

// 2026-11-05 도감 인형 뜸 점검. 도감(CollectionManager.GroundToFloor)은 collectionScale 적용 후
// 자식 Renderer.bounds 최저점을 바닥에 맞춘다. 실제 메시 발바닥(기본 자세 / 각 애니메이션 최저점)과의 차이 = 떠 보이는 높이.
// 실행: Unity -batchmode -projectPath D:/pick -executeMethod DollFloorCheck.Run -quit -> _design/dolls/logs/floor_check.txt
public static class DollFloorCheck
{
    public static void Run()
    {
        GameActorData.Load();
        var lines = new List<string> { "stage\tkey\tscale\tboundsMin\trestFoot\tfloat(rest)\tanimFootMin..Max\tfloat(anim)\trenderers" };
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var mesh = new Mesh();
        try
        {
            foreach (var row in GameActorData.Actors.OrderBy(r => r.stage))
            {
                var prefab = Resources.Load<GameObject>(GameCommon.GetModelPrefabFullPath(row.animalKey));
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                try
                {
                    go.transform.localScale = Vector3.one * row.collectionScale;
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    string extra = "";
                    float boundsMin = renderers.Min(r => r.bounds.min.y);
                    float Foot()
                    {
                        float lowest = float.MaxValue;
                        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s => s.enabled))
                        {
                            smr.BakeMesh(mesh, true);
                            foreach (var v in mesh.vertices) lowest = Mathf.Min(lowest, smr.transform.TransformPoint(v).y);
                        }
                        return lowest;
                    }
                    float rest = Foot();
                    var animator = go.GetComponentInChildren<Animator>();
                    float aMin = float.MaxValue, aMax = float.MinValue;
                    foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct().Where(c => !c.name.ToLower().Contains("jump") && !c.name.ToLower().Contains("attack")))
                        for (int f = 0; f <= 10; f++)
                        {
                            clip.SampleAnimation(animator.gameObject, clip.length * f / 10f);
                            float y = Foot(); aMin = Mathf.Min(aMin, y); aMax = Mathf.Max(aMax, y);
                        }
                    // 새 GroundToFloor 방식 검증: BakeMesh(false)+localToWorld 최저점 vs 충돌 헐(실제 모양에 맞춘 볼록 헐) 최저점
                    foreach (var a in go.GetComponentsInChildren<Animator>()) a.Rebind();
                    float newMin = float.MaxValue;
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(x => x.enabled))
                    {
                        smr.BakeMesh(mesh, true); var m = smr.transform.localToWorldMatrix;
                        foreach (var v in mesh.vertices) newMin = Mathf.Min(newMin, m.MultiplyPoint3x4(v).y);
                    }
                    Physics.SyncTransforms();
                    var hulls = go.GetComponentsInChildren<Collider>(true).Where(c => c.name.StartsWith("Hull ")).ToArray();
                    float hullMin = hulls.Length > 0 ? hulls.Min(c => c.bounds.min.y) : float.NaN;
                    extra = $"	newMin {newMin:F3}	hullMin {hullMin:F3}	diff {(newMin - hullMin):F3}";
                    string names = string.Join(",", renderers.Select(r => $"{r.GetType().Name}:{r.name}{(r.enabled ? "" : "(off)")}"));
                    lines.Add($"{row.stage}\t{row.animalKey}\t{row.collectionScale}\t{boundsMin:F3}\t{rest:F3}\t{rest - boundsMin:F3}\t{aMin:F3}..{aMax:F3}\t{aMin - boundsMin:F3}{extra}\t{names}");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
        finally { Object.DestroyImmediate(mesh); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/logs/floor_check.txt"), lines);
        Debug.Log("[DollFloorCheck] done");
    }

    // 전후 비교 그림: 위 줄 = 예전(범위 상자 기준), 아래 줄 = 수정(메시 최저점 기준). 도감 크기 그대로, 바닥판 위에 세운다
    public static void RenderCompare()
    {
        GameActorData.Load();
        string[] keys = { "fox", "tiger", "lion", "raccoon", "redpanda", "dog", "zebra", "horse", "duck", "gorilla" };
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var mesh = new Mesh();
        try
        {
            float x = 0, gap = 3.6f;
            for (int row = 0; row < 2; row++)
            {
                float z = 0, baseY = (1 - row) * 6.5f;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = new Vector3((keys.Length - 1) * gap / 2, baseY - 0.05f, z); floor.transform.localScale = new Vector3(keys.Length * gap + 2, 0.1f, 4f);
                floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.55f, .42f, .3f) };
                x = 0;
                foreach (var k in keys)
                {
                    var row0 = GameActorData.Get(k);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>(GameCommon.GetModelPrefabFullPath(k)), scene);
                    go.transform.localScale = Vector3.one * row0.collectionScale;
                    go.transform.position = new Vector3(x, baseY, z); go.transform.rotation = Quaternion.Euler(0, 200, 0);
                    float minY = float.MaxValue;
                    if (row == 0) foreach (var r in go.GetComponentsInChildren<Renderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
                    else foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(q => q.enabled))
                        { smr.BakeMesh(mesh, true); var m = smr.transform.localToWorldMatrix; foreach (var v in mesh.vertices) minY = Mathf.Min(minY, m.MultiplyPoint3x4(v).y); }
                    go.transform.position += Vector3.up * (baseY - minY);
                    x += gap;
                }
            }
            var camGo = new GameObject("cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.72f, .82f, .9f);
            cam.orthographic = true; cam.orthographicSize = 6.6f; cam.aspect = 2600f / 1320f;
            cam.transform.position = new Vector3((keys.Length - 1) * gap / 2, 4.6f, -30); cam.transform.rotation = Quaternion.Euler(3, 0, 0);
            var lightGo = new GameObject("light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(45, 160, 0); l.intensity = 1.3f; l.shadows = LightShadows.Soft;
            int W = 2600, H = 1320; var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_design/dolls/logs/floor_compare.png"), tex.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(mesh); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
}
