using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 뽑기 미션 정의 (JSON table/mission 한 행 = 미션 하나).
    // MissionSystem이 percent 가중치로 하나를 추첨해 진행한다.
    [System.Serializable]
    public class MissionData
    {
        public const string TypeActor = "actor";         // 지정 동물 인형을 뽑는 미션
        public const string TypeRandomBox = "randombox"; // 랜덤박스를 뽑는 미션

        public int key;               // 고유 키 (PlayerContext에 진행 중 미션으로 저장된다)
        public int collection_count;  // 목표 수집 개수
        public string mission_type;   // TypeActor | TypeRandomBox
        public int reward_count;      // 성공 보상 개수 (actor = 아군 마리수 / randombox = 박스 개수)
        public float percent;         // 미션 추첨 가중치 (전체 합 대비 비율)

        public bool IsActorType => mission_type == TypeActor;
    }

    [System.Serializable]
    public class MissionDataList
    {
        public List<MissionData> missions;
    }

    public class GameMissionData
    {
        private static MissionDataList _dataList;

        public static List<MissionData> Missions
        {
            get
            {
                if (_dataList == null)
                    Load();
                return _dataList?.missions;
            }
        }

        public static void Load()
        {
            TextAsset json = Resources.Load<TextAsset>("Table/mission");
            if (json == null)
            {
                Debug.LogError("[GameMissionData] Table/mission.json 로드 실패");
                return;
            }

            _dataList = JsonUtility.FromJson<MissionDataList>(json.text);

            if (_dataList?.missions == null || _dataList.missions.Count == 0)
                Debug.LogError("[GameMissionData] mission.json 파싱 실패 또는 미션이 없습니다");
            else
                Debug.Log($"[GameMissionData] 미션 {_dataList.missions.Count}개 로드");
        }

        public static MissionData Get(int key)
        {
            return Missions?.Find(m => m.key == key);
        }

        // percent 가중치 비례로 미션 하나를 추첨한다. (가중치 합이 0 이하면 균등 추첨)
        public static MissionData PickRandomByPercent()
        {
            List<MissionData> missions = Missions;
            if (missions == null || missions.Count == 0)
                return null;

            float total = 0f;
            foreach (MissionData mission in missions)
                total += Mathf.Max(0f, mission.percent);

            if (total <= 0f)
                return missions[Random.Range(0, missions.Count)];

            float pick = Random.value * total;
            foreach (MissionData mission in missions)
            {
                pick -= Mathf.Max(0f, mission.percent);
                if (pick <= 0f)
                    return mission;
            }

            return missions[missions.Count - 1];
        }
    }
}
