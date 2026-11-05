using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FunRabbit;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AllDollChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("All dolls: " + message); }

    [MenuItem("Tools/New Dolls/Validate All Dolls")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var log = new List<string>(); int samples = 0;
        // 판에서 빠진 8종은 에셋을 빌드 밖(_design/retired_20261105)으로 옮겼으므로 검사에서 뺀다
        var retired = new HashSet<string> { "hamster", "rabbit", "crocodile", "turtle", "penguin", "owl", "squid", "jellyfish" };
        var keys = LapTwoDollReview.Keys.Concat(LapThreeDollReview.Keys).ToArray();
        var bases = LapTwoDollReview.Bases.Concat(LapThreeDollReview.Bases).ToArray();
        try
        {
            GameActorData.Load(); GameQuestData.Load();
            var rows = GameActorData.Actors.OrderBy(r => r.stage).ToArray();
            // 2026-11-05 8종(햄스터·토끼·악어·펭귄·거북이·부엉이·오징어·해파리) 제외 -> 28판(28판 후 1판부터 반복). 구간마다 능력치 행을 덜어냈으므로
            // 능력치는 '이전 36판 표의 행을 순서대로 골라 쓴 것'인지만 본다(값 변경·순서 뒤바뀜 금지)
            Check(rows.Length == 28 && rows.Select(r => r.animalKey).Distinct().Count() == 28, "28 unique stage keys");
            // 2026-11-05 구간 폐지: 28판 후 1판부터 반복, 보스 배수 1 -> 3 -> 6 (아군 그대로)
            Check(GameQuestManager.LoopBossMultiplierStep == 3, "boss multiplier step 3");
            Check(rows.Count(r => !string.IsNullOrEmpty(r.legacyKey)) == 16, "16 new species");
            int prevOld = -1;
            var oldRows = (JArray)JObject.Parse(File.ReadAllText("_design/dolls/logs/actor_before_lap3.json"))["actors"];
            var newRows = (JArray)JObject.Parse(File.ReadAllText("Assets/Resources/Table/actor.json"))["actors"];
            var names = (JArray)JObject.Parse(File.ReadAllText("Assets/Resources/Table/stringData.json"))["stringData"];
            // 2026-11-04 판 재배치: 판별 능력치는 판에 고정(이전 표와 같은 판 = 같은 값), 동물 정보(이름·모델·소리·크기·이펙트·legacyKey)는 동물을 따라간다
            string[] identity = { "band", "animalKey", "nameKey", "model", "texture", "legacyKey", "sound", "inGameScale", "collectionScale", "attackFx", "hitFx" };
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; Check(row.stage == i + 1, "stage order");
                var newRow = (JObject)newRows[i].DeepClone();
                int j = Array.IndexOf(keys, row.animalKey);
                if (j >= 0)
                {
                    string key = keys[j], baseKey = bases[j];
                    string suffix = j < 12 ? "_b" : "_r";
                    Check(row.legacyKey == baseKey + suffix && row.nameKey == "doll_name_" + key && row.model == "Prefabs/dollPrefabs/doll_" + key + "_full_prefab" && string.IsNullOrEmpty(row.texture), key + " table mapping");
                    Check(GameActorData.ResolveAnimalKey(baseKey + suffix) == key, key + " old save alias");
                    Check(GameActorData.ResolveAnimalKey(baseKey + "_g") == LapTwoDollReview.Keys[j % 12], key + " green alias");
                }
                else Check(string.IsNullOrEmpty(row.legacyKey), row.animalKey + " original species has no legacy key");
                foreach (string field in identity) newRow.Remove(field);
                newRow.Remove("stage");
                int match = -1;
                for (int o = prevOld + 1; o < oldRows.Count && match < 0; o++)
                {
                    var oldRow = (JObject)oldRows[o].DeepClone();
                    foreach (string field in identity) oldRow.Remove(field);
                    oldRow.Remove("stage");
                    if (JToken.DeepEquals(oldRow, newRow)) match = o;
                }
                Check(match >= 0, "stage balance taken in order from the original table at stage " + row.stage);
                prevOld = match;
                foreach (string path in new[] { GameCommon.GetModelPrefabFullPath(row.animalKey), GameCommon.GetBossModelPrefabFullPath(row.animalKey), GameCommon.GetIconPrefabFullPath(row.animalKey) })
                    Check(Resources.Load<GameObject>(path), "prefab " + path);
                Check(Resources.Load<Sprite>(GameCommon.GetIconFullPath(row.animalKey)), row.animalKey + " icon");
                Check(Resources.Load<Sprite>(GameCommon.GetBossIconFullPath(row.animalKey)), row.animalKey + " boss icon");
                Check(Resources.Load<AudioClip>("Sound/" + row.sound), row.animalKey + " sound");
                var name = names.OfType<JObject>().Single(x => (string)x["key"] == row.nameKey);
                foreach (string lang in new[] { "kor", "eng", "jpn", "tha" }) Check(!string.IsNullOrEmpty((string)name[lang]), row.nameKey + " " + lang);
            }
            log.Add("PASS: 28 stage resources, new-species identities, legacy aliases, four languages; stage balance values unchanged.");
            var panelRoot = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("UI2/Prefabs/UICollectionPanel"),scene);
            try
            {
                var panel = panelRoot.GetComponent<UICollectionPanel>();
                var data = rows.Select(r => new UICollectionItemData { title=r.animalKey, animalKey=r.animalKey, active=r.stage<=24 }).ToArray();
                panel.SetCollectionItems(data); panel.SetCollectionItems(data);
                var slots=panelRoot.GetComponentsInChildren<UICollectionItem>(true);
                Check(slots.Length==rows.Length,"collection slots = stage count without duplicate growth");
                for(int i=0;i<rows.Length;i++) Check(slots[i].GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.text==rows[i].animalKey),"collection slot " +(i+1));
            }
            finally { UnityEngine.Object.DestroyImmediate(panelRoot); }
            log.Add("PASS: collection slots 1-28 and repeat population.");
            for (int i=0;i<keys.Length;i++)
            {
                string key=keys[i], baseKey=bases[i];
                if (retired.Contains(key)) continue;
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
                        // 2026-11-04: 신규 인형은 자기 모양으로 충돌체를 다시 만든다(DollColliderTools) -> 개수는 베이스와 달라도 되고, 일반(full)은 볼록 헐이 있어야 한다
                        var hulls=go.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.StartsWith("Hull ")).ToArray();
                        if(kind=="full")Check(hulls.Length>0&&hulls.All(c=>c.convex&&c.sharedMesh!=null),key+" own convex hulls");
                        else Check(go.GetComponentsInChildren<Collider>(true).Length==basis.GetComponentsInChildren<Collider>(true).Length,key+" inherited colliders");
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
            File.WriteAllLines("_design/dolls/logs/all_dolls_validation.txt",log);
            Debug.Log("[AllDollChecks] PASS "+samples+" animation samples");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
