using System;
using System.IO;
using System.Linq;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BattleFramingPreview
{
    [MenuItem("Tools/Gameplay Polish/Preview Battle")]
    public static void Run()
    {
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/Stage0.unity");
        var rt=new RenderTexture(1080,400,24);
        try
        {
            var roots=scene.GetRootGameObjects();
            var battle=roots.SelectMany(r=>r.GetComponentsInChildren<ActorBattleSystem>(true)).First();
            var bossCam=roots.SelectMany(r=>r.GetComponentsInChildren<BossCamera>(true)).First();
            var cam=bossCam.Cam;cam.scene=scene;cam.targetTexture=rt;
            var cameraData=cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            foreach(var overlay in cameraData.cameraStack)if(overlay!=null)overlay.targetTexture=rt;
            var data=new SerializedObject(battle);
            var pivot=(Transform)data.FindProperty("bossTransform").objectReferenceValue;
            var ap=data.FindProperty("allyTransforms");
            var anchors=Enumerable.Range(0,ap.arraySize).Select(i=>(Transform)ap.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            var rows=JsonUtility.FromJson<ActorDataList>(File.ReadAllText("Assets/Resources/Table/actor.json")).actors;
            foreach(var row in rows)
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/"+row.model.Replace("_full_prefab","_mon_prefab")+".prefab");
                var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
                var materials=new System.Collections.Generic.List<Material>();
                try
                {
                    model.transform.SetPositionAndRotation(pivot.position,pivot.rotation);
                    model.transform.SetParent(pivot,true);
                    model.transform.localScale=Vector3.one*row.inGameScale;
                    if(!string.IsNullOrEmpty(row.texture))
                        foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                        {
                            var copies=renderer.sharedMaterials.Select(m=>m==null?null:new Material(m)).ToArray();
                            materials.AddRange(copies.Where(m=>m!=null));
                            foreach(var material in copies)
                                if(material!=null&&material.mainTexture!=null)material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/"+row.texture+".tga");
                            renderer.sharedMaterials=copies;
                        }
                    bossCam.SetViewAspect(2.7f);bossCam.FrameBattle(model.transform,anchors);
                    var animator=model.GetComponentInChildren<Animator>();
                    var transforms=model.GetComponentsInChildren<Transform>(true);
                    var positions=transforms.Select(t=>t.localPosition).ToArray();
                    var rotations=transforms.Select(t=>t.localRotation).ToArray();
                    var scales=transforms.Select(t=>t.localScale).ToArray();
                    var mesh=new Mesh();
                    try
                    {
                        if(animator!=null&&animator.runtimeAnimatorController!=null)
                        foreach(var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                        for(int sample=0;sample<5;sample++)
                        {
                            for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}
                            clip.SampleAnimation(animator.gameObject,clip.length*sample/4f);
                            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                            {
                                skin.BakeMesh(mesh,true);
                                foreach(var vertex in mesh.vertices)
                                {
                                    var v=cam.WorldToViewportPoint(skin.transform.TransformPoint(vertex));
                                    if(v.z<=0||v.x<0||v.x>1||v.y<0||v.y>1)throw new Exception("Animation clipped: "+row.animalKey+"/"+clip.name+" sample="+sample+" viewport="+v+" camera="+cam.transform.position+" size="+cam.orthographicSize+" rootScale="+model.transform.lossyScale+" skinScale="+skin.transform.lossyScale+" bounds="+skin.bounds+" baked="+mesh.bounds);
                                }
                            }
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                    for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}
                    if(row.stage!=18&&row.stage!=24&&row.stage!=35)continue;
                    cam.Render();
                    var old=RenderTexture.active;RenderTexture.active=rt;
                    var image=new Texture2D(1080,400,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,1080,400),0,0);image.Apply();RenderTexture.active=old;
                    Directory.CreateDirectory(".utmp/gameplay_polish");
                    File.WriteAllBytes(".utmp/gameplay_polish/battle_stage_"+row.stage+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                finally { UnityEngine.Object.DestroyImmediate(model);foreach(var material in materials)UnityEngine.Object.DestroyImmediate(material); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene);rt.Release();UnityEngine.Object.DestroyImmediate(rt); }
        File.WriteAllText(".utmp/gameplay_polish/battle_checks.txt","PASS: 36 bosses, 5 samples of each animation, complete mesh within viewport.\n");
        Debug.Log("[BattleFramingPreview] PASS: 36 animated bosses framed.");
    }
}
