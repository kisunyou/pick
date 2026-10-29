using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MenuPolishChecks
{
    const string Output = ".utmp/menu_polish";
    static int _checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    [MenuItem("Tools/Gameplay Polish/Check menu and mission presentation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/result.txt", "RUNNING\n");
        _checks = 0;
        CheckStageModels();
        Preview(648, 1404, new Rect(0, 24, 648, 1330));
        Preview(1080, 1920, new Rect(0, 0, 1080, 1920));
        Preview(1440, 3120, new Rect(20, 80, 1400, 2920));
        HudSafeAreaChecks.Run();
        WaveBalanceChecks.Run();
        File.WriteAllText(Output + "/result.txt", "PASS: " + _checks + " assertions; consistent menu header insets, no cumulative offsets, 4 localized mission banners x 3 viewports, independent combo/mission components, horizontal drag reversal, all 36 stage stats preserved, 1/3 and repeated-band model/icon/texture alignment. No player-save writes.\n");
        Debug.Log("MENU_POLISH_CHECKS_PASSED=" + _checks);
    }

    static void CheckStageModels()
    {
        GameActorData.Load();
        GameQuestData.Load();
        var baseline = JsonUtility.FromJson<ActorDataList>(File.ReadAllText("_report/2026-09-12_wave_balance_candidate.json")).actors;
        Check(GameActorData.Actors.Count == 36, "Stage count changed.");
        string[] visuals = { "animalKey", "nameKey", "model", "texture", "sound", "inGameScale", "collectionScale" };
        foreach (var row in GameActorData.Actors)
        {
            int visualStage = row.stage;
            if (new[] { 1, 13, 25 }.Contains(row.stage)) visualStage += 2;
            if (new[] { 3, 15, 27 }.Contains(row.stage)) visualStage -= 2;
            var original = baseline.Single(a => a.stage == row.stage);
            var visual = baseline.Single(a => a.stage == visualStage);
            foreach (var field in typeof(ActorData).GetFields())
                Check(Equals(field.GetValue(row), field.GetValue(visuals.Contains(field.Name) ? visual : original)),
                    "Stage field mismatch: " + row.stage + " " + field.Name);
            Check(Resources.Load<GameObject>(GameCommon.GetModelPrefabFullPath(row.animalKey)) != null, "Missing ally prefab.");
            Check(Resources.Load<GameObject>(GameCommon.GetBossModelPrefabFullPath(row.animalKey)) != null, "Missing boss prefab.");
            Check(Resources.Load<Sprite>(GameCommon.GetIconFullPath(row.animalKey)) != null, "Missing collection icon.");
            if (!string.IsNullOrEmpty(row.texture)) Check(Resources.Load<Texture2D>(row.texture) != null, "Missing variant texture.");
            int baseStage = (row.stage - 1) % 12 + 1;
            Check(GameCommon.GetBaseAnimalKey(row.animalKey) == GameQuestData.GetStage(baseStage).animalKey,
                "Collection grade points at a different animal.");
        }
    }

    static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var points = corners.Select(camera.WorldToScreenPoint).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }

    static void Preview(int width, int height, Rect safe)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(width, height, 24);
        try
        {
            float canvasHeight = height * 1080f / width;
            var cameraGo = new GameObject("Preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = canvasHeight * .5f;
            camera.aspect = (float)width / height;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.farClipPlane = 1000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .3f, .32f);
            camera.cullingMask = 1 << 5;
            camera.targetTexture = rt;
            var canvasGo = new GameObject("Preview canvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.layer = 5;
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080, canvasHeight);
            Rect? reference = null;
            foreach (string name in MenuPolishBuilder.Headers)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPolishBuilder.Folder + name + ".prefab");
                var top = asset.transform.Find(name == "UIHud" ? "uiTopbar" : "Top");
                Check(top.parent == asset.transform, "Header is not attached to the full-screen panel: " + name);
                var clone = UnityEngine.Object.Instantiate(top.gameObject, canvas.transform, false);
                clone.SetActive(true);
                var layout = clone.GetComponent<UITopbarSafeArea>();
                Check(layout != null, "Menu header safe-area component missing: " + name);
                layout.ApplyScreenLayout(new Vector2(width, height), safe);
                Canvas.ForceUpdateCanvases();
                var bounds = Bounds((RectTransform)clone.transform, camera);
                if (reference.HasValue)
                    Check((bounds.position - reference.Value.position).sqrMagnitude < .01f && (bounds.size - reference.Value.size).sqrMagnitude < .01f,
                        "Menu header is not aligned with collection: " + name);
                else reference = bounds;
                layout.ApplyScreenLayout(new Vector2(width, height), safe);
                Check((Bounds((RectTransform)clone.transform, camera).position - bounds.position).sqrMagnitude < .01f, "Safe area accumulated.");
                layout.ApplyScreenLayout(new Vector2(width, height), new Rect(0, 0, width, height));
                Check(Mathf.Abs(Bounds((RectTransform)clone.transform, camera).yMax - height) < 1, "Header did not reset to original position.");
                UnityEngine.Object.DestroyImmediate(clone);
            }
            var message = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MenuPolishBuilder.MessagePath), scene);
            message.transform.SetParent(canvas.transform, false);
            Check(message.GetComponentInChildren<UIComboHud>(true) == null, "Mission banner contains combo logic.");
            var view = message.GetComponent<UIMissionCompleteMessage>();
            var banner = message.transform.Find("MissionBanner").GetComponent<RectTransform>();
            var label = message.GetComponentInChildren<TextMeshProUGUI>(true);
            var group = message.GetComponent<CanvasGroup>();
            Check(!group.blocksRaycasts && !group.interactable, "Mission message blocks gameplay input.");
            var rows = JsonUtility.FromJson<StringDataList>(File.ReadAllText("Assets/Resources/Table/stringData.json")).stringData;
            var row = rows.Single(r => r.key == "mission_complete_message");
            int language = 0;
            foreach (string text in new[] { row.kor, row.eng, row.jpn, row.tha })
            {
                Check(!string.IsNullOrWhiteSpace(text), "Missing mission translation.");
                view.PlayMessage(text);
                view.OnClose();
                banner.GetComponent<UITopbarSafeArea>().ApplyScreenLayout(new Vector2(width, height), safe);
                Canvas.ForceUpdateCanvases();
                label.ForceMeshUpdate();
                Check(!label.isTextOverflowing, "Mission text overflow: " + text);
                Rect bounds = Bounds(banner, camera);
                Check(bounds.xMin >= 0 && bounds.xMax <= width && bounds.yMin >= 0 && bounds.yMax <= safe.yMax,
                    "Mission banner outside safe bounds.");
                Capture(camera, rt, Output + "/mission_" + width + "x" + height + "_" + language++ + ".png");
            }
            var dragGo = new GameObject("Collection drag test", typeof(CollectionCamera));
            SceneManager.MoveGameObjectToScene(dragGo, scene);
            var drag = dragGo.GetComponent<CollectionCamera>();
            var serialized = new SerializedObject(drag);
            serialized.FindProperty("_mainCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var method = typeof(CollectionCamera).GetMethod("ScreenDeltaToWorldXZ", BindingFlags.NonPublic | BindingFlags.Instance);
            Check((Vector3)method.Invoke(drag, new object[] { Vector2.right }) == Vector3.left, "Horizontal drag not reversed.");
            Check((Vector3)method.Invoke(drag, new object[] { Vector2.up }) == Vector3.forward, "Vertical drag changed.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    static void Capture(Camera camera, RenderTexture rt, string path)
    {
        camera.Render();
        var previous = RenderTexture.active;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Check(image.GetPixels32().Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Take(50).Count() == 50, "Blank message render: " + path);
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
