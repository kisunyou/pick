using System;
using System.IO;
using FunRabbit;
using UnityEditor;
using UnityEngine;

public static class BlueVariantChecks
{
    [MenuItem("Tools/Validation/Check Blue Variants")]
    public static void Run()
    {
        GameActorData.Load();
        GameQuestData.Load();
        Require(GameActorData.Actors.Count == 36, "Expected 36 actors.");
        Require(GameQuestData.TotalStageCount == 36, "Expected 36 stages.");
        Require(GameActorData.ResolveAnimalKey(null) == null, "Null key changed.");
        Require(GameActorData.ResolveAnimalKey("") == "", "Empty key changed.");
        Require(GameActorData.Get("unknown_g") == null, "Unknown key matched.");
        Require(GameActorData.ResolveAnimalKey("bear_golden") == "bear_golden", "Golden suffix changed.");

        for (int stage = 13; stage <= 24; stage++)
        {
            ActorData actor = GameActorData.GetByStage(stage);
            Require(actor != null && actor.animalKey.EndsWith("_b"), "Invalid variant at " + stage);
            string baseKey = actor.animalKey.Substring(0, actor.animalKey.Length - 2);
            string legacy = baseKey + "_g";
            Require(ReferenceEquals(actor, GameActorData.Get(legacy)), "Legacy save key failed: " + legacy);
            Require(ReferenceEquals(actor, GameActorData.Get(actor.animalKey)), "New key failed.");
            Require(GameActorData.ResolveAnimalKey(legacy) == actor.animalKey, "Legacy mission key mismatch.");
            Require(GameCommon.GetBaseAnimalKey(actor.animalKey) == baseKey, "New base key failed.");
            Require(GameCommon.GetBaseAnimalKey(legacy) == baseKey, "Legacy base key failed.");
            Require(GameCommon.GetBaseAnimalKey(baseKey + "_r") == baseKey, "Red base key failed.");
            Require(GameQuestData.GetStage(stage).animalKey == actor.animalKey, "Stage mapping failed.");
            Require(actor.texture.EndsWith("_b"), "Texture path not migrated.");
            Texture2D texture = Resources.Load<Texture2D>(actor.texture);
            Require(texture != null && texture.width == 512 && texture.height == 512, "Invalid texture: " + actor.texture);
            Require(Resources.Load<GameObject>(GameCommon.GetModelPrefabFullPath(legacy)) != null, "Legacy ally model failed.");
            Require(Resources.Load<GameObject>(GameCommon.GetBossModelPrefabFullPath(actor.animalKey)) != null, "Boss model failed.");
            Require(GameCommon.GetIconFullPath(legacy) == GameCommon.GetIconFullPath(actor.animalKey), "Icon alias failed.");
            Require(GameActorData.Get(baseKey) != null && GameActorData.Get(baseKey + "_r") != null, "Original/red lookup failed.");
        }

        const string folder = "Assets/Resources/Prefabs/dollPrefabs";
        Require(Directory.GetFiles(folder, "*_g.tga*").Length == 0, "Retired green assets remain.");
        Require(Directory.GetFiles(folder, "*_b.tga").Length == 12, "Expected 12 replacement textures.");
        Debug.Log("[BlueVariantChecks] PASS: 12 textures, 36 stages, legacy save/mission aliases, original/red keys, ally/boss resources.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[BlueVariantChecks] " + message);
    }
}
