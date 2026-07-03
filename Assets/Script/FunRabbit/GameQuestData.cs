using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 스테이지별 퀘스트 규칙 (JSON table/quest 한 행 = 한 스테이지).
    // 인형 정체성(모델/아이콘 경로)은 Doll(DollData)로 위임한다.
    [System.Serializable]
    public class StageQuestData
    {
        public int stage;
        public int createDollCount;
        public string animalKey;        // 이 스테이지의 목표 동물
        public int totalMissionCount;

        private DollData _doll;

        // 이 스테이지의 목표 인형 정체성 (animalKey 기반, 최초 접근 시 생성)
        public DollData Doll
        {
            get
            {
                if (_doll == null)
                    _doll = new DollData(animalKey);
                return _doll;
            }
        }
    }

    [System.Serializable]
    public class StageQuestDataList
    {
        public List<StageQuestData> stages;
    }

    public class GameQuestData
    {
        private static StageQuestDataList _dataList;

        public static StageQuestDataList StageQuestDataList
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

            _dataList = JsonUtility.FromJson<StageQuestDataList>(json.text);
            Debug.Log($"[GameStageData] Loaded {_dataList.stages.Count} stages.");
        }

        public static StageQuestData GetStage(int stage)
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
