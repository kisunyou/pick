using UnityEngine;

namespace FunRabbit
{
    public class GameQuestManager : Singleton<GameQuestManager>
    {
        private const string KEY_STAGE = "currentStage";
        private const string KEY_MISSION_COUNT = "missionCount";

        // MissionCount가 변경될 때 발생하는 이벤트 (current, total)
        public event System.Action<int, int> OnMissionCountChanged;
        // (stage, isClear) - isClear는 스테이지 클리어로 단계가 올라간 경우 true
        public event System.Action<int, bool> OnStageChanged;
        // 스테이지 클리어 시 발생하는 이벤트. 인자는 다음 스테이지 데이터 (마지막 스테이지면 null)
        public event System.Action<QuestData> OnStageClear;

        // 현재 스테이지 (1부터 시작)
        public int CurrentStage
        {
            get 
            {
                if (!PlayerPrefs.HasKey(KEY_STAGE))
                    SetCurrentStage(1);
                
                return PlayerPrefs.GetInt(KEY_STAGE, 1); 
            }
        }

        public void SetCurrentStage(int stage, bool isClear = false)
        {
            PlayerPrefs.SetInt(KEY_STAGE, stage);
            OnStageChanged?.Invoke(stage, isClear);

            // 스테이지 클리어로 단계가 올라간 경우, 새 스테이지 풀로 인형을 다시 생성
            if (isClear)
                GameDollCreator.Instance.ResetCurrentStage();
        }

        // 현재 스테이지에서 달성한 미션 수
        public int MissionCount
        {
            get => PlayerPrefs.GetInt(KEY_MISSION_COUNT, 0);
            private set
            {
                PlayerPrefs.SetInt(KEY_MISSION_COUNT, value);
                OnMissionCountChanged?.Invoke(value, TotalMissionCount);
            }
        }

        // 현재 스테이지의 총 미션 수
        public int TotalMissionCount
        {
            get
            {
                QuestData data = GetCurrentStageData();
                return data != null ? data.totalMissionCount : 0;
            }
        }

        // 현재 스테이지 데이터
        public QuestData GetCurrentStageData()
        {
            return GameQuestData.GetStage(CurrentStage);
        }

        public void CheckMissionAdd(string prefabName)
        {
            QuestData data = GetCurrentStageData();
            if (data == null)
            {
                Debug.LogError($"[GameQuestManager] No stage data for stage {CurrentStage}");
                return;
            }
            if (prefabName == data.GetModelPrefabName())
            {
                AddMission();
            }
        }

        // 미션 1회 달성
        public void AddMission()
        {
            MissionCount++;
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] MissionCount: {MissionCount} / {TotalMissionCount}");

            if (IsStageClear())
            {
                // 클리어한 스테이지의 다음 스테이지 데이터 (마지막 스테이지면 null)
                QuestData nextStageData = GameQuestData.GetStage(CurrentStage + 1);
                OnStageClear?.Invoke(nextStageData);

                // 즉시 다음 스테이지로 진행 (CurrentStage +1, MissionCount 0 리셋, OnStageChanged 발생)
                GoNextStage();
            }
        }

        // 스테이지 클리어 여부
        public bool IsStageClear()
        {
            return MissionCount >= TotalMissionCount;
        }

        // 다음 스테이지로 이동
        public void GoNextStage()
        {
            int next = CurrentStage + 1;
            if (next > GameQuestData.TotalStageCount)
            {
                Debug.Log("[GameQuestManager] All stages cleared!");
                return;
            }

            SetCurrentStage(next, true);
            MissionCount = 0;
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] Move to Stage {CurrentStage}");
        }

        // 데이터 초기화
        public void Reset()
        {
            SetCurrentStage(1);
            MissionCount = 0;
            PlayerPrefs.Save();
        }
    }
}
