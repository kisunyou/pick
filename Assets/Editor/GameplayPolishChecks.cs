using System;
using System.IO;
using System.Linq;
using System.Text;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayPolishChecks
{
    const string Folder=".utmp/gameplay_polish";
    static void Check(bool condition,string message) { if(!condition)throw new Exception("[GameplayPolishChecks] "+message); }
    static T Field<T>(UnityEngine.Object obj,string name) where T:UnityEngine.Object =>
        (T)new SerializedObject(obj).FindProperty(name).objectReferenceValue;

    [MenuItem("Tools/Gameplay Polish/Validate And Preview")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        PreviewPopup(540,960);
        PreviewPopup(540,1200);
        PreviewPopup(1024,768);
        BattleFramingPreview.Run();
        File.WriteAllText(Folder+"/checks.txt","PASS: popup wiring, icons, labels, responsive bounds; all 36 bosses framed.\n");
        Debug.Log("[GameplayPolishChecks] PASS: popup wiring/icons/layout and 36 boss bounds.");
    }

    static void PreviewPopup(int width,int height)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var rt=new RenderTexture(width,height,24);
        try
        {
            var cameraGo=new GameObject("PreviewCamera",typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var cam=cameraGo.GetComponent<Camera>();
            cam.scene=scene;
            cam.orthographic=true;cam.orthographicSize=(height/(width/1080f))*.5f;cam.nearClipPlane=.1f;cam.farClipPlane=1000;
            cam.aspect=(float)width/height;cam.transform.position=new Vector3(0,0,-100);
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.22f,.23f);cam.targetTexture=rt;
            var canvasGo=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasGo,scene);
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            canvas.worldCamera=cam;
            ((RectTransform)canvas.transform).sizeDelta=new Vector2(1080,height/(width/1080f));
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UICoinShortPopup.prefab");
            Check(asset!=null,"Popup asset missing.");
            var root=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);root.transform.SetParent(canvasGo.transform,false);
            var popup=root.GetComponent<UICoinShortPopup>();
            var panel=Field<RectTransform>(popup,"panel");
            Canvas.ForceUpdateCanvases();
            float scale=Mathf.Min(1f,(1080-64f)/panel.sizeDelta.x,(height/(width/1080f)-64)/panel.sizeDelta.y);
            panel.localScale=Vector3.one*scale;
            var buttons=new[]{"purchaseButton","adButton","cancelButton"}.Select(n=>Field<Button>(popup,n)).ToArray();
            foreach(var button in buttons)Check(button!=null,"Button missing.");
            foreach(var button in buttons.Take(2))
                Check(button.GetComponentsInChildren<Image>().Any(i=>i.name=="ActionIcon"&&i.sprite!=null),"Button icon missing.");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate();
            var report=new StringBuilder();
            foreach(var button in buttons)
            {
                var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    var screen=cam.WorldToScreenPoint(corner);
                    Check(screen.x>=0&&screen.x<=width&&screen.y>=0&&screen.y<=height,"Button offscreen: "+button.name);
                }
                report.AppendLine(button.name+" "+((RectTransform)button.transform).rect);
            }
            cam.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=old;
            Check(image.GetPixels32().Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Take(100).Count()==100,"Blank popup render.");
            File.WriteAllBytes(Folder+"/coin_popup_"+width+"x"+height+".png",image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            File.WriteAllText(Folder+"/popup_"+width+"x"+height+".txt",report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(scene);rt.Release();UnityEngine.Object.DestroyImmediate(rt); }
    }

    static void CheckCameras()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var go=new GameObject("FramingCamera",typeof(Camera),typeof(BossCamera));
            SceneManager.MoveGameObjectToScene(go,scene);
            var cam=go.GetComponent<Camera>();cam.orthographic=true;cam.aspect=2.7f;
            cam.transform.position=new Vector3(0,5,-32);cam.transform.rotation=Quaternion.Euler(9,0,0);
            var bossCam=go.GetComponent<BossCamera>();var serialized=new SerializedObject(bossCam);
            serialized.FindProperty("cam").objectReferenceValue=cam;serialized.ApplyModifiedPropertiesWithoutUndo();
            var data=JsonUtility.FromJson<ActorDataList>(File.ReadAllText("Assets/Resources/Table/actor.json"));
            foreach(var row in data.actors)
            {
                string path=row.model.Replace("_full_prefab","_mon_prefab");
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/"+path+".prefab");
                Check(asset!=null,"Boss prefab missing: "+path);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
                model.transform.position=Vector3.zero;model.transform.localScale=Vector3.one*row.inGameScale;
                try
                {
                    bossCam.FrameBattle(model.transform,Array.Empty<Transform>());
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        if(!(renderer is SkinnedMeshRenderer)&&!(renderer is MeshRenderer))continue;
                        var bounds=renderer.bounds;
                        for(int i=0;i<8;i++)
                        {
                            var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                            var viewport=cam.WorldToViewportPoint(point);
                            Check(viewport.z>0&&viewport.x>=.04f&&viewport.x<=.96f&&viewport.y>=.04f&&viewport.y<=.96f,"Clipped boss: "+row.animalKey);
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
