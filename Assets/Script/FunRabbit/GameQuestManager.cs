using Firebase.Analytics;
using UnityEngine;

namespace FunRabbit
{
    public class GameQuestManager : Singleton<GameQuestManager>
    {
        private const string KEY_STAGE = "currentStage";
        private const string KEY_BOSS_HP = "bossHp";

        // 보스 hp가 변경될 때 발생하는 이벤트 (current, max)
        public event System.Action<int, int> OnBossHpChanged;
        // (stage, isClear) - isClear는 스테이지 클리어로 단계가 올라간 경우 true
        public event System.Action<int, bool> OnStageChanged;
        // 스테이지 클리어 시 발생하는 이벤트. 인자는 다음 스테이지 데이터 (마지막 스테이지면 null)
        public event System.Action<StageQuestData> OnStageClear;

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

        // 모든 스테이지 클리어 상태 (CurrentStage == TotalStageCount + 1). 보스가 없고, 전 동물이
        // "클리어됨"(stage < CurrentStage) 판정을 받아 도감/뽑기 풀/랜덤박스 아군에 마지막 동물까지 포함된다.
        public bool IsAllCleared => CurrentStage > GameQuestData.TotalStageCount;

        public void SetCurrentStage(int stage, bool isClear = false)
        {
            PlayerPrefs.SetInt(KEY_STAGE, stage);

            // 새 스테이지의 보스 hp를 최대치로 채우고, battle_field(ActorBattleSystem)의 보스 모델을 갱신한다
            ResetBossHp();
            RefreshBattleBoss(stage);

            OnStageChanged?.Invoke(stage, isClear);

            // 스테이지 클리어로 단계가 올라간 경우, 새 스테이지 풀로 인형을 다시 생성
            if (isClear)
                GameDollCreator.Instance.ResetCurrentStage();
        }

        // 현재 스테이지 보스 몬스터의 최대 hp (actor.json의 해당 animalKey bossHp 필드)
        public int MaxBossHp
        {
            get
            {
                StageQuestData data = GetCurrentStageData();
                return data != null ? GameActorData.GetBossHp(data.animalKey) : 0;
            }
        }

        // 현재 스테이지 보스 몬스터의 남은 hp
        public int BossHp
        {
            get
            {
                if (!PlayerPrefs.HasKey(KEY_BOSS_HP))
                    ResetBossHp();

                // actor.json bossHp 하향 조정 뒤 이전 저장값이 새 최대치를 넘을 수 있다 - 최대치로 잘라 준다
                // (게이지 비율 1 초과 방지). 잘린 값은 다음 DamageBoss에서 저장된다.
                return Mathf.Min(PlayerPrefs.GetInt(KEY_BOSS_HP, 0), MaxBossHp);
            }
            private set
            {
                PlayerPrefs.SetInt(KEY_BOSS_HP, value);
                OnBossHpChanged?.Invoke(value, MaxBossHp);
            }
        }

        // 보스 hp를 현재 스테이지의 최대치로 되돌린다.
        private void ResetBossHp()
        {
            PlayerPrefs.SetInt(KEY_BOSS_HP, MaxBossHp);
        }

        // 지정 스테이지의 보스를 battle_field(ActorBattleSystem)에 반영한다. (씬에 없으면 조용히 무시 - 자체 Start()에서 로드)
        private void RefreshBattleBoss(int stage)
        {
            if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
                return;

            // 올클리어 단계(stageData == null)면 null을 넘겨 보스와 남은 아군을 정리한다
            StageQuestData stageData = GameQuestData.GetStage(stage);
            battleSystem.SetBoss(stageData != null ? GameActorData.Get(stageData.animalKey) : null);
        }

        // 현재 스테이지 데이터
        public StageQuestData GetCurrentStageData()
        {
            return GameQuestData.GetStage(CurrentStage);
        }

        // 해당 인형(prefabName)이 현재 스테이지의 미션 대상인지 판정한다. (보스 공격은 즉시 처리하지 않음)
        // 실제 처리는 트레일 연출이 도착한 시점에 ActorBattleSystem.AddAllyActor(-> DamageBoss) / PlayerContext.AddRandomBox로 한다.
        public bool IsMissionTarget(string prefabName)
        {
            StageQuestData data = GetCurrentStageData();
            if (data == null)
            {
                Debug.LogError($"[GameQuestManager] No stage data for stage {CurrentStage}");
                return false;
            }

            return prefabName.Contains(data.Doll.GetModelPrefabName());
        }

        // ally 액터가 보스를 공격했을 때 호출: attackPower만큼 보스 hp를 깎고, 0 이하가 되면 스테이지를 클리어한다.
        public void DamageBoss(int damage)
        {
            if (damage <= 0)
                return;

            // 올클리어 뒤에는 보스가 없다 - 데미지/클리어 이벤트가 반복 발생하지 않도록 무시한다
            if (IsAllCleared)
                return;

            BossHp = Mathf.Max(0, BossHp - damage);
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] BossHp: {BossHp} / {MaxBossHp}");

            FireBaseAnalyticsManager.Instance.LogEvent("boss_damage",
                new Parameter("stage", CurrentStage),
                new Parameter("boss_hp", BossHp));

            if (IsStageClear())
            {
                // 스테이지 클리어 - 매회 기록 (몇 번째 스테이지인지 포함)
                FireBaseAnalyticsManager.Instance.LogEvent("clear_stage", new Parameter("stage", CurrentStage));

                // 클리어한 스테이지의 다음 스테이지 데이터 (마지막 스테이지면 null)
                StageQuestData nextStageData = GameQuestData.GetStage(CurrentStage + 1);
                OnStageClear?.Invoke(nextStageData);

                // 즉시 다음 스테이지로 진행 (CurrentStage +1, 보스 hp 리셋, OnStageChanged 발생)
                GoNextStage();
            }
        }

        // 스테이지 클리어 여부 (보스 hp가 0 이하)
        public bool IsStageClear()
        {
            return BossHp <= 0;
        }

        // 다음 스테이지로 이동
        public void GoNextStage()
        {
            // 이미 올클리어 단계면 더 진행하지 않는다
            if (IsAllCleared)
                return;

            // 마지막 스테이지 클리어 시 next = TotalStageCount + 1 = 올클리어 단계로 진입한다
            // (GetCurrentStageData() == null, MaxBossHp 0, RefreshBattleBoss가 보스를 제거, 인형 풀에 마지막 동물 포함)
            int next = CurrentStage + 1;
            if (next > GameQuestData.TotalStageCount)
                Debug.Log("[GameQuestManager] All stages cleared!");

            SetCurrentStage(next, true);
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] Move to Stage {CurrentStage}");
        }

        // 데이터 초기화
        public void Reset()
        {
            SetCurrentStage(1);
            PlayerPrefs.Save();
        }
    }
}
