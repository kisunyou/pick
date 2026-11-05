using System.IO;
using System.Linq;
using FunRabbit;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LapTwoCollectionPreview
{
    [MenuItem("Tools/New Dolls/Capture Lap Two Collection")]
    public static void Run() => Capture(18, new[] { 13, 19 });

    public static void Capture(int maxCleared, int[] startStages)
    {
        var scene=EditorSceneManager.NewPreviewScene(); var rt=new RenderTexture(1080,1920,24);
        try
        {
            var camGo=new GameObject("CollectionPreviewCamera");SceneManager.MoveGameObjectToScene(camGo,scene);
            var cam=camGo.AddComponent<Camera>();cam.scene=scene;cam.targetTexture=rt;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.15f,.19f,.23f);
            cam.transform.position=new Vector3(0,0,-10);cam.orthographic=true;cam.orthographicSize=5;cam.farClipPlane=100;
            var canvasGo=new GameObject("CollectionPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasGo,scene);
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=10;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1080,1920);scaler.matchWidthOrHeight=0;
            var root=(GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("UI2/Prefabs/UICollectionPanel"),scene);
            root.transform.SetParent(canvasGo.transform,false);
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            foreach(var group in root.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
            GameActorData.Load();var rows=GameActorData.Actors.OrderBy(r=>r.stage).ToArray();
            var names=(JArray)JObject.Parse(File.ReadAllText("Assets/Resources/Table/stringData.json"))["stringData"];
            var data=rows.Select(r=>new UICollectionItemData
            {
                title=(string)names.First(n=>(string)n["key"]==r.nameKey)["kor"],animalKey=r.animalKey,
                modelPath=r.stage<=maxCleared?GameCommon.GetModelPrefabFullPath(r.animalKey):GameCommon.GetBossModelPrefabFullPath(r.animalKey),active=r.stage<=maxCleared
            }).ToArray();
            var panel=root.GetComponent<UICollectionPanel>();panel.SetCollectionItems(data);
            var slots=root.GetComponentsInChildren<UICollectionItem>(true);
            for(int i=0;i<slots.Length;i++)
            {
                var so=new SerializedObject(slots[i]);var image=(Image)so.FindProperty("thumbnailIcon").objectReferenceValue;
                image.sprite=Resources.Load<Sprite>(rows[i].stage<=maxCleared?GameCommon.GetIconFullPath(rows[i].animalKey):GameCommon.GetBossIconFullPath(rows[i].animalKey));
                image.SetNativeSize();image.gameObject.SetActive(true);
            }
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(rect);Canvas.ForceUpdateCanvases();
            var scroll=root.GetComponentInChildren<ScrollRect>(true);var grid=scroll.content.GetComponent<GridLayoutGroup>();
            int columns=Mathf.Max(1,Mathf.FloorToInt((scroll.content.rect.width+grid.spacing.x)/(grid.cellSize.x+grid.spacing.x)));
            Directory.CreateDirectory("_design/dolls/review");
            foreach(int start in startStages)
            {
                scroll.content.anchoredPosition=new Vector2(scroll.content.anchoredPosition.x,((start-1)/columns)*(grid.cellSize.y+grid.spacing.y));
                Canvas.ForceUpdateCanvases();cam.Render();var prev=RenderTexture.active;RenderTexture.active=rt;
                var image=new Texture2D(1080,1920,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1080,1920),0,0);image.Apply();
                RenderTexture.active=prev;File.WriteAllBytes("_design/dolls/review/collection_stage_"+start+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
            }
            File.WriteAllText("_design/dolls/logs/collection_preview_"+startStages[0]+".txt","Real UICollectionPanel prefab, 36 populated entries, columns="+columns+", starts="+string.Join(",",startStages)+", max cleared="+maxCleared+".\n");
        }
        finally {rt.Release();Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
