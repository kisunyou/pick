using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 동물(액터) 공통 속성 (JSON table/actor 한 행 = 동물 한 종).
    // 스테이지 진행 규칙은 GameQuestData(quest.json)가, 동물 자체 속성은 여기가 담당한다.
    [System.Serializable]
    public class ActorData
    {
        public string animalKey;
        public string sound;                 // 획득 시 울음소리 (Resources/Sound 기준 경로)
        public float inGameScale = 1f;       // 인게임(뽑기 기계) 인형 스케일
        public float collectionScale = 1.5f; // 컬렉션(도감) 배회 인형 스케일
    }

    [System.Serializable]
    public class ActorDataList
    {
        public List<ActorData> actors;
    }

    public class GameActorData
    {
        // 테이블에 항목이 없을 때의 기본값
        const float DEFAULT_INGAME_SCALE = 1f;
        const float DEFAULT_COLLECTION_SCALE = 1.5f;

        private static ActorDataList _dataList;

        public static void Load()
        {
            TextAsset json = Resources.Load<TextAsset>("table/actor");
            if (json == null)
            {
                Debug.LogError("[GameActorData] actor.json not found in Resources/");
                return;
            }

            _dataList = JsonUtility.FromJson<ActorDataList>(json.text);
            Debug.Log($"[GameActorData] Loaded {_dataList.actors.Count} actors.");
        }

        public static ActorData Get(string animalKey)
        {
            if (_dataList == null)
                Load();

            return _dataList?.actors.Find(a => a.animalKey == animalKey);
        }

        // animalKey에 해당하는 획득 울음소리 경로(Resources/Sound 기준)를 반환한다. 없으면 null.
        public static string GetSound(string animalKey)
        {
            return Get(animalKey)?.sound;
        }

        // 인게임(뽑기 기계) 인형 스케일. 테이블에 없으면 기본값.
        public static float GetInGameScale(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.inGameScale : DEFAULT_INGAME_SCALE;
        }

        // 컬렉션(도감) 배회 인형 스케일. 테이블에 없으면 기본값.
        public static float GetCollectionScale(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.collectionScale : DEFAULT_COLLECTION_SCALE;
        }
    }
}
