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
        private const string KEY_BOSS_HP_MAX = "bossHpMax";
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
        private bool _checkRestoredClear = true;
        private Coroutine _clearRoutine;

        // ===== PlayerPrefs 조회 캐시 =====
        // BattleActor.CanFight 가 액터마다 매 프레임 BossHp 를 읽는데, BossHp 1회 조회가 HasKey/GetInt 7회 안팎 +
        // CurrentStage(마이그레이션·범위 검사) 조회라 전투 중 부담이 컸다 → 검증을 마친 값을 메모리에 캐시한다.
        // - 쓰기(SetCurrentStage / BossHp setter / ResetBossHp)는 PlayerPrefs 와 캐시를 함께 갱신한다.
        // - PlayerPrefs 를 이 클래스 밖에서 통째로 바꾸는 경로는 반드시 InvalidateSaveCache 를 불러야 한다:
        //   클라우드 스냅샷 적용(CloudSaveSnapshot.ApplyToPlayerPrefs), 복원 직후 bossHp 직접 기록(CloudSaveManager.ApplyAndRefresh),
        //   로컬 데이터 삭제(계정 삭제·재시작 - CloudSaveManager.ResetLocalData).
        //   (계정 전환은 Sync → ApplyAndRefresh 경로로 들어오므로 위 훅으로 같이 무효화된다)
        private int _cachedStage;               // 0 = 미캐시
        private bool _bossHpCached;
        private int _cachedBossHp;

        // 캐시를 버려 다음 조회 때 PlayerPrefs 에서 다시 읽게 한다
        public void InvalidateSaveCache()
        {
            _cachedStage = 0;
            _bossHpCached = false;
        }

        // 인스턴스가 있을 때만 무효화 (PlayerPrefs 를 바꾸는 쪽이 싱글톤 생성을 유발하지 않도록)
        public static void InvalidateSaveCacheIfExists()
        {
            if (IsCheckInstance())
                Instance.InvalidateSaveCache();
        }

        public void PrepareForCloudRestore()
        {
            if (_clearRoutine != null) StopCoroutine(_clearRoutine);
            _clearRoutine = null;
            _pendingStageClear = false;
            _checkRestoredClear = true;
        }

        public void NotifyRestoredBossHp()
        {
            _checkRestoredClear = true;
            OnBossHpChanged?.Invoke(BossHp, MaxBossHp);
        }

        private void Update()
        {
            if (!_checkRestoredClear || !ActorBattleSystem.CanAdvanceBattle ||
                UIHud.Instance == null || OnStageClear == null) return;
            _checkRestoredClear = false;
            // HP=0 is the durable clear marker if the app closed before the popup could open.
            if (MaxBossHp > 0 && BossHp <= 0) QueueStageClear();
        }

        // 현재 스테이지 (1~TotalPlayableStageCount 연속 번호)
        public int CurrentStage
        {
            get
            {
                // 검증(마이그레이션·범위 보정)을 마친 값이 캐시돼 있으면 그대로 쓴다
                if (_cachedStage > 0)
                    return _cachedStage;

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

                // 스테이지 데이터가 아직 없으면(범위 보정 불가) 캐시하지 않는다 - 데이터 로드 후 다시 검증되게
                if (TotalPlayableStageCount > 0)
                    _cachedStage = stage;
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
            PrepareForCloudRestore();
            PlayerPrefs.SetInt(KEY_STAGE, stage);
            // 새 값은 다음 조회에서 범위 검사를 거쳐 다시 캐시된다 (보스 hp 캐시는 아래 ResetBossHp 가 갱신)
            InvalidateSaveCache();

            // 새 스테이지의 보스 hp를 최대치로 채우고, battle_field(ActorBattleSystem)의 보스 모델을 갱신한다
            // (클리어로 넘어온 경우 새 보스 스폰은 미션 클리어 연출이 끝난 뒤로 미룬다)
            ResetBossHp();
            RefreshBattleBoss(stage, isClear);

            OnStageChanged?.Invoke(stage, isClear);
            if (CloudSaveManager.IsCheckInstance() && CloudSaveManager.Instance.GameplayStarted && !SessionOperation.IsBusy)
                GameplayAnalytics.StageStarted(stage);

            // 스테이지 클리어로 단계가 올라간 경우, 새 스테이지 풀로 인형을 다시 생성
            if (isClear)
                GameDollCreator.Instance.ResetCurrentStage();
        }

        // 현재 스테이지 보스 몬스터의 최대 hp (actor.json의 스테이지별 난이도 곡선).
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
                // 보정(밸런스 변경 비율 변환·범위 제한)을 마친 값이 캐시돼 있으면 그대로 쓴다 (CanFight 매 프레임 조회)
                if (_bossHpCached)
                    return _cachedBossHp;

                if (!PlayerPrefs.HasKey(KEY_BOSS_HP))
                    ResetBossHp();

                // 밸런스 변경 시 남은 체력 비율을 한 번만 변환한다. 처치한 보스는 0을 유지한다.
                int maxHp = MaxBossHp;
                int hp = PlayerPrefs.GetInt(KEY_BOSS_HP, 0);
                int savedMax = PlayerPrefs.GetInt(KEY_BOSS_HP_MAX, 0);
                if (maxHp > 0 && savedMax != maxHp)
                {
                    int previousMax = BossHpBalance.ResolvePreviousMax(CurrentStage, savedMax, maxHp);
                    hp = BossHpBalance.RescaleRemaining(hp, previousMax, maxHp);
                    PlayerPrefs.SetInt(KEY_BOSS_HP, hp);
                    PlayerPrefs.SetInt(KEY_BOSS_HP_MAX, maxHp);
                    PlayerPrefs.Save();
                }
                int clamped = Mathf.Clamp(hp, 0, maxHp);
                // 스테이지 데이터가 없으면(maxHp 0) 캐시하지 않는다 - 데이터 로드 후 다시 보정되게
                if (maxHp > 0)
                {
                    _cachedBossHp = clamped;
                    _bossHpCached = true;
                }
                return clamped;
            }
            private set
            {
                int maxHp = MaxBossHp;
                PlayerPrefs.SetInt(KEY_BOSS_HP, value);
                SetBossHpCache(value, maxHp);
                OnBossHpChanged?.Invoke(value, maxHp);
            }
        }

        // 보스 hp 캐시를 (getter 와 같은 범위 제한으로) 갱신한다
        private void SetBossHpCache(int hp, int maxHp)
        {
            if (maxHp > 0)
            {
                _cachedBossHp = Mathf.Clamp(hp, 0, maxHp);
                _bossHpCached = true;
            }
            else
            {
                _bossHpCached = false;
            }
        }

        // 보스 hp를 현재 스테이지의 최대치로 되돌린다.
        private void ResetBossHp()
        {
            int maxHp = MaxBossHp;
            PlayerPrefs.SetInt(KEY_BOSS_HP, maxHp);
            if (maxHp > 0)
                PlayerPrefs.SetInt(KEY_BOSS_HP_MAX, maxHp);
            else
                PlayerPrefs.DeleteKey(KEY_BOSS_HP_MAX);
            SetBossHpCache(maxHp, maxHp);
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
            if (!ActorBattleSystem.CanAdvanceBattle) return;
            if (damage <= 0)
                return;

            // 범위 밖(비정상 상태)이면 보스가 없다 - 데미지/클리어 이벤트가 반복 발생하지 않도록 무시한다
            if (IsAllCleared)
                return;

            // 클리어 확정 후 크레인 READY 복귀 대기 중 - 추가 데미지는 무시한다 (hp 0 재클리어 방지)
            if (_pendingStageClear)
                return;

            BossHp = Mathf.Max(0, BossHp - damage);
            // 피격마다 디스크 flush 하지 않는다 - 값은 즉시 PlayerPrefs 에 쓰고 flush 는 주기/백그라운드 전환 시 1회
            // (강제 종료 시 최대 몇 초의 보스 hp 진행만 잃음, 보상·재화와 무관).
            // 단 hp 0(클리어 확정)은 아래 QueueStageClear 가 즉시 저장한다 - 재시작 시 클리어 복구 마커.
            PrefsSaveScheduler.MarkDirty();
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

                QueueStageClear();
            }
        }

        private void QueueStageClear()
        {
            if (_pendingStageClear) return;
            _pendingStageClear = true;
            if (CurrentStage > PlayerPrefs.GetInt(KEY_MAX_CLEARED_STAGE, 0))
                PlayerPrefs.SetInt(KEY_MAX_CLEARED_STAGE, CurrentStage);
            PlayerPrefs.Save();
            _clearRoutine = StartCoroutine(FireStageClearWhenReady());
        }

        private System.Collections.IEnumerator FireStageClearWhenReady()
        {
            while (_pendingStageClear)
            {
                // Always leave the crane's READY callback and old-stage save before advancing.
                yield return null;
                if (!ActorBattleSystem.CanAdvanceBattle || UIHud.Instance == null || OnStageClear == null)
                    continue;
                if (Crane.TryGetSetInstance(out Crane crane) && crane != null && crane.Status != CraneStatus.READY)
                    continue;
                if (UIMissionClearPanel.Get() != null) continue;
                if (FireStageClear()) yield break;
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // 클리어 이벤트 발행(미션 클리어 패널 표시) + 다음 스테이지로 전환
        private bool FireStageClear()
        {
            // 클리어한 스테이지의 다음 스테이지 데이터 (마지막(36) 클리어면 하드 구간 시작(25) 데이터)
            StageQuestData nextStageData = GetStageData(GetNextStage());
            try { OnStageClear?.Invoke(nextStageData); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                var failedPanel = UIMissionClearPanel.Get();
                if (failedPanel != null) failedPanel.Close();
                return false;
            }
            UIMissionClearPanel panel = UIMissionClearPanel.Get();
            if (panel == null) return false;

            // Do not silently advance a cleared boss until its popup has actually been created.
            _clearRoutine = null;

            // 클리어 코인 보상은 스테이지 전환과 "같은 저장 단위"로 먼저 지급한다.
            // (이전: 스테이지는 즉시 저장, 코인은 패널의 코인 연출 도착 시 지급 → 연출 중 앱 종료 시 보상 유실)
            // 지급 없이 전환만 저장되거나, 지급 후 전환이 안 돼 재시작 시 hp=0 재클리어로 중복 지급되는
            // 중간 상태가 남지 않도록 한 번의 PlayerPrefs.Save 로 묶는다. 패널의 코인 연출은 표시 전용.
            long rewardCoin = GetCurrentClearCoinReward();
            PlayerContext.RunAtomic(() =>
            {
                if (rewardCoin > 0)
                    PlayerContext.AddCoinAmount(rewardCoin);
                GoNextStage();
            });

            // 같은 프레임에 알려 하단 바 숫자가 코인 비행 도착 전에 먼저 올라가지 않게 한다
            if (rewardCoin > 0 && panel != null)
                panel.OnClearRewardGranted(rewardCoin);
            return true;
        }

        // 현재(방금 클리어한) 스테이지의 클리어 코인 보상 (UIMissionClearPanel 표기와 같은 출처)
        private long GetCurrentClearCoinReward()
        {
            StageQuestData data = GetCurrentStageData();
            return Mathf.Max(0, GameActorData.GetClearCoinReward(data?.animalKey));
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
