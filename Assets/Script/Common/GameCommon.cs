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

        public static string GetIconPrefabFullPath(string animalKey)
        {
            return $"UI2/Prefabs/MissionIconPrefab/{animalKey}MissionIcon";
        }

        public static string GetIconFullPath(string animalKey)
        {
            return $"UI2/Thumbnail/{animalKey}";
        }

        // 3D 월드 좌표를 화면(2D) 좌표로 변환한다. (GameCameraManager 위임)
        public static Vector3 Convert3dTo2dCoord(Vector3 worldPos)
        {
            return GameCameraManager.Instance.Convert3dTo2dCoord(worldPos);
        }
    }
}