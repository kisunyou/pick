using Firebase.Analytics;
using UnityEngine;

namespace FunRabbit
{
    // 스테이지 진행 관리. 스테이지 구성은 actor.json(GameActorData → GameQuestData 파사드)이 정본이다:
    // 연속 36스테이지 = 원본 12종(1~12) + _g 변형(13~24) + _r 변형(25~36).
    // 변형의 외형(model/texture)과 스탯(allyHp/bossHp 등)은 전부 actor.json 행 값을 그대로 쓴다.
    // 마지막(36)을 깨면 하드 구간 시작(25)으로 돌아가 하드를 무한 반복한다.
    public class GameQuestManager : Singleton<GameQuestManager>
    {
        private const string KEY_STAGE = "currentStage";
        private const string KEY_BOSS_HP = "bossHp";
        private const string KEY_CYCLE_LEGACY = "currentCycle"; // 구버전 회차 키 (마이그레이션 후 제거)
        private const string KEY_MAX_CLEARED_STAGE = "maxClearedStage"; // 지금까지 클리어한 최고 스테이지 (하드 순환에도 유지)

        // 난이도 구간 수 (1=원본, 2=_g, 3=_r) - 하드 시작/마이그레이션 계산과 로그 표기에 쓴다
        public const int MaxCycle = 3;

        // 전체 플레이 스테이지 수 (actor.json 행 수 = 36)
        public static int TotalPlayableStageCount => GameQuestData.TotalStageCount;

        // 구간당 스테이지 수 (36 / 3 = 12)
        public static int StagesPerBand => MaxCycle > 0 ? TotalPlayableStageCount / MaxCycle : 0;

        // 하드(_r) 구간 시작 스테이지 (25) - 마지막 클리어 후 여기로 돌아가 무한 반복
        public static int HardBandStartStage => TotalPlayableStageCount - StagesPerBand + 1;

        // 보스 hp가 변경될 때 발생하는 이벤트 (current, max)
        public event System.Action<int, int> OnBossHpChanged;
        // (stage, isClear) - isClear는 스테이지 클리어로 단계가 올라간 경우 true
        public event System.Action<int, bool> OnStageChanged;
        // 스테이지 클리어 시 발생하는 이벤트. 인자는 다음 스테이지 데이터
        public event System.Action<StageQuestData> OnStageClear;

        // 스테이지 클리어 대기 상태: 보스 hp는 0이 됐지만 크레인이 플레이 중(READY 아님)이라
        // 클리어 연출/스테이지 전환을 미뤄둔 상태. 스테이지 전환이 인형 풀을 리셋하므로,
        // 플레이 도중 처리하면 집고 있던 인형이 사라지고 그 플레이가 날아간다.
        private bool _pendingStageClear;

        // 현재 스테이지 (1~TotalPlayableStageCount 연속 번호)
        public int CurrentStage
        {
            get
            {
                if (!PlayerPrefs.HasKey(KEY_STAGE))
                    SetCurrentStage(1);

                int stage = PlayerPrefs.GetInt(KEY_STAGE, 1);

                // (마이그레이션) 구버전 회차 키(currentCycle) 저장분을 연속 스테이지 번호로 접는다
                if (PlayerPrefs.HasKey(KEY_CYCLE_LEGACY) && StagesPerBand > 0)
                {
                    int cycle = Mathf.Clamp(PlayerPrefs.GetInt(KEY_CYCLE_LEGACY, 1), 1, MaxCycle);
                    PlayerPrefs.DeleteKey(KEY_CYCLE_LEGACY);

                    if (cycle > 1 && stage <= StagesPerBand)
                    {
                        stage = (cycle - 1) * StagesPerBand + stage;
                        PlayerPrefs.SetInt(KEY_STAGE, stage);
                    }
                    PlayerPrefs.Save();
                }

                // 범위 초과(구버전 "올클리어" 저장 등) - 하드 구간 시작으로 보정
                if (stage > TotalPlayableStageCount && TotalPlayableStageCount > 0)
                {
                    SetCurrentStage(HardBandStartStage);
                    stage = HardBandStartStage;
                }

                return stage;
            }
        }

