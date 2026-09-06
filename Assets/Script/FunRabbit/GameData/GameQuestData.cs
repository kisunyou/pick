using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 스테이지 한 칸의 데이터 (actor.json 행에서 구성 - quest.json은 폐기됨).
    // 인형 정체성(모델/아이콘 경로)은 Doll(DollData)로 위임한다.
    [System.Serializable]
    public class StageQuestData
    {
        public int stage;
        public int createDollCount;
        public string animalKey;        // 이 스테이지의 목표 동물 (동물 자체 속성은 GameActorData/actor.json 참조)
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

        // 스테이지 목록을 actor.json(GameActorData)의 stage 필드로 구성한다.
        // (quest.json은 폐기 - actor.json이 스테이지 구성의 단일 정본. createDollCount/totalMissionCount는
        //  원래도 사용처가 없던 죽은 필드라 0으로 남는다.)
        public static void Load()
        {
            List<ActorData> actors = GameActorData.Actors;
            if (actors == null)
            {
                Debug.LogError("[GameStageData] actor.json 로드 실패 - 스테이지를 구성할 수 없습니다");
                return;
            }

            List<StageQuestData> stages = new List<StageQuestData>();
            foreach (ActorData actor in actors)
            {
                if (actor.stage <= 0 || string.IsNullOrEmpty(actor.animalKey))
                    continue;

                stages.Add(new StageQuestData
                {
                    stage = actor.stage,
                    animalKey = actor.animalKey,
                });
            }

            stages.Sort((a, b) => a.stage.CompareTo(b.stage));
            _dataList = new StageQuestDataList { stages = stages };

            // 불변 조건 검증: stage 값은 1부터 빠짐없이 순차 증가해야 한다 (중복/공백 시 진행이 꼬인다)
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i].stage != i + 1)
                {
                    Debug.LogError($"[GameStageData] actor.json stage 값이 순차적이지 않습니다: " +
                                   $"{i + 1}번째 항목의 stage = {stages[i].stage} ({stages[i].animalKey}). 1~{stages.Count} 연속이어야 합니다.");
                    break;
                }
            }

            Debug.Log($"[GameStageData] actor.json 기반 스테이지 {stages.Count}개 구성.");
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
