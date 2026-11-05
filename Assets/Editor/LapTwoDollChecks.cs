using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FunRabbit;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LapTwoDollChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Lap two: " + message); }

    [MenuItem("Tools/New Dolls/Validate Lap Two")]
    public static void Run()
    {
        GameActorData.Load();
        if (GameActorData.Actors.Count(r => !string.IsNullOrEmpty(r.legacyKey)) == 24)
        {
            AllDollChecks.Run();
            return;
        }
        var scene = EditorSceneManager.NewPreviewScene();
        var log = new List<string>(); int samples = 0;
        try
        {
            GameActorData.Load(); GameQuestData.Load();
            var rows = GameActorData.Actors.OrderBy(r => r.stage).ToArray();
            Check(rows.Length == 36 && rows.Select(r => r.animalKey).Distinct().Count() == 36, "36 unique stage keys");
            Check(rows.Count(r => !string.IsNullOrEmpty(r.legacyKey)) == 12, "12 new species");
            var oldRows = (JArray)JObject.Parse(File.ReadAllText("_design/dolls/logs/actor_before_lap2.json"))["actors"];
            var newRows = (JArray)JObject.Parse(File.ReadAllText("Assets/Resources/Table/actor.json"))["actors"];
            var names = (JArray)JObject.Parse(File.ReadAllText("Assets/Resources/Table/stringData.json"))["stringData"];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; Check(row.stage == i + 1, "stage order");
                var oldRow = (JObject)oldRows[i].DeepClone(); var newRow = (JObject)newRows[i].DeepClone();
                if (i >= 12 && i < 24)
                {
                    string key = LapTwoDollReview.Keys[i-12], baseKey = LapTwoDollReview.Bases[i-12];
                    Check(row.animalKey == key && row.legacyKey == baseKey + "_b" && string.IsNullOrEmpty(row.texture), key + " table mapping");
                    Check(GameActorData.ResolveAnimalKey(baseKey + "_b") == key && GameActorData.ResolveAnimalKey(baseKey + "_g") == key, key + " old save aliases");
                    Check(GameActorData.ResolveAnimalKey(baseKey + "_r") == baseKey + "_r", "third band unchanged");
                    foreach (string field in new[] { "animalKey", "nameKey", "model", "texture", "legacyKey" }) { oldRow.Remove(field); newRow.Remove(field); }
                }
                Check(JToken.DeepEquals(oldRow,newRow), "only identity changed at stage " + row.stage);
                foreach (string path in new[] { GameCommon.GetModelPrefabFullPath(row.animalKey), GameCommon.GetBossModelPrefabFullPath(row.animalKey), GameCommon.GetIconPrefabFullPath(row.animalKey) })
                    Check(Resources.Load<GameObject>(path), "prefab " + path);
                Check(Resources.Load<Sprite>(GameCommon.GetIconFullPath(row.animalKey)), row.animalKey + " icon");
                Check(Resources.Load<Sprite>(GameCommon.GetBossIconFullPath(row.animalKey)), row.animalKey + " boss icon");
                Check(Resources.Load<AudioClip>("Sound/" + row.sound), row.animalKey + " sound");
                var name = names.OfType<JObject>().Single(x => (string)x["key"] == row.nameKey);
                foreach (string lang in new[] { "kor", "eng", "jpn", "tha" }) Check(!string.IsNullOrEmpty((string)name[lang]), row.nameKey + " " + lang);
            }
            log.Add("PASS: 36 stage resources, stage 13-24 identities, legacy aliases, four languages; original stages and all balance values unchanged.");
            var panelRoot = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("UI2/Prefabs/UICollectionPanel"),scene);
            try
            {
                var panel = panelRoot.GetComponent<UICollectionPanel>();
                var data = rows.Select(r => new UICollectionItemData { title=r.animalKey, animalKey=r.animalKey, active=r.stage<=24 }).ToArray();
                panel.SetCollectionItems(data); panel.SetCollectionItems(data);
                var slots=panelRoot.GetComponentsInChildren<UICollectionItem>(true);
                Check(slots.Length==36,"36 collection slots without duplicate growth");
                for(int i=12;i<24;i++) Check(slots[i].GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.text==rows[i].animalKey),"collection slot " +(i+1));
            }
            finally { UnityEngine.Object.DestroyImmediate(panelRoot); }
            log.Add("PASS: collection slots 13-24 and repeat population.");
            for (int i=0;i<LapTwoDollReview.Keys.Length;i++)
            {
                string key=LapTwoDollReview.Keys[i], baseKey=LapTwoDollReview.Bases[i];
                string folder="Assets/Resources/Prefabs/dollPrefabs/";
                Check(File.ReadAllBytes(folder+"doll_"+key+"_full.png").SequenceEqual(File.ReadAllBytes(folder+"doll_"+key+"_mon.png")),key+" no painted boss eye texture");
                var mission=Resources.Load<GameObject>(GameCommon.GetIconPrefabFullPath(key));
                Check(mission.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(im=>im.sprite==Resources.Load<Sprite>(GameCommon.GetIconFullPath(key))),key+" mission sprite");
                foreach(string kind in new[]{"full","mon"})
                {
                    var prefab=Resources.Load<GameObject>("Prefabs/dollPrefabs/doll_"+key+"_"+kind+"_prefab");
                    var basis=Resources.Load<GameObject>("Prefabs/dollPrefabs/doll_"+baseKey+"_"+kind+"_prefab");
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene); var baked=new Mesh();
                    try
                    {
                        var animator=go.GetComponentInChildren<Animator>();
                        Check(animator.runtimeAnimatorController==basis.GetComponentInChildren<Animator>().runtimeAnimatorController,key+" inherited controller");
                        Check(go.GetComponentsInChildren<Collider>(true).Length==basis.GetComponentsInChildren<Collider>(true).Length,key+" inherited colliders");
                        var skins=go.GetComponentsInChildren<SkinnedMeshRenderer>();
                        var rest=new Dictionary<SkinnedMeshRenderer,float>();
                        foreach(var skin in skins)
                        {
                            var mesh=skin.sharedMesh;
                            Check(skin.bones.All(b=>b!=null)&&mesh.bindposes.Length==skin.bones.Length,key+" bones");
                            Check(mesh.boneWeights.All(w=>Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)<.001f),key+" weights");
                            Check(skin.sharedMaterials.All(m=>m&&m.shader&&m.shader.isSupported&&m.mainTexture),key+" materials");
                            if(kind=="mon")
                            {
                                Check(mesh.subMeshCount>=2&&mesh.GetTriangles(1).Length>=720,key+" modeled eyes");
                                string head=(string)JObject.Parse(File.ReadAllText("_design/dolls/rig/"+key+"/boss_eyes.json"))["head_bone"];
                                Check(mesh.GetTriangles(1).Take(720).All(v=>skin.bones[mesh.boneWeights[v].boneIndex0].name==head&&mesh.boneWeights[v].weight0>.999f),key+" eye attachment");
                            }
                            skin.BakeMesh(baked);rest[skin]=baked.bounds.size.magnitude;
                        }
                        var transforms=go.GetComponentsInChildren<Transform>(true);
                        var pos=transforms.Select(t=>t.localPosition).ToArray();var rot=transforms.Select(t=>t.localRotation).ToArray();var scale=transforms.Select(t=>t.localScale).ToArray();
                        var clips=animator.runtimeAnimatorController.animationClips.Distinct().ToArray();Check(clips.Length>=4,key+" animations");
                        foreach(var clip in clips)for(int frame=0;frame<5;frame++)
                        {
                            for(int t=0;t<transforms.Length;t++){transforms[t].localPosition=pos[t];transforms[t].localRotation=rot[t];transforms[t].localScale=scale[t];}
                            clip.SampleAnimation(animator.gameObject,clip.length*frame/4f);
                            foreach(var skin in skins)
                            {
                                skin.BakeMesh(baked);
                                Check(baked.vertices.All(v=>!float.IsNaN(v.sqrMagnitude)&&!float.IsInfinity(v.sqrMagnitude)),key+" finite animation");
                                Check(baked.bounds.size.magnitude>rest[skin]*.1f&&baked.bounds.size.magnitude<rest[skin]*4f,key+" bounded animation");
                            }
                            samples++;
                        }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(go);}
                }
                log.Add("PASS: "+key+" normal/boss, original rig/colliders, modeled eyes, mission icon.");
            }
            log.Add("PASS: "+samples+" animation samples.");
            File.WriteAllLines("_design/dolls/logs/lap2_validation.txt",log);
            Debug.Log("[LapTwoDollChecks] PASS "+samples+" animation samples");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