        // 현재 난이도 구간 (1~MaxCycle) - 로그/디버그 표기용 (외형·스탯은 actor.json 행이 결정)
        public int CurrentCycle
        {
            get
            {
                int perBand = StagesPerBand;
                return perBand > 0 ? Mathf.Clamp((CurrentStage - 1) / perBand + 1, 1, MaxCycle) : 1;
            }
        }

        // 모든 스테이지 클리어 상태. 하드 무한 반복 구조라 정상 플레이에서는 true가 되지 않는다 (가드 용도).
        public bool IsAllCleared => CurrentStage > TotalPlayableStageCount;

        // 지금까지 클리어한 최고 스테이지. 하드 구간 순환(36→25)으로 CurrentStage가 되돌아가도 유지된다 -
        // 도감(UICollectionPanel)의 변형(_g/_r) 등급 판정 기준. 기록이 없는 구버전 저장은
        // "현재 스테이지 - 1"(자연 진행과 동일)로 본다.
        public int MaxClearedStage => Mathf.Max(PlayerPrefs.GetInt(KEY_MAX_CLEARED_STAGE, 0), CurrentStage - 1);

        // (디버그 전용) 최고 클리어 기록을 강제 지정한다 - 스테이지 점프 시 도감 등급도 그 시점 기준이 되게.
        public void OverrideMaxClearedStage(int maxClearedStage)
        {
            PlayerPrefs.SetInt(KEY_MAX_CLEARED_STAGE, Mathf.Max(0, maxClearedStage));
        }

        public void SetCurrentStage(int stage, bool isClear = false)
        {
            PlayerPrefs.SetInt(KEY_STAGE, stage);

            // 새 스테이지의 보스 hp를 최대치로 채우고, battle_field(ActorBattleSystem)의 보스 모델을 갱신한다
            // (클리어로 넘어온 경우 새 보스 스폰은 미션 클리어 연출이 끝난 뒤로 미룬다)
            ResetBossHp();
            RefreshBattleBoss(stage, isClear);

            OnStageChanged?.Invoke(stage, isClear);

            // 스테이지 클리어로 단계가 올라간 경우, 새 스테이지 풀로 인형을 다시 생성
            if (isClear)
                GameDollCreator.Instance.ResetCurrentStage();
        }

        // 현재 스테이지 보스 몬스터의 최대 hp (actor.json 해당 행의 bossHp 그대로 - 변형 행은 값 자체가 2/3배)
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
        private void RefreshBattleBoss(int stage, bool deferSpawn = false)
        {
            if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
                return;

            // 데이터가 없으면(범위 밖) null을 넘겨 보스와 남은 아군을 정리한다
            StageQuestData stageData = GetStageData(stage);
            battleSystem.SetBoss(stageData != null ? GameActorData.Get(stageData.animalKey) : null, deferSpawn);
        }

        // 지정 스테이지의 스테이지 데이터 (actor.json 기반 1~36행)
        public StageQuestData GetStageData(int stage)
        {
            if (stage < 1 || stage > TotalPlayableStageCount)
                return null;

            return GameQuestData.GetStage(stage);
        }

        // 현재 스테이지 데이터
        public StageQuestData GetCurrentStageData()
        {
            return GetStageData(CurrentStage);
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

            // 범위 밖(비정상 상태)이면 보스가 없다 - 데미지/클리어 이벤트가 반복 발생하지 않도록 무시한다
            if (IsAllCleared)
                return;

            // 클리어 확정 후 크레인 READY 복귀 대기 중 - 추가 데미지는 무시한다 (hp 0 재클리어 방지)
            if (_pendingStageClear)
                return;

            BossHp = Mathf.Max(0, BossHp - damage);
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] BossHp: {BossHp} / {MaxBossHp}");

            FireBaseAnalyticsManager.Instance.LogEvent("boss_damage",
                new Parameter("stage", CurrentStage),
                new Parameter("boss_hp", BossHp));

