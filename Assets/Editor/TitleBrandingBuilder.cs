using System;
using System.IO;
using System.Linq;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TitleBrandingBuilder
{
    const string PrefabPath = "Assets/Resources/UI2/Prefabs/UILoading.prefab";
    const string BackgroundPath = "Assets/Resources/UI2/titleImage.png";
    const string LogoPath = "Assets/Resources/UI2/titleLogo_bbop.png";
    static readonly string[] LogoPaths =
    {
        LogoPath,
        "Assets/Resources/UI2/titleLogo_takepick_en.png",
        "Assets/Resources/UI2/titleLogo_takepick_ja.png",
        "Assets/Resources/UI2/titleLogo_takepick_th.png"
    };
    static readonly LanguageType[] Languages =
        { LanguageType.KOR, LanguageType.ENG, LanguageType.JPN, LanguageType.THA };
    const string Output = ".utmp/title_branding";

    [MenuItem("Tools/Gameplay Polish/Apply Bbop Title")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        AssetDatabase.ImportAsset(BackgroundPath, ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in LogoPaths) ImportLogo(path);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var title = root.GetComponentsInChildren<Image>(true)
                .Single(i => AssetDatabase.GetAssetPath(i.sprite) == BackgroundPath);
            title.gameObject.name = "titleImage";
            title.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            var fitter = title.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = title.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)title.sprite.texture.width / title.sprite.texture.height;
            title.rectTransform.pivot = new Vector2(.5f, .5f);
            var child = title.transform.Find("TitleLogo_Bbop");
            var logoGo = child != null ? child.gameObject : new GameObject("TitleLogo_Bbop", typeof(RectTransform), typeof(Image));
            logoGo.transform.SetParent(title.transform, false);
            logoGo.layer = title.gameObject.layer;
            logoGo.transform.SetAsLastSibling();
            var logo = logoGo.GetComponent<Image>();
            if (logoGo.GetComponent<UILogoIntroAnimation>() == null)
                logoGo.AddComponent<UILogoIntroAnimation>();
            // Grow down from the upper center to keep the 1.4x overshoot inside short portrait screens.
            logo.rectTransform.pivot = new Vector2(.5f, 1f);
            logo.raycastTarget = false;
            logo.preserveAspect = true;
            var localized = logoGo.GetComponent<LocalizedImage>();
            if (localized == null) localized = logoGo.AddComponent<LocalizedImage>();
            var serialized = new SerializedObject(localized);
            var variants = serialized.FindProperty("variants");
            variants.arraySize = LogoPaths.Length;
            serialized.FindProperty("fallbackLanguage").enumValueIndex = (int)LanguageType.ENG;
            for (int i = 0; i < LogoPaths.Length; i++)
            {
                PlaceLogo(title.sprite, logo.rectTransform, LogoPaths[i]);
                var variant = variants.GetArrayElementAtIndex(i);
                variant.FindPropertyRelative("language").enumValueIndex = (int)Languages[i];
                variant.FindPropertyRelative("sprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPaths[i]);
                variant.FindPropertyRelative("overrideAnchors").boolValue = true;
                variant.FindPropertyRelative("anchorMin").vector2Value = logo.rectTransform.anchorMin;
                variant.FindPropertyRelative("anchorMax").vector2Value = logo.rectTransform.anchorMax;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            localized.ApplyLanguage(LanguageType.KOR);
            logo.rectTransform.localScale = Vector3.one;
            logoGo.GetComponent<UILogoIntroAnimation>().Hide();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Validate();
    }

    static void ImportLogo(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = path == LogoPath ? 2048 : 1024;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    static void PlaceLogo(Sprite background, RectTransform rect, string path)
    {
        var image = new Texture2D(2, 2);
        try
        {
            if (!ImageConversion.LoadImage(image, File.ReadAllBytes(path)))
                throw new Exception("Cannot load logo.");
            Rect visible = VisibleRect(image);
            Vector2 backdrop = background.rect.size;
            float scale = Mathf.Min(backdrop.x * .37f / (visible.width * image.width),
                backdrop.y * .108f / (visible.height * image.height));
            Vector2 size = new Vector2(image.width * scale / backdrop.x, image.height * scale / backdrop.y);
            Vector2 alphaCenter = visible.center;
            Vector2 center = new Vector2(.5f, .805f) - Vector2.Scale(alphaCenter - Vector2.one * .5f, size);
            rect.anchorMin = center - size * .5f;
            rect.anchorMax = center + size * .5f;
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }

    static Rect VisibleRect(Texture2D image)
    {
        int minX = image.width, minY = image.height, maxX = -1, maxY = -1;
        var pixels = image.GetPixels32();
        for (int y = 0; y < image.height; y++)
        for (int x = 0; x < image.width; x++)
        {
            if (pixels[y * image.width + x].a < 32) continue;
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
        }
        if (maxX < minX || maxY < minY) throw new Exception("Empty logo.");
        return Rect.MinMaxRect((float)minX / image.width, (float)minY / image.height,
            (float)(maxX + 1) / image.width, (float)(maxY + 1) / image.height);
    }

    [MenuItem("Tools/Gameplay Polish/Validate Bbop Title")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/result.txt", "RUNNING\n");
        for (int i = 0; i < LogoPaths.Length; i++)
        {
            var decoded = new Texture2D(2, 2);
            Rect visible;
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(LogoPaths[i]))) throw new Exception("Cannot load logo.");
                var pixels = decoded.GetPixels32();
                if (!pixels.Any(p => p.a == 0) || !pixels.Any(p => p.a >= 250))
                    throw new Exception("Logo must have actual alpha transparency: " + LogoPaths[i]);
                visible = VisibleRect(decoded);
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
            Preview(540, 960, visible, Languages[i]);
            Preview(648, 1404, visible, Languages[i]);
            Preview(768, 1024, visible, Languages[i]);
            if (Languages[i] == LanguageType.KOR)
            {
                Preview(540, 960, visible, Languages[i], 0f);
                Preview(540, 960, visible, Languages[i], .30f);
                Preview(540, 960, visible, Languages[i], .53f);
            }
        }
        File.WriteAllText(Output + "/result.txt", "PASS: 4 localized logos x 3 viewports; alpha, switching, fallback, hierarchy and nonblank renders. No player-save writes.\n");
        Debug.Log("TITLE_BRANDING_CHECKS_PASSED");
    }

    static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var points = corners.Select(p => camera.WorldToScreenPoint(p)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }

    static void Preview(int width, int height, Rect visible, LanguageType language, float introProgress = 1f)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(width, height, 24);
        try
        {
            var cameraGo = new GameObject("Title preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = height * 1080f / width * .5f;
            camera.aspect = (float)width / height;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.targetTexture = rt;
            var canvasGo = new GameObject("Title preview canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080, height * 1080f / width);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            root.transform.SetParent(canvas.transform, false);
            // Match the initial loading state; login and touch-to-start are mutually exclusive at runtime.
            var loading = new SerializedObject(root.GetComponent<FunRabbit.UILoading>());
            ((GameObject)loading.FindProperty("_touchToStart").objectReferenceValue).SetActive(false);
            ((Button)loading.FindProperty("_guestLoginBtn").objectReferenceValue).gameObject.SetActive(false);
            ((Button)loading.FindProperty("_googleLoginBtn").objectReferenceValue).gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            var title = root.transform.Find("titleImage").GetComponent<Image>();
            var logo = title.transform.Find("TitleLogo_Bbop").GetComponent<Image>();
            var localized = logo.GetComponent<LocalizedImage>();
            if (localized == null) throw new Exception("LocalizedImage is missing.");
            foreach (var next in Languages.Concat(new[] { (LanguageType)999, language }))
            {
                if (!localized.ApplyLanguage(next)) throw new Exception("Language selection failed.");
                int index = Array.IndexOf(Languages, next);
                if (index < 0) index = 1;
                if (AssetDatabase.GetAssetPath(logo.sprite) != LogoPaths[index])
                    throw new Exception("Wrong localized sprite.");
            }
            var settings = new SerializedObject(localized);
            var missing = settings.FindProperty("variants").GetArrayElementAtIndex(2).FindPropertyRelative("sprite");
            var japanese = missing.objectReferenceValue;
            missing.objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (!localized.ApplyLanguage(LanguageType.JPN) || AssetDatabase.GetAssetPath(logo.sprite) != LogoPaths[1])
                throw new Exception("Missing sprite did not fall back to English.");
            missing.objectReferenceValue = japanese;
            settings.ApplyModifiedPropertiesWithoutUndo();
            localized.ApplyLanguage(language);
            var fitter = title.GetComponent<AspectRatioFitter>();
            fitter.SetLayoutHorizontal();
            fitter.SetLayoutVertical();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
            Canvas.ForceUpdateCanvases();
            var background = Bounds(title.rectTransform, camera);
            var label = Bounds(logo.rectTransform, camera);
            label = Rect.MinMaxRect(Mathf.Lerp(label.xMin, label.xMax, visible.xMin),
                Mathf.Lerp(label.yMin, label.yMax, visible.yMin),
                Mathf.Lerp(label.xMin, label.xMax, visible.xMax),
                Mathf.Lerp(label.yMin, label.yMax, visible.yMax));
            if (background.xMin > .1f || background.yMin > .1f || background.xMax < width - .1f || background.yMax < height - .1f)
                throw new Exception("Title background does not cover the viewport.");
            if (label.xMin < 0 || label.yMin < 0 || label.xMax > width || label.yMax > height)
                throw new Exception("Visible logo is outside the viewport: " + width + "x" + height + " " + label);
            if (logo.sprite == null || logo.raycastTarget || !logo.preserveAspect)
                throw new Exception("Invalid logo setup.");
            var intro = logo.GetComponent<UILogoIntroAnimation>();
            if (intro == null) throw new Exception("Logo intro animation is missing.");
            float duration = new SerializedObject(intro).FindProperty("duration").floatValue;
            foreach (float pose in new[] { 0f, .30f, .53f, .70f, .85f, 1f })
            {
                intro.Play();
                intro.Advance(duration * pose);
                var bounds = Bounds(logo.rectTransform, camera);
                var letters = Rect.MinMaxRect(Mathf.Lerp(bounds.xMin, bounds.xMax, visible.xMin),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, visible.yMin),
                    Mathf.Lerp(bounds.xMin, bounds.xMax, visible.xMax),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, visible.yMax));
                if (letters.xMin < 0 || letters.yMin < 0 || letters.xMax > width || letters.yMax > height)
                    throw new Exception("Animated logo clipped: " + language + " " + width + "x" + height + " pose=" + pose);
            }
            intro.Play();
            intro.Advance(duration * introProgress);
            camera.Render();
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = rt;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                if (image.GetPixels32().Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Take(100).Count() < 100)
                    throw new Exception("Blank title preview.");
                string poseSuffix = introProgress < 1f ? "_intro_" + Mathf.RoundToInt(introProgress * 100f) : "";
                File.WriteAllBytes(Output + "/title_" + language + "_" + width + "x" + height + poseSuffix + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
