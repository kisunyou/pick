using System;
using System.IO;
using System.Linq;
using System.Text;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GameplayPolishBuilder
{
    const string Destination = "Assets/Resources/UI2/Prefabs/UICoinShortPopup.prefab";
    static T Find<T>(GameObject root, string name) where T : Component =>
        root.GetComponentsInChildren<T>(true).First(c => c.gameObject.name == name);
    static void Assign(SerializedObject obj, string name, UnityEngine.Object value) =>
        obj.FindProperty(name).objectReferenceValue = value;
    static void Rect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f,.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
    static void Label(Button button, string key, string text, bool withIcon)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        var localized = label.GetComponent<LocalizedText>();
        if (localized != null)
        {
            var data = new SerializedObject(localized);
            data.FindProperty("key").stringValue = key;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        label.text = text;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24;
        label.fontSizeMax = 44;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(withIcon ? 104 : 16, 14);
        label.rectTransform.offsetMax = new Vector2(-16, -14);
        label.raycastTarget = false;
    }
    static void Icon(Button button,string path)
    {
        var go = new GameObject("ActionIcon",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
        go.layer = button.gameObject.layer;
        go.transform.SetParent(button.transform,false);
        var image = go.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if(image.sprite == null)throw new Exception("Missing icon: "+path);
        image.preserveAspect = true;
        image.raycastTarget = false;
        Rect(image.rectTransform,new Vector2(0,.5f),new Vector2(56,4),new Vector2(68,68));
    }

    [MenuItem("Tools/Gameplay Polish/Build Coin Popup")]
    public static void Build()
    {
        var root=PrefabUtility.LoadPrefabContents("Assets/Resources/UI2/Prefabs/UIPopup.prefab");
        try
        {
            root.name="UICoinShortPopup";
            var old=root.GetComponent<UIPopup>();
            var data=new SerializedObject(old);
            var title=(TMP_Text)data.FindProperty("titleText").objectReferenceValue;
            var description=(TMP_Text)data.FindProperty("descriptionText").objectReferenceValue;
            var purchase=(Button)data.FindProperty("okButton").objectReferenceValue;
            var cancel=(Button)data.FindProperty("cancelButton").objectReferenceValue;
            var close=(Button)data.FindProperty("closeButton").objectReferenceValue;
            var dimmed=(Button)data.FindProperty("dimedButton").objectReferenceValue;
            var coin=(Image)data.FindProperty("coinIcon").objectReferenceValue;
            if(coin!=null)coin.gameObject.SetActive(false);
            UnityEngine.Object.DestroyImmediate(old);
            var popup=root.AddComponent<UICoinShortPopup>();
            var panel=Find<RectTransform>(root,"Popup");
            Rect(panel,new Vector2(.5f,.5f),Vector2.zero,new Vector2(940,800));
            var line=Find<RectTransform>(root,"TitleLine");
            line.sizeDelta=new Vector2(880,36);
            Rect(description.rectTransform,new Vector2(.5f,.5f),new Vector2(0,125),new Vector2(810,170));
            description.text="플레이에 코인 100개가 필요합니다.";
            description.enableAutoSizing=true;description.fontSizeMin=26;description.fontSizeMax=40;
            var buttons=Find<RectTransform>(root,"buttons");
            Rect(buttons,new Vector2(.5f,.5f),new Vector2(0,-70),new Vector2(824,150));
            cancel.transform.SetParent(panel,false);
            var cancelLayout=cancel.GetComponent<LayoutElement>();if(cancelLayout!=null)UnityEngine.Object.DestroyImmediate(cancelLayout);
            Rect((RectTransform)cancel.transform,new Vector2(.5f,.5f),new Vector2(0,-265),new Vector2(350,120));
            var ad=UnityEngine.Object.Instantiate(purchase,buttons);
            purchase.name="purchaseButton";ad.name="adButton";
            Label(purchase,"button_purchase","구매",true);
            Label(ad,"button_watch_ad","광고보기",true);
            Label(cancel,"button_cancel","취소",false);
            Icon(purchase,"Assets/Resources/UI2/Images/Components/Icon_ShopItems/ShopItem_Coin_1.png");
            Icon(ad,"Assets/Resources/UI2/Images/Components/IconMisc/Icon_ImageIcon_Ad_00_l.png");
            var status=UnityEngine.Object.Instantiate(description,panel);
            status.name="adStatusText";
            Rect(status.rectTransform,new Vector2(.5f,.5f),new Vector2(0,-175),new Vector2(820,60));
            status.fontSizeMin=20;status.fontSizeMax=28;
            status.text="500 코인 · 남은 횟수 10";
            title.text="코인 부족";
            var serialized=new SerializedObject(popup);
            Assign(serialized,"panel",panel);Assign(serialized,"titleText",title);Assign(serialized,"descriptionText",description);
            Assign(serialized,"adStatusText",status);Assign(serialized,"purchaseButton",purchase);Assign(serialized,"adButton",ad);
            Assign(serialized,"cancelButton",cancel);Assign(serialized,"closeButton",close);Assign(serialized,"dimmedButton",dimmed);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,Destination);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Inspect();
        Debug.Log("[GameplayPolish] Coin popup built.");
    }

    [MenuItem("Tools/Gameplay Polish/Update Battle HUD")]
    public static void UpdateHud()
    {
        const string path="Assets/Resources/UI2/Prefabs/UIHud.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var data=new SerializedObject(root.GetComponent<UIHud>());
            var mission=Find<RectTransform>(root,"uiMissionHud");
            var box=Find<RectTransform>(root,"openRandomBoxPanelButton");
            Assign(data,"missionHudRect",mission);Assign(data,"randomBoxButtonRect",box);
            data.ApplyModifiedPropertiesWithoutUndo();
            mission.anchoredPosition=new Vector2(mission.anchoredPosition.x,-420);
            box.anchoredPosition=new Vector2(box.anchoredPosition.x,-482);
            Find<RectTransform>(root,"bossCamView").sizeDelta=new Vector2(0,320);
            Find<RectTransform>(root,"bossHPGage").anchoredPosition=new Vector2(0,-326);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Gameplay Polish/Inspect")]
    public static void Inspect()
    {
        var text=new StringBuilder();
        text.AppendLine("Playing: "+EditorApplication.isPlaying);
        var hud=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UIHud.prefab");
        foreach(var rect in hud.GetComponentsInChildren<RectTransform>(true))
            if(rect.anchorMin.y>=.9f || rect.name.Contains("boss") || rect.name.Contains("mission"))
                text.AppendLine(rect.name+" parent="+rect.parent?.name+" anchor="+rect.anchorMin+"/"+rect.anchorMax+" pos="+rect.anchoredPosition+" size="+rect.sizeDelta);
        Directory.CreateDirectory(".utmp/gameplay_polish");
        File.WriteAllText(".utmp/gameplay_polish/inspection.txt",text.ToString());
    }
}