            if (IsStageClear())
            {
                // 최고 클리어 기록 갱신 (하드 순환으로 CurrentStage가 25로 되돌아가도 이 값은 남는다)
                if (CurrentStage > PlayerPrefs.GetInt(KEY_MAX_CLEARED_STAGE, 0))
                    PlayerPrefs.SetInt(KEY_MAX_CLEARED_STAGE, CurrentStage);

                // 스테이지 클리어 - 매회 기록 (몇 번째 스테이지인지 포함)
                FireBaseAnalyticsManager.Instance.LogEvent("clear_stage", new Parameter("stage", CurrentStage));

                // 크레인이 플레이 중(READY 아님)이면 클리어 연출/스테이지 전환을 READY 복귀 시점으로 미룬다
                if (Crane.TryGetSetInstance(out Crane crane) && crane != null && crane.Status != CraneStatus.READY)
                {
                    _pendingStageClear = true;
                    crane.OnChangedStatus -= OnCraneStatusForPendingClear;
                    crane.OnChangedStatus += OnCraneStatusForPendingClear;
                    Debug.Log($"[GameQuestManager] 스테이지 클리어 - 크레인 플레이 중(status={crane.Status}), READY 복귀 후 처리");
                    return;
                }

                FireStageClear();
            }
        }

        // 크레인이 READY로 돌아오면 미뤄둔 스테이지 클리어를 처리한다.
        private void OnCraneStatusForPendingClear(int craneStatus)
        {
            if (!_pendingStageClear || craneStatus != CraneStatus.READY)
                return;

            _pendingStageClear = false;

            if (Crane.TryGetSetInstance(out Crane crane) && crane != null)
                crane.OnChangedStatus -= OnCraneStatusForPendingClear;

            // 크레인 상태 머신(SetStatus) 한가운데서 스테이지 전환(인형 파괴/재생성)을 실행하지 않도록
            // 한 프레임 뒤에 처리한다. (READY 직후 크레인이 하는 구스테이지 저장도 먼저 끝난다)
            StartCoroutine(FireStageClearNextFrame());
        }

        private System.Collections.IEnumerator FireStageClearNextFrame()
        {
            yield return null;
            FireStageClear();
        }

        // 클리어 이벤트 발행(미션 클리어 패널 표시) + 다음 스테이지로 전환
        private void FireStageClear()
        {
            // 클리어한 스테이지의 다음 스테이지 데이터 (마지막(36) 클리어면 하드 구간 시작(25) 데이터)
            StageQuestData nextStageData = GetStageData(GetNextStage());
            OnStageClear?.Invoke(nextStageData);

            // 다음 스테이지로 진행 (보스 hp 리셋, OnStageChanged 발생)
            GoNextStage();
        }

        // 스테이지 클리어 여부 (보스 hp가 0 이하)
        public bool IsStageClear()
        {
            return BossHp <= 0;
        }

        // 현재 스테이지 다음에 진행할 스테이지 번호 (마지막(36) 이후는 하드 구간 시작(25)으로 순환)
        private int GetNextStage()
        {
            int next = CurrentStage + 1;
            return next > TotalPlayableStageCount ? HardBandStartStage : next;
        }

        // 다음 스테이지로 이동. 마지막(36)을 깨면 하드 구간 시작(25)으로 돌아가 무한 반복한다.
        public void GoNextStage()
        {
            int next = GetNextStage();

            SetCurrentStage(next, true);
            PlayerPrefs.Save();
            Debug.Log($"[GameQuestManager] Move to Stage {CurrentStage} (구간 {CurrentCycle}, 보스 {GetCurrentStageData()?.animalKey})");
        }

        // 데이터 초기화 (구버전 회차 키/최고 클리어 기록 포함)
        public void Reset()
        {
            PlayerPrefs.DeleteKey(KEY_CYCLE_LEGACY);
            PlayerPrefs.DeleteKey(KEY_MAX_CLEARED_STAGE);
            SetCurrentStage(1);
            PlayerPrefs.Save();
        }
    }
}
