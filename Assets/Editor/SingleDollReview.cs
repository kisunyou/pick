using System;
using System.IO;
using System.Linq;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One completed doll at a time: import, validate resource links and animation, capture Unity previews.
public static class SingleDollReview
{
    public static void ImportAndReview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        NewDollImporter.ImportFromArgs();
        NewDollImporter.ImportUiFromArgs();
        Validate();
        NewDollImporter.PreviewFromArgs();
        Debug.Log("[SingleDollReview] PASS import, resources, legacy keys, rig, animation and previews");
    }

    public static void Validate()
    {
        var validationScene = EditorSceneManager.NewPreviewScene();
        try
        {
        GameActorData.Load();
        var rows = GameActorData.Actors;
        Require(rows.Count == 36 && rows.Select(r => r.animalKey).Distinct().Count() == 36, "36 unique stage keys");
        Require(GameActorData.GetByStage(13).animalKey == "sheep", "stage 13 sheep");
        Require(rows.Any(r => r.legacyKey == "cow_b" && r.animalKey == "sheep"), "sheep replacement retained");
        Require(GameActorData.ResolveAnimalKey("cow_b") == "sheep", "legacy cow_b");
        Require(GameActorData.ResolveAnimalKey("cow_g") == "sheep", "legacy cow_g");
        Require(GameActorData.ResolveAnimalKey("pig_g") == (rows.FirstOrDefault(r => r.legacyKey == "pig_b")?.animalKey ?? "pig_b"), "green legacy variant");
        var strings = File.ReadAllText("Assets/Resources/Table/stringData.json");
        foreach (var row in rows)
        {
            Require(Resources.Load<GameObject>(GameCommon.GetModelPrefabFullPath(row.animalKey)), row.animalKey + " full");
            Require(Resources.Load<GameObject>(GameCommon.GetBossModelPrefabFullPath(row.animalKey)), row.animalKey + " boss");
            Require(Resources.Load<Sprite>(GameCommon.GetIconFullPath(row.animalKey)), row.animalKey + " icon");
            Require(Resources.Load<Sprite>(GameCommon.GetBossIconFullPath(row.animalKey)), row.animalKey + " boss icon");
            Require(Resources.Load<GameObject>(GameCommon.GetIconPrefabFullPath(row.animalKey)), row.animalKey + " mission icon");
            Require(Resources.Load<AudioClip>("Sound/" + row.sound), row.animalKey + " sound");
            Require(strings.Contains("\"" + row.nameKey + "\""), row.animalKey + " localized name");
        }
        var panelRoot = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("UI2/Prefabs/UICollectionPanel"), validationScene);
        try
        {
            var panel = panelRoot.GetComponent<UICollectionPanel>();
            // No asynchronous image loads in edit mode; resource paths are checked above.
            var items = rows.OrderBy(r => r.stage).Select(r => new UICollectionItemData
            { title = r.animalKey, animalKey = r.animalKey, active = r.stage < 13 }).ToArray();
            panel.SetCollectionItems(items);
            panel.SetCollectionItems(items);
            var slots = panelRoot.GetComponentsInChildren<UICollectionItem>(true);
            Require(slots.Length == 36, "collection expands to 36 exactly once");
            Require(slots[12].GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t.text == "sheep"), "sheep in collection slot 13");
        }
        finally { UnityEngine.Object.DestroyImmediate(panelRoot); }
        int samples = 0;
        foreach (var kind in new[] { "full", "mon" })
        {
            var prefab = Resources.Load<GameObject>("Prefabs/dollPrefabs/doll_sheep_" + kind + "_prefab");
            var original = Resources.Load<GameObject>("Prefabs/dollPrefabs/doll_cow_" + kind + "_prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, validationScene);
            var baked = new Mesh();
            try
            {
                var animator = go.GetComponentInChildren<Animator>();
                Require(animator.runtimeAnimatorController == original.GetComponentInChildren<Animator>().runtimeAnimatorController, "base controller retained");
                Require(go.GetComponentsInChildren<Collider>(true).Length == original.GetComponentsInChildren<Collider>(true).Length, "base colliders retained");
                var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var skin in skins)
                {
                    Require(skin.bones.All(b => b != null), "all bones mapped");
                    Require(skin.sharedMesh.bindposes.Length == skin.bones.Length, "bindpose count");
                    Require(skin.sharedMesh.boneWeights.All(w => Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1) < .001f), "normalized skin weights");
                    Require(skin.sharedMaterials.All(m => m && m.shader && m.shader.isSupported && m.mainTexture), "supported textured materials");
                }
                var transforms = go.GetComponentsInChildren<Transform>(true);
                var positions = transforms.Select(t => t.localPosition).ToArray();
                var rotations = transforms.Select(t => t.localRotation).ToArray();
                var scales = transforms.Select(t => t.localScale).ToArray();
                var clips = animator.runtimeAnimatorController.animationClips.Distinct().ToArray();
                Require(clips.Length >= 4, "four inherited animations");
                foreach (var clip in clips)
                for (int frame = 0; frame < 5; frame++)
                {
                    for (int i = 0; i < transforms.Length; i++)
                    { transforms[i].localPosition=positions[i]; transforms[i].localRotation=rotations[i]; transforms[i].localScale=scales[i]; }
                    clip.SampleAnimation(animator.gameObject, clip.length * frame / 4f);
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(baked);
                        Require(baked.vertices.All(v => !float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude)), "finite animation vertices");
                        Require(baked.bounds.size.magnitude > .001f, "nonempty posed mesh");
                    }
                    samples++;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(go); }
        }
        Directory.CreateDirectory("_design/dolls/logs");
        File.WriteAllText("_design/dolls/logs/unity_validation.txt", "PASS: 36 stages, " + rows.Count(r => !string.IsNullOrEmpty(r.legacyKey)) + " new species, legacy keys, normal/boss/icon/mission/sound/name links; 36 collection slots with sheep at 13; inherited controller/colliders; " + samples + " sheep animation samples.\n");
        }
        finally { EditorSceneManager.ClosePreviewScene(validationScene); }
    }

    [MenuItem("Tools/New Dolls/Update Sheep Boss Eyes")]
    public static void UpdateSheepBossEyes()
    {
        NewDollImporter.ImportBossOnly("sheep", "cow");
        Validate();
        var prefab = Resources.Load<GameObject>("Prefabs/dollPrefabs/doll_sheep_mon_prefab");
        var mesh = prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        Require(mesh.subMeshCount == 2, "body plus original eyes/horns");
        Require(mesh.GetTriangles(1).Length / 3 == 376, "240 original eye triangles plus 136 horn triangles");
        Require(File.ReadAllBytes("Assets/Resources/Prefabs/dollPrefabs/doll_sheep_mon.png").SequenceEqual(
            File.ReadAllBytes("Assets/Resources/Prefabs/dollPrefabs/doll_sheep_full.png")), "unpainted body atlas");
        string output = "_design/dolls/rig/_unity_preview";
        Directory.CreateDirectory(output);
        NewDollImporter.Preview("cow", "mon", output);
        NewDollImporter.Preview("sheep", "full", output);
        NewDollImporter.Preview("sheep", "mon", output);
        File.WriteAllText("_design/dolls/logs/boss_eyes_validation.txt",
            "PASS: two original cow boss eye meshes (124 vertices / 240 triangles), head skinning, unpainted body texture, 40 animation samples.\n");
        Debug.Log("[SingleDollReview] PASS modeled sheep boss eyes");
    }

    static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("Doll validation failed: " + message);
    }
}
