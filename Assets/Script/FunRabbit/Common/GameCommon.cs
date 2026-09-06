using UnityEngine;

namespace FunRabbit
{
    public class GameCommon
    {
        // ── 변형(_g/_r) animalKey 처리 ─────────────────────────────────
        // 변형 키(bear_g / bear_r)는 actor.json 행의 model/texture로 외형이 결정되고,
        // 아이콘/이름 등 공용 리소스는 원본 동물(bear) 것을 그대로 쓴다.

        // 변형 suffix(_g/_r)를 제거한 원본 동물 키를 반환한다
        public static string GetBaseAnimalKey(string animalKey)
        {
            if (string.IsNullOrEmpty(animalKey))
                return animalKey;

            if (animalKey.EndsWith("_g") || animalKey.EndsWith("_r"))
                return animalKey.Substring(0, animalKey.Length - 2);

            return animalKey;
        }

        // actor.json 행의 texture 필드가 지정돼 있으면 모델의 모든 머티리얼 텍스처를 교체한다.
        // (비어있으면 모델 원본 텍스처 유지. renderer.materials 인스턴스에만 적용 - 원본 에셋 미오염)
        public static void ApplyDataTexture(GameObject model, string animalKey)
        {
            string texturePath = GameActorData.Get(animalKey)?.texture;
            if (string.IsNullOrEmpty(texturePath))
                return;

            Texture2D texture = Resources.Load<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[GameCommon] 텍스처 로드 실패: {texturePath} ({animalKey})");
                return;
            }

            foreach (Renderer modelRenderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in modelRenderer.materials)
                {
                    if (material == null || material.mainTexture == null)
                        continue;

                    material.mainTexture = texture;
                }
            }
        }

        public static string GetModelPrefabName(string animalKey)
        {
            return $"doll_{GetBaseAnimalKey(animalKey)}_full_prefab";
        }

        // 모델 프리팹 경로: actor.json 행의 model 필드 우선, 없으면 기존 이름 규칙
        public static string GetModelPrefabFullPath(string animalKey)
        {
            string model = GameActorData.Get(animalKey)?.model;
            if (!string.IsNullOrEmpty(model))
                return model;

            return $"Prefabs/dollPrefabs/{GetModelPrefabName(animalKey)}";
        }

        // 보스 전용 모델 프리팹 (ally/컬렉션이 쓰는 "_full_prefab"과 별개로, "_mon_prefab" 이름의 전용 모델을 쓴다)
        public static string GetBossModelPrefabName(string animalKey)
        {
            return $"doll_{GetBaseAnimalKey(animalKey)}_mon_prefab";
        }

        // 보스 모델 프리팹 경로: actor.json 행의 model 경로에서 "_full_prefab"→"_mon_prefab" 치환, 없으면 이름 규칙
        public static string GetBossModelPrefabFullPath(string animalKey)
        {
            string model = GameActorData.Get(animalKey)?.model;
            if (!string.IsNullOrEmpty(model) && model.EndsWith("_full_prefab"))
                return model.Substring(0, model.Length - "_full_prefab".Length) + "_mon_prefab";

            return $"Prefabs/dollPrefabs/{GetBossModelPrefabName(animalKey)}";
        }

        public static string GetIconPrefabFullPath(string animalKey)
        {
            return $"UI2/Prefabs/MissionIconPrefab/{GetBaseAnimalKey(animalKey)}MissionIcon";
        }

        public static string GetIconFullPath(string animalKey)
        {
            return $"UI2/Thumbnail/{GetBaseAnimalKey(animalKey)}";
        }

        // 보스 버전 썸네일 (미클리어 스테이지의 도감 표시용)
        public static string GetBossIconFullPath(string animalKey)
        {
            return $"UI2/Thumbnail/{GetBaseAnimalKey(animalKey)}_boss";
        }

        // 인형 표시 이름의 stringData 키 (표시 시점에 LanguageManager.Get으로 변환해서 사용).
        // actor.json 행의 nameKey 필드가 정본 - 변형 행(bear_g 등)도 원본 이름 키가 기입돼 있다.
        // (행이나 필드가 없을 때만 기존 이름 규칙으로 폴백)
        public static string GetDollNameStringKey(string animalKey)
        {
            string nameKey = GameActorData.Get(animalKey)?.nameKey;
            if (!string.IsNullOrEmpty(nameKey))
                return nameKey;

            return $"doll_name_{GetBaseAnimalKey(animalKey)}";
        }

        // 3D 월드 좌표를 화면(2D) 좌표로 변환한다. (GameCameraManager 위임)
        public static Vector3 Convert3dTo2dCoord(Vector3 worldPos)
        {
            return GameCameraManager.Instance.Convert3dTo2dCoord(worldPos);
        }
    }
}