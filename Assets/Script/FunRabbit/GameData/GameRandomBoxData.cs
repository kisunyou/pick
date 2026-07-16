using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    [System.Serializable]
    public class RandomBoxData
    {
        public int key;
        public int itemkey;      // 지급 아이템 (ItemData.key)
        public int Probability;  // 확률 가중치
    }

    [System.Serializable]
    public class RandomBoxDataList
    {
        public List<RandomBoxData> randomBoxes;
    }

    public class GameRandomBoxData
    {
        private static RandomBoxDataList _dataList;

        public static RandomBoxDataList RandomBoxDataList
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
            TextAsset json = Resources.Load<TextAsset>("Table/randombox");
            if (json == null)
            {
                Debug.LogError("[GameRandomBox] randombox.json not found in Resources/Table/");
                return;
            }

            _dataList = JsonUtility.FromJson<RandomBoxDataList>(json.text);
            Debug.Log($"[GameRandomBox] Loaded {_dataList.randomBoxes.Count} random boxes.");
        }

        public static List<RandomBoxData> GetAll()
        {
            if (_dataList == null)
                Load();

            return _dataList?.randomBoxes;
        }

        public static int TotalCount
        {
            get
            {
                if (_dataList == null) Load();
                return _dataList?.randomBoxes.Count ?? 0;
            }
        }
    }
}
