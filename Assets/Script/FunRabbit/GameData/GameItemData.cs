using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    [System.Serializable]
    public class ItemData
    {
        public int key;
        public string name;
        public string icon_path;   // JSON 키(icon_path)와 동일해야 매핑됨
        public int count;          // 지급 수량 (코인 아이템이면 코인 개수)
    }

    [System.Serializable]
    public class ItemDataList
    {
        public List<ItemData> items;
    }

    public class GameItemData
    {
        private static ItemDataList _dataList;

        public static ItemDataList ItemDataList
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
            TextAsset json = Resources.Load<TextAsset>("Table/item");
            if (json == null)
            {
                Debug.LogError("[GameItemData] item.json not found in Resources/Table/");
                return;
            }

            _dataList = JsonUtility.FromJson<ItemDataList>(json.text);
            Debug.Log($"[GameItemData] Loaded {_dataList.items.Count} items.");
        }

        public static ItemData Get(int key)
        {
            if (_dataList == null)
                Load();

            return _dataList.items.Find(i => i.key == key);
        }

        public static int TotalItemCount
        {
            get
            {
                if (_dataList == null) Load();
                return _dataList?.items.Count ?? 0;
            }
        }
    }
}
