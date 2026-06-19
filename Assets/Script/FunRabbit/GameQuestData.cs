using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    [System.Serializable]
    public class QuestData
    {
        public int stage;
        public int createDollCount;
        public string animalKey;
        public int totalMissionCount;

        public string GetModelPrefabName()
        {            
            return $"doll_{animalKey}_full_prefab";
        }

        public string GetIconPrefabFullPath()
        {
            return $"UI2/Prefabs/MissionIconPrefab/{animalKey}MissionIcon";
        }

        public string GetIconFullPath()
        {
            return $"UI2/Thumbnail/{animalKey}";
        }
    }

    [System.Serializable]
    public class QuestDataList
    {
        public List<QuestData> stages;
    }

    public class GameQuestData
    {
        private static QuestDataList _dataList;

        public static QuestDataList QuestDataList
        {
            get
            {
                if (_dataList == null)
                    Load();
                return _dataList;
            }
        }

        public static void Load()
        {
            TextAsset json = Resources.Load<TextAsset>("table/quest");
            if (json == null)
            {
                Debug.LogError("[GameStageData] quest.json not found in Resources/");
                return;
            }

            _dataList = JsonUtility.FromJson<QuestDataList>(json.text);
            Debug.Log($"[GameStageData] Loaded {_dataList.stages.Count} stages.");
        }

        public static QuestData GetStage(int stage)
        {
            if (_dataList == null)
                Load();

            return _dataList.stages.Find(s => s.stage == stage);
        }

        public static int TotalStageCount
        {
            get
            {
                if (_dataList == null) Load();
                return _dataList?.stages.Count ?? 0;
            }
        }
    }
}
