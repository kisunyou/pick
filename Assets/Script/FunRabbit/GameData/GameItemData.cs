using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    [System.Serializable]
    public class ItemData
    {
        public int key;
        public string name;        // 아이템 이름의 stringData 키 (표시 시 LanguageManager.Get으로 변환)
        public string icon_path;   // JSON 키(icon_path)와 동일해야 매핑됨. allyActor 랜덤 지급 아이템(animalKey 빈 값)은 빈 값 - 추첨 시 GameCommon.GetIconFullPath(animalKey)로 채워진다
        public int count;          // 지급 수량 (코인 아이템이면 코인 개수)
        public string itemType;    // "coin", "reset", "allyActor" 등 아이템 종류
        public string itemDescription; // 아이템 설명의 stringData 키 (표시 시 LanguageManager.Get으로 변환)
        public string animalKey;   // itemType "allyActor" 전용 - 지급할 아군 액터 (actor.json의 animalKey). 다른 타입은 빈 값.
                                   // allyActor인데 빈 값이면 랜덤박스 추첨 시 클리어한 액터 중 하나를 랜덤으로 확정한다 (UIRandomboxPanelControl.ResolveRandomAllyItem)
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
