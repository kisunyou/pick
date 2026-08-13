using UnityEngine;

namespace FunRabbit
{
    public class GameCommon
    {
        public static string GetModelPrefabName(string animalKey)
        {
            return $"doll_{animalKey}_full_prefab";
        }

        public static string GetModelPrefabFullPath(string animalKey)
        {
            return $"Prefabs/dollPrefabs/{GetModelPrefabName(animalKey)}";
        }

        // 보스 전용 모델 프리팹 (ally/컬렉션이 쓰는 "_full_prefab"과 별개로, "_mon_prefab" 이름의 전용 모델을 쓴다)
        public static string GetBossModelPrefabName(string animalKey)
        {
            return $"doll_{animalKey}_mon_prefab";
        }

        public static string GetBossModelPrefabFullPath(string animalKey)
        {
            return $"Prefabs/dollPrefabs/{GetBossModelPrefabName(animalKey)}";
        }

        public static string GetIconPrefabFullPath(string animalKey)
        {
            return $"UI2/Prefabs/MissionIconPrefab/{animalKey}MissionIcon";
        }

        public static string GetIconFullPath(string animalKey)
        {
            return $"UI2/Thumbnail/{animalKey}";
        }

        // 보스 버전 썸네일 (미클리어 스테이지의 도감 표시용)
        public static string GetBossIconFullPath(string animalKey)
        {
            return $"UI2/Thumbnail/{animalKey}_boss";
        }

        // 인형 표시 이름의 stringData 키 (표시 시점에 LanguageManager.Get으로 변환해서 사용)
        public static string GetDollNameStringKey(string animalKey)
        {
            return $"doll_name_{animalKey}";
        }

        // 3D 월드 좌표를 화면(2D) 좌표로 변환한다. (GameCameraManager 위임)
        public static Vector3 Convert3dTo2dCoord(Vector3 worldPos)
        {
            return GameCameraManager.Instance.Convert3dTo2dCoord(worldPos);
        }
    }
}