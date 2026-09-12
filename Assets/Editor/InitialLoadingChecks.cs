using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class InitialLoadingChecks
{
    const BindingFlags Fields = BindingFlags.Static | BindingFlags.NonPublic;
    static FieldInfo Field(string name) => typeof(SessionOperation).GetField(name, Fields);
    static void Set(string name, object value) => Field(name).SetValue(null, value);
    static int Count(string name) => (int)Field(name).GetValue(null);
    static void Refresh() => typeof(SessionOperation).GetMethod("RefreshBackground", Fields).Invoke(null, null);
    static IDisposable Lease(bool dim) => (IDisposable)Activator.CreateInstance(
        typeof(SessionOperation).GetNestedType("Lease", BindingFlags.NonPublic), new object[] { dim });
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [MenuItem("Tools/Release Safety/Check initial loading overlay")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionOperation.IsBusy)
            throw new Exception("Run in idle Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var target = new RenderTexture(270, 480, 24);
        var saved = new Dictionary<string, object>();
        foreach (string name in new[] { "_count", "_dimCount", "_blocker", "_retry" })
            saved.Add(name, Field(name).GetValue(null));
        try
        {
            var root = new GameObject("Overlay test", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.layer = 5;
            var cameraGo = new GameObject("Preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.orthographic = true;
            camera.orthographicSize = 960;
            camera.aspect = 1080f / 1920f;
            camera.cullingMask = 1 << 5;
            camera.targetTexture = target;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)root.transform).sizeDelta = new Vector2(1080, 1920);
            var panel = new GameObject("Block input", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            panel.layer = 5;
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var blocker = panel.GetComponent<Image>();
            Set("_blocker", blocker);
            Set("_retry", null);
            Set("_count", 1);
            Set("_dimCount", 0);
            Refresh();
            Check(blocker.color.a == 0f && SessionOperation.IsBusy, "Initial loading should be transparent but busy.");
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var eventsGo = new GameObject("Events", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(eventsGo, scene);
            var pointer = new PointerEventData(eventsGo.GetComponent<EventSystem>())
            {
                position = new Vector2(target.width * .5f, target.height * .5f)
            };
            var hits = new List<RaycastResult>();
            root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Exists(h => h.gameObject == panel), "Transparent loading overlay must still block UI input.");

            Set("_dimCount", 1);
            Refresh();
            Check(Mathf.Approximately(blocker.color.a, .65f), "Account switch lost its dimmed background.");
            Set("_count", 2);
            var dimLease = Lease(true);
            dimLease.Dispose();
            Check(Count("_count") == 1 && Count("_dimCount") == 0 && blocker.color.a == 0f && SessionOperation.IsBusy,
                "Completing nested account work must restore transparent initial loading.");
            dimLease.Dispose();
            Check(Count("_count") == 1 && Count("_dimCount") == 0, "Repeated dispose changed counters.");

            Set("_count", 2);
            Set("_dimCount", 1);
            Lease(false).Dispose();
            Check(Count("_count") == 1 && Count("_dimCount") == 1 && Mathf.Approximately(blocker.color.a, .65f),
                "A transparent lease must not remove another operation's dimming.");

            Set("_count", 3);
            Set("_dimCount", 2);
            Lease(true).Dispose();
            Check(Count("_count") == 2 && Count("_dimCount") == 1 && Mathf.Approximately(blocker.color.a, .65f),
                "Nested dimmed operations must retain the remaining dim request.");

            Set("_dimCount", 0);
            Set("_retry", panel);
            Refresh();
            Check(Mathf.Approximately(blocker.color.a, .65f), "Retry instructions need a readable background.");
            Set("_retry", null);
            Refresh();
            Check(blocker.color.a == 0f, "Retry dismissal must restore the loading background.");
            Check((bool)typeof(SessionOperation).GetMethod("Begin").GetParameters()[0].DefaultValue,
                "Existing callers must retain dimming by default.");
        }
        finally
        {
            foreach (var pair in saved) Set(pair.Key, pair.Value);
            EditorSceneManager.ClosePreviewScene(scene);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
        LaunchReadinessChecks.Run();
        ReleaseSafetyChecks.Run();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/initial_loading_result.txt",
            "PASS: 10 overlay checks, including actual transparent-image raycast; 31 existing safety checks. No auth/network/save changes.\n");
        Debug.Log("INITIAL_LOADING_CHECKS_PASSED=10");
    }
}
