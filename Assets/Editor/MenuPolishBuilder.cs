using System;
using System.IO;
using System.Linq;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MenuPolishBuilder
{
    public const string Folder = "Assets/Resources/UI2/Prefabs/";
    public const string MessagePath = Folder + "UIMissionCompleteMessage.prefab";
    public static readonly string[] Headers = { "UIHud", "UICollectionPanel", "UIRandomboxPanel", "UIShopPanel" };

    [MenuItem("Tools/Gameplay Polish/Apply menu and mission presentation")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        foreach (var name in Headers)
        {
            var root = PrefabUtility.LoadPrefabContents(Folder + name + ".prefab");
            try
            {
                var top = root.transform.Find(name == "UIHud" ? "uiTopbar" : "Top");
                if (top.GetComponent<UITopbarSafeArea>() == null) top.gameObject.AddComponent<UITopbarSafeArea>();
                PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        BuildMessage();
        MenuPolishChecks.Run();
    }

    static void BuildMessage()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "UIHud.prefab");
        var combo = hud.GetComponentInChildren<UIComboHud>(true);
        var source = new SerializedObject(combo);
        var image = ((GameObject)source.FindProperty("comboDoubleImage").objectReferenceValue).GetComponent<Image>();
        var text = (TextMeshProUGUI)source.FindProperty("comboText").objectReferenceValue;
        var root = new GameObject("UIMissionCompleteMessage", typeof(RectTransform), typeof(CanvasGroup), typeof(UIMissionCompleteMessage));
        try
        {
            root.layer = 5;
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
            var banner = new GameObject("MissionBanner", typeof(RectTransform), typeof(UITopbarSafeArea));
            banner.layer = 5;
            banner.transform.SetParent(root.transform, false);
            var bannerRect = (RectTransform)banner.transform;
            bannerRect.anchorMin = new Vector2(.08f, 1);
            bannerRect.anchorMax = new Vector2(.92f, 1);
            bannerRect.pivot = new Vector2(.5f, 1);
            bannerRect.anchoredPosition = new Vector2(0, -170);
            bannerRect.sizeDelta = new Vector2(0, 220);
            var background = UnityEngine.Object.Instantiate(image.gameObject, banner.transform, false);
            background.name = "ComboStyleBackground";
            background.SetActive(true);
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.raycastTarget = false;
            backgroundImage.preserveAspect = true;
            Stretch(backgroundImage.rectTransform, Vector2.zero, Vector2.zero);
            var label = UnityEngine.Object.Instantiate(text.gameObject, banner.transform, false).GetComponent<TextMeshProUGUI>();
            label.gameObject.name = "MissionCompleteText";
            label.gameObject.SetActive(true);
            label.text = "미션 클리어";
            label.enableAutoSizing = true;
            label.fontSizeMin = 20;
            label.fontSizeMax = 64;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.characterSpacing = 0;
            Stretch(label.rectTransform, new Vector2(65, 45), new Vector2(-65, -45));
            var serialized = new SerializedObject(root.GetComponent<UIMissionCompleteMessage>());
            serialized.FindProperty("banner").objectReferenceValue = bannerRect;
            serialized.FindProperty("canvasGroup").objectReferenceValue = group;
            serialized.FindProperty("messageText").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, MessagePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
        rect.localScale = Vector3.one;
        var pos = rect.localPosition;
        pos.z = 0;
        rect.localPosition = pos;
    }
}
