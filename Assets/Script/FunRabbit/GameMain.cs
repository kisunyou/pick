using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public enum GameStatus
    {
        LOBBY,
        INGAME,
        COLLECTION,
    }

    public class GameMain : Singleton<GameMain>
    {
        [SerializeField] public float HorizontalSpeed = 500.0f;
        [SerializeField] public float DownSpeed = 15.0f;
        // 2026-07-16: 30 → 15. 하강력 잔류 버그(MovingDownStop 미호출) 수정으로 상승이
        // 2배 빨라져(≈5.88 m/s), 기존 체감(≈2.94 m/s)을 유지하도록 절반으로 낮춤.
        [SerializeField] public float UpSpeed = 15.0f;

        // 테스트용: 게임 시작 시 지정 스테이지(quest.json stage 번호)로 강제 이동.
        // 0 이하 = 비활성(저장된 진행대로 시작), 총 스테이지 수 초과 = 마지막 스테이지로 보정.
        // (const 대신 readonly - 토글 시 CS0162 방지)
        private static readonly int ForcedStartStage = 0;

        // 게임 상태 변경 이벤트
        public System.Action<GameStatus> OnChangedStatus { get; set; }
        // 스테이지 로드 완료 신호 (로딩 UI 등 로드 완료 시점이 필요한 곳에서 구독)
        public System.Action OnStageLoaded { get; set; }

        // 현재 게임 상태 (늦게 구독한 쪽에서 즉시 반영할 수 있도록 보관)
        public GameStatus CurrentStatus { get; private set; }
        // 직전 게임 상태 (컬렉션 화면에서 뒤로가기로 진입 전 상태에 복귀할 때 사용)
        public GameStatus PreviousStatus { get; private set; }
        public bool HasStatus { get; private set; }
        // 스테이지 로드가 한 번이라도 완료됐는지
        public bool IsStageLoaded { get; private set; }

        private void Start()
        {
            // 씬 로드/UI 생성 전에 스테이지를 확정한다 - 이후 CreateDolls가 이 스테이지 기준으로 인형을 생성
            if (ForcedStartStage > 0)
                ForceStartStage();

            PlayerContext.Initialize();

            // 다국어 문자열 매니저 - 다른 UI가 텍스트를 그리기 전에 가장 먼저 깨운다
            // (저장된 언어가 없으면 OS 시스템 언어를 감지해 기본값으로 사용)
            LanguageManager.MakeInstance();

            // UI2/Thumbnail 스프라이트를 미리 로드해둔다 (아이콘 표시 시마다 Resources.Load 하지 않도록)
            SpriteCache.Preload();

            // 게임/크레인 상태를 구독해 BGM을 재생하는 사운드 매니저를 깨운다
            AudioManager.MakeInstance();

            // 컬렉션(도감) 진입 시 획득 인형을 생성/배회시키는 매니저를 깨운다
            CollectionManager.MakeInstance();

            // LevelPlay 광고 SDK 초기화
            LevelPlayAds.MakeInstance();

            // Firebase Analytics 초기화
            FireBaseAnalyticsManager.MakeInstance();

            // 코인 타이머 완료 로컬 알림 - 채널 등록 + (Android 13+) 알림 권한 요청
            CoinRewardNotificationScheduler.Init();

            // 하단 공통 메뉴 바 (코인 표시/컬렉션 버튼) - UICoinTimerHud가 코인 비행 도착점을
            // 참조하므로 UIHud보다 먼저 연다
            UIBottomBar.CreateOrGet();

            var uiHUD = UIHud.CreateOrGet();


            // UIDpadControl 기본 인스턴스를 깨워 게임/크레인 상태를 구독시킴
            // (UIDpad는 표시가 필요한 시점에 컨트롤이 생성)
            _ = UIDpadControl.getDefault;

            // 생성 시 OnOpen에서 로딩 표시 시작, 이후 흐름은 UILoadingControl이 처리
            UILoading.CreateOrGet();

            SceneLoader.Instance.LoadAsync("Stage0", this.OnLoadedStage);
        }

        // 저장된 스테이지가 지정 스테이지(ForcedStartStage)와 다르면 강제 이동한다.
        // 이미 같은 스테이지면 아무것도 하지 않는다 (재실행 시 보스 hp 진행 보존).
        // SetCurrentStage의 RefreshBattleBoss는 이 시점에 씬이 없어 조용히 스킵되고,
        // 보스 반영은 Stage0 로드 후 ActorBattleSystem이 자체 Start()에서 수행한다.
        private static void ForceStartStage()
        {
            int lastStage = GameQuestData.TotalStageCount;
            if (lastStage <= 0)
                return;

            int stage = Mathf.Min(ForcedStartStage, lastStage);
            if (GameQuestManager.Instance.CurrentStage == stage)
                return;

            GameQuestManager.Instance.SetCurrentStage(stage);
            PlayerPrefs.Save();
            Debug.Log($"[GameMain] 테스트 모드: 스테이지 {stage}에서 시작");
        }

        private void OnLoadedStage()
        {
            SetGameStatus(GameStatus.LOBBY);
            GameDollCreator.Instance.CreateDolls();

            IsStageLoaded = true;
            OnStageLoaded?.Invoke();
        }

        public void SetGameStatus(GameStatus status)
        {
            if (!GameMachine.TryGetSetInstance(out _))
            {
                Debug.LogError("GameMachine 인스턴스가 존재하지 않습니다.");
                return;
            }

            // 실제로 상태가 바뀔 때만 직전 상태를 기록한다 (같은 상태 재설정으로 덮어쓰지 않게)
            if (HasStatus && CurrentStatus != status)
                PreviousStatus = CurrentStatus;

            CurrentStatus = status;
            HasStatus = true;
            OnChangedStatus?.Invoke(status);
        }

        // 상태 변경 구독 (+ 이미 설정된 상태가 있으면 즉시 1회 반영)
        public static void SubscribeStatus(System.Action<GameStatus> handler)
        {
            if (!IsCheckInstance())
                return;

            Instance.OnChangedStatus -= handler;
            Instance.OnChangedStatus += handler;

            if (Instance.HasStatus)
                handler(Instance.CurrentStatus);
        }

        public static void UnsubscribeStatus(System.Action<GameStatus> handler)
        {
            if (IsCheckInstance())
                Instance.OnChangedStatus -= handler;
        }

        // 스테이지 로드 완료 구독 (+ 이미 완료됐으면 즉시 1회 반영)
        public static void SubscribeStageLoaded(System.Action handler)
        {
            if (!IsCheckInstance())
                return;

            Instance.OnStageLoaded -= handler;
            Instance.OnStageLoaded += handler;

            if (Instance.IsStageLoaded)
                handler();
        }

        public static void UnsubscribeStageLoaded(System.Action handler)
        {
            if (IsCheckInstance())
                Instance.OnStageLoaded -= handler;
        }

        const long WatchAdRewardCoinAmount = 500;

        const float CoinRewardFlyDelay = 1f;  // 팝업 노출 후 코인 비행 연출 시작까지 대기 시간(초)
        const float CoinRewardCloseDelay = 1f; // 코인 비행 연출 시작 후 팝업이 닫히기까지 대기 시간(초)

        // 광고 시청 보상 플로우: 확인 팝업 → 리워드 광고 시청 → 보상 팝업(코인 아이콘 포함, 조작 불가) →
        // 1초 후 코인 비행 연출 + 지급 → 1초 후 팝업 자동 닫힘
        // 하루 최대 PlayerContext.WATCH_AD_DAILY_LIMIT회 - 남은 횟수를 본문에 표시하고,
        // 소진되면 확인 버튼을 비활성화한다 (횟수 차감은 보상 지급 시점 - 중도 이탈은 소모 안 됨).
        public void ShowWatchAdForCoinsPopup()
        {
            int remaining = PlayerContext.GetRemainingWatchAdCount();

            UIPopup popup = UIPopup.CreateOrGet();
            popup.Set(
                LanguageManager.Instance.Get("popup_watchad_title"),
                LanguageManager.Instance.Get("popup_watchad_body", WatchAdRewardCoinAmount, remaining),
                () =>
            {
                // 리워드 광고 시청 시도(광고 요청) 시점 - 매회 기록
                FireBaseAnalyticsManager.Instance.LogEvent("watch_ad_try");

                LevelPlayAds.Instance.ShowRewardedAd(() => ShowCoinRewardPopup(WatchAdRewardCoinAmount));
            });

            popup.SetOkButtonInteractable(remaining > 0);
        }

        // 백그라운드 전환 시 코인 타이머 완료 알림을 예약하고, 복귀 시 미발송분을 취소한다.
        // (게임 중에는 알림이 뜨지 않고, 안 하고 있을 때만 도착하도록)
        private void OnApplicationPause(bool pause)
        {
            if (pause)
                CoinRewardNotificationScheduler.OnAppPause();
            else
                CoinRewardNotificationScheduler.OnAppResume();
        }

        private void ShowCoinRewardPopup(long coinAmount)
        {
            // 리워드 광고 시청 완료(보상 지급 확정) 시점 - 매회 기록 + 하루 시청 횟수 차감
            FireBaseAnalyticsManager.Instance.LogEvent("watch_ad_complete");
            PlayerContext.AddWatchAdCount();

            UIPopup rewardPopup = UIPopup.CreateOrGet();
            rewardPopup.Set(
                LanguageManager.Instance.Get("popup_reward_title"),
                LanguageManager.Instance.Get("popup_reward_body", coinAmount),
                null, showCoinIcon: true, showButtons: false);

            StartCoroutine(PlayCoinRewardSequence(rewardPopup, coinAmount));
        }

        private IEnumerator PlayCoinRewardSequence(UIPopup rewardPopup, long coinAmount)
        {
            yield return new WaitForSeconds(CoinRewardFlyDelay);

            RectTransform coinIconTransform = rewardPopup.CoinIconTransform;
            if (coinIconTransform != null && UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(coinIconTransform, coinAmount);
            else
                PlayerContext.AddCoinAmount(coinAmount);

            yield return new WaitForSeconds(CoinRewardCloseDelay);

            rewardPopup.Close();
        }

        //private void OnGUI()
        //{
        //    if (GUI.Button(new Rect(20f, 20f, 160f, 50f), "TEST"))
        //    {

        //        PlayerContext.AddRandomBox();
        //        //-- 테스트용 코드: 인형 획득 연출 및 전투 시스템에 추가
        //        //ActorData actorData = GameActorData.Get("bear");
        //        //DollData dollData = new DollData("bear");

        //        //UIHud hud = UIHud.CreateOrGet();
        //        //Transform trailTarget = hud.AllyStackActors != null ? hud.AllyStackActors.transform : null;

        //        //hud.GetDollTrailHud.PlayGetDollTrail(dollData.GetIconPrefabFullPath(), trailTarget, () =>
        //        //{
        //        //    if (ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
        //        //        battleSystem.AddAllyActor(actorData);
        //        //});
        //        //----------------------------------------------------------------

        //        //PlayerContext.AddItemAmount(9, 10);

        //        //PlayerContext.AddRandomBoxProgressValue(1);

        //        //LevelPlayAds.Instance.ShowRewardedAd(
        //        //    () => Debug.Log("[GameMain] 보상 광고 테스트: 보상 지급됨"),
        //        //    () => Debug.Log("[GameMain] 보상 광고 테스트: 보상 실패/취소"));
        //        //UIHud.CreateOrGet().GetDollTrailHud.PlayGetDollTrail("UI2/Prefabs/MissionIconPrefab/pigMissionIcon",
        //        //    () => PlayerContext.AddRandomBoxProgressValue(0.1f));
        //        //UIRandomboxPanel.CreateOrGet();
        //        //UIHud.CreateOrGet().GetDollTrailHud.PlayGetDollTrail(Vector3.zero, "UI2/Prefabs/MissionIconPrefab/pigMissionIcon", null);
        //        //UIHud.CreateOrGet().OnTestPlayCoinGetEffect();
        //        //var questData = GameQuestData.GetStage(5);
        //        //var nextData = GameQuestData.GetStage(6);
        //        //var panel = UIMissionClearPanel.CreateOrGet();
        //        //if (panel != null)
        //        //    panel.SetData(questData?.animalKey, nextData?.animalKey);
        //        //UIHud.CreateOrGet().ShowTimer(10);
        //        //int stage = GameQuestManager.Instance.CurrentStage;
        //        //if (stage >= 0)
        //        //{
        //        //    StageManager.Save(stage);
        //        //    Debug.Log($"[GameMain] StageManager.Save({stage}) 완료");
        //        //}
        //        //else
        //        //{
        //        //    Debug.LogWarning($"[GameMain] 잘못된 stage 값({stage}), 저장 스킵.");
        //        //}
        //    }
        //}
    }
}
