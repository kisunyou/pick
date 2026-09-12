using System;
using System.IO;
using System.Linq;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class HudSafeAreaChecks
{
    const string Output = ".utmp/hud_safe_area";
    static int _checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    static T Field<T>(UnityEngine.Object obj, string name) where T : UnityEngine.Object =>
        (T)new SerializedObject(obj).FindProperty(name).objectReferenceValue;

    [MenuItem("Tools/Gameplay Polish/Check HUD Safe Area")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _checks = 0;
        Directory.CreateDirectory(Output);
        Preview(648, 1404, new Rect(0, 24, 648, 1330), true);
        Preview(1080, 1920, new Rect(0, 0, 1080, 1920), true);
        Preview(1440, 3120, new Rect(0, 80, 1440, 2920), false);
        Preview(2340, 1080, new Rect(100, 30, 2160, 1050), false);
        File.WriteAllText(Output + "/result.txt", "PASS: " + _checks +
            " checks; four resolutions, simulated top/side cutouts, repeated updates, reset and rendered HUD. No device or player-save writes.\n");
        Debug.Log("HUD_SAFE_AREA_CHECKS_PASSED=" + _checks);
    }

    public static void RunBatch()
    {
        try
        {
            Run();
            LaunchReadinessChecks.Run();
            GameplayAnalyticsChecks.Run();
            WaveBalanceChecks.Run();
            // This older runner exits Unity automatically in batch mode, so it must run last.
            ReleaseSafetyChecks.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/result.txt", "FAIL: " + e);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var points = corners.Select(p => camera.WorldToScreenPoint(p)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }

    static void Preview(int width, int height, Rect safe, bool render)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(width, height, 24);
        Texture2D battleTexture = null;
        try
        {
            float hudHeight = height * 1080f / width;
            var cameraGo = new GameObject("HUDPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = hudHeight * .5f;
            camera.aspect = (float)width / height;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.cullingMask = 1 << 5;
            camera.targetTexture = rt;
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.layer = 5;
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080, hudHeight);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UIHud.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            root.transform.SetParent(canvasGo.transform, false);
            var hud = root.GetComponent<UIHud>();
            var topbar = root.GetComponentInChildren<UITopbar>(true);
            var back = Field<Button>(topbar, "backButton");
            var barRect = (RectTransform)topbar.transform;
            Canvas.ForceUpdateCanvases();
            Vector2 originalMin = barRect.offsetMin, originalMax = barRect.offsetMax;
            Check(Mathf.Abs(hud.BossCamViewRect.rect.height - 320f) < .01f, "Prefab height not reduced.");

            hud.ApplyScreenLayout(new Vector2(width, height), safe);
            Canvas.ForceUpdateCanvases();
            Rect imageBounds = Bounds(hud.BossCamViewRect, camera);
            float topPixels = height - safe.yMax;
            float expectedHeight = (400f * width / 1080f + topPixels) * .8f;
            Check(Mathf.Abs(imageBounds.yMax - height) < .1f, "Gap above render texture.");
            Check(Mathf.Abs(imageBounds.xMin) < .1f && Mathf.Abs(imageBounds.xMax - width) < .1f, "Image not full width.");
            Check(Mathf.Abs(imageBounds.height - expectedHeight) < .1f, "Image height was not reduced by 20%.");
            Rect buttonBounds = Bounds((RectTransform)back.transform, camera);
            Rect topbarBounds = Bounds(barRect, camera);
            Check(buttonBounds.yMax <= safe.yMax + .1f && buttonBounds.xMin >= safe.xMin - .1f &&
                buttonBounds.xMax <= safe.xMax + .1f, "Back button outside safe area.");
            Check(topbarBounds.yMax <= safe.yMax + .1f && topbarBounds.xMin >= safe.xMin - .1f &&
                topbarBounds.xMax <= safe.xMax + .1f, "Topbar outside safe area.");

            Rect gaugeBounds = Bounds((RectTransform)Field<UICustumGage>(hud, "bossHPGage").transform, camera);
            Check(Mathf.Abs(gaugeBounds.center.y - (height - expectedHeight - 6f * width / 1080f)) < .1f, "Gauge spacing changed.");
            Rect missionBounds = Bounds(Field<RectTransform>(hud, "missionHudRect"), camera);
            Rect boxBounds = Bounds(Field<RectTransform>(hud, "randomBoxButtonRect"), camera);
            Check(Mathf.Abs(missionBounds.center.y - (height - expectedHeight - 100f * width / 1080f)) < .1f, "Mission did not follow the picture.");
            Check(Mathf.Abs(boxBounds.center.y - (height - expectedHeight - 162f * width / 1080f)) < .1f, "Box button did not follow the picture.");
            Vector2 paddedMin = barRect.offsetMin, paddedMax = barRect.offsetMax;
            for (int i = 0; i < 10; i++) hud.ApplyScreenLayout(new Vector2(width, height), safe);
            Check((paddedMin - barRect.offsetMin).sqrMagnitude < .001f &&
                (paddedMax - barRect.offsetMax).sqrMagnitude < .001f, "Insets accumulate.");
            hud.ApplyScreenLayout(new Vector2(width, height), new Rect(0, 0, width, height));
            Check((originalMin - barRect.offsetMin).sqrMagnitude < .001f &&
                (originalMax - barRect.offsetMax).sqrMagnitude < .001f, "No-cutout layout not restored.");
            Check(Mathf.Abs(hud.BossCamViewRect.rect.height - 320f) < .01f, "No-cutout picture height incorrect.");
            hud.ApplyScreenLayout(new Vector2(width, height), new Rect());
            Check((originalMax - barRect.offsetMax).sqrMagnitude < .001f, "Invalid safe area not ignored.");
            hud.ApplyScreenLayout(new Vector2(width, height), safe);

            if (!render) return;
            foreach (Transform child in root.transform)
                child.gameObject.SetActive(child.name == "bossBattle" || child.name == "uiTopbar");
            Field<GameObject>(topbar, "topbarBack").SetActive(false);
            Field<TextMeshProUGUI>(topbar, "topbarTitle").text = "Collection";
            battleTexture = RenderBattle(Mathf.RoundToInt(hud.BossCamViewRect.rect.height));
            Field<RawImage>(hud, "bossCamView").texture = battleTexture;
            Canvas.ForceUpdateCanvases();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
            camera.Render();
            var image = Read(rt);
            try
            {
                File.WriteAllBytes(Output + "/full_" + width + "x" + height + ".png", image.EncodeToPNG());
                Check(image.GetPixels32().Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Take(100).Count() == 100, "Blank HUD.");
                // The top-center must sample the battle picture, never the magenta main-camera background.
                Color pixel = image.GetPixel(width / 2, height - 2);
                Check(!(pixel.r > .95f && pixel.g < .05f && pixel.b > .95f), "Visible gap above battle image.");
                // Export the top UI only; the magenta area below is deliberately outside this preview.
                int cropHeight = Mathf.Min(height, Mathf.CeilToInt(expectedHeight));
                var crop = new Texture2D(width, cropHeight, TextureFormat.RGB24, false);
                try
                {
                    crop.SetPixels(image.GetPixels(0, height - cropHeight, width, cropHeight));
                    crop.Apply();
                    File.WriteAllBytes(Output + "/hud_" + width + "x" + height + ".png", crop.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(crop); }
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }

            hud.SetActiveHud(GameStatus.COLLECTION);
            Field<GameObject>(topbar, "topbarBack").SetActive(true);
            Canvas.ForceUpdateCanvases();
            Check(!Field<GameObject>(hud, "bossBattle").activeSelf, "Battle visible behind collection bar.");
            Rect titleBounds = Bounds(Field<TextMeshProUGUI>(topbar, "topbarTitle").rectTransform, camera);
            Check(titleBounds.yMax <= safe.yMax + .1f && titleBounds.xMin >= safe.xMin - .1f &&
                titleBounds.xMax <= safe.xMax + .1f, "Collection title outside safe area.");
            Check(back.gameObject.activeInHierarchy && back.interactable, "Back button disabled in collection.");
            camera.backgroundColor = new Color(.15f, .2f, .25f);
            camera.Render();
            var collection = Read(rt);
            try
            {
                int cropHeight = Mathf.Min(height, Mathf.CeilToInt(height - Bounds(barRect, camera).yMin));
                var crop = new Texture2D(width, cropHeight, TextureFormat.RGB24, false);
                try
                {
                    crop.SetPixels(collection.GetPixels(0, height - cropHeight, width, cropHeight));
                    crop.Apply();
                    File.WriteAllBytes(Output + "/collection_" + width + "x" + height + ".png", crop.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(crop); }
            }
            finally { UnityEngine.Object.DestroyImmediate(collection); }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            if (battleTexture != null) UnityEngine.Object.DestroyImmediate(battleTexture);
        }
    }

    static Texture2D Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            return image;
        }
        finally { RenderTexture.active = previous; }
    }

    static Texture2D RenderBattle(int height)
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Stage0.unity");
        var rt = new RenderTexture(1080, height, 24);
        try
        {
            var roots = scene.GetRootGameObjects();
            var battle = roots.SelectMany(r => r.GetComponentsInChildren<ActorBattleSystem>(true)).First();
            var bossCamera = roots.SelectMany(r => r.GetComponentsInChildren<BossCamera>(true)).First();
            var camera = bossCamera.Cam;
            camera.scene = scene;
            camera.targetTexture = rt;
            var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            foreach (var overlay in cameraData.cameraStack) if (overlay != null) overlay.targetTexture = rt;
            var serialized = new SerializedObject(battle);
            var pivot = (Transform)serialized.FindProperty("bossTransform").objectReferenceValue;
            var entries = serialized.FindProperty("allyTransforms");
            var anchors = Enumerable.Range(0, entries.arraySize)
                .Select(i => (Transform)entries.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/dollPrefabs/doll_cow_mon_prefab.prefab"), scene);
            model.transform.SetParent(pivot, false);
            model.transform.localScale = Vector3.one * 1.09f;
            bossCamera.SetViewAspect(1080f / height);
            bossCamera.FrameBattle(model.transform, anchors);
            camera.Render();
            var image = Read(rt);
            Check(image.GetPixels32().Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Take(100).Count() == 100,
                "Blank battle texture.");
            return image;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
