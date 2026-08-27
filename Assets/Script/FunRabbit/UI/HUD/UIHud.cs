using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIHud",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIHud : BaseUIView<UIHud>
    {
        [SerializeField] GameObject lobbyHUD, inGameHUD, collectionHUD;
        [SerializeField] Button enterStageButton;

        [SerializeField] GameObject playTimer;
        [SerializeField] Image playTimerIcon;
        [SerializeField] TextMeshProUGUI playTimerText;

        [SerializeField] Button playButton;

        [SerializeField] GameObject grap;
        [SerializeField] Button grapButton;

        [SerializeField] Button resetButton;
        [SerializeField] Button backButton;
        [SerializeField] GameObject resetCount;
        [SerializeField] TextMeshProUGUI resetCountText;
        [SerializeField] RawImage bossCamView;
        [SerializeField] GameObject bossBattle;

        /// <summary>
        /// coin timer
        /// </summary>
        [SerializeField] UICoinTimerHud coinTimerHud;

        /// <summary>
        /// get doll trail
        /// </summary>
        [SerializeField] UIGetDollTrailHud getDollTrailHud;

        [SerializeField] UICustumGage bossHPGage;

        [SerializeField] UIAllyStackActors allyStackActors;

        [SerializeField] BattleActorBuffManager buffManager;

        public UIGetDollTrailHud GetDollTrailHud => getDollTrailHud;

        // ally 대기열(스택) UI. 슬롯이 꽉 찼을 때 대기 중인 ally 아이콘을 보여준다 (ActorBattleSystem이 사용).
        public UIAllyStackActors AllyStackActors => allyStackActors;

        // 버프 아이콘(BuffIcons) 관리자. AddBuff(BuffType)로 버프 스택을 올린다.
        public BattleActorBuffManager BuffManager => buffManager;

        // bossCamView(RawImage)가 화면에서 차지하는 RectTransform. bossCamera의 뷰포트 좌표를
        // 이 사각형 안으로 매핑하면 실제 화면(HUD) 위치가 나온다 (UIActorHPGage가 사용).
        public RectTransform BossCamViewRect => bossCamView != null ? (RectTransform)bossCamView.transform : null;

        // 타이머 설정
        private const int TimerWarningThreshold = 5; // 빨간색/회전/스케일 연출이 시작되는 남은 시간(초)
        private const string CountdownSoundName = "game_sounds/ui/play_countdown"; // 경고 구간 매 초 재생할 카운트다운 효과음
        private const float TimerIconSwingAngle = 20f; // 시계 아이콘이 흔들리는 각도
        private const float TimerIconSwingDuration = 0.25f; // 한 방향으로 흔드는 시간
        private const float TimerTextPulseScale = 1.4f; // 매 초 박자마다 숫자가 커지는 최대 배율
        private const float TimerTextPulseDuration = 0.18f; // 펄스 한쪽(커지거나 작아지는) 시간
        private readonly Color _timerNormalColor = Color.white;
        private readonly Color _timerWarningColor = Color.red;

        private Coroutine _timerCoroutine;
        private Tween _timerIconTween;
        private Tween _timerTextTween;

        public UIHudControl Control { get; private set; } = new UIHudControl();

        public System.Action OnEnterStageButtonClicked { get; set; }

        public void SetActivePlayButton(bool isActive)
        {
            if (playButton.gameObject.activeSelf == isActive)
                return;
            playButton.gameObject.SetActive(isActive);
        }

        public void SetActiveGrabButton(bool isActive)
        {
            if (grap.activeSelf == isActive)
                return;
            grap.SetActive(isActive);
        }

        public void SetActiveResetButton(bool isActive)
        {
            if (resetButton == null || resetButton.gameObject.activeSelf == isActive)
                return;
            resetButton.gameObject.SetActive(isActive);
        }

        // 사용 가능한 리셋 횟수를 표시한다 (아이템 보유 수 + 인형 수 조건 무료 리셋). 0이면 resetCount 오브젝트를 숨긴다.
        public void SetResetCountText(int count)
        {
            if (resetCountText != null)
                resetCountText.text = count.ToString();

            if (resetCount != null)
                resetCount.SetActive(count > 0);
        }

        private void Start()
        {
            enterStageButton.onClick.AddListener(() => Control.OnClickStageEnterBtn());
            playButton.onClick.AddListener(() => Control.OnClickPlayBtn());
            grapButton.onClick.AddListener(()=> Control.OnClickGrapBtn());
            resetButton.onClick.AddListener(() => Control.OnClickResetButton());
            if (backButton != null)
                backButton.onClick.AddListener(() => Control.OnClickBackButton());
            Control.Initialize(this);

            // 보스 카메라는 Stage0 씬 로드 후에 생성되므로, 로드 완료 시점에 연결한다.
            // (SubscribeStageLoaded는 이미 로드 완료된 상태면 즉시 1회 콜백해준다)
            GameMain.SubscribeStageLoaded(OnStageLoaded);

            // 보스 hp 게이지: 데미지(OnBossHpChanged)뿐 아니라 스테이지 전환(OnStageChanged, ResetBossHp 이후 발생)에도 갱신
            GameQuestManager questManager = GameQuestManager.Instance;
            questManager.OnBossHpChanged += OnBossHpChanged;
            questManager.OnStageChanged += OnStageChangedRefreshBossHPGage;
            RefreshBossHPGage();
        }

        private void OnBossHpChanged(int current, int max) => SetBossHPGage(current, max);

        private void OnStageChangedRefreshBossHPGage(int stage, bool isClear) => RefreshBossHPGage();

        private void RefreshBossHPGage()
        {
            GameQuestManager questManager = GameQuestManager.Instance;
            SetBossHPGage(questManager.BossHp, questManager.MaxBossHp);
        }

        private void SetBossHPGage(int current, int max)
        {
            if (bossHPGage == null)
                return;

            bossHPGage.SetGage(max > 0 ? (float)current / max : 0f);
        }

        // 보스 카메라(Stage0에 배치)의 RenderTexture를 상단 화면에 표시 (항상 노출)
        private void OnStageLoaded()
        {
            if (bossCamView != null && BossCamera.TryGetSetInstance(out BossCamera bossCam))
                bossCamView.texture = bossCam.TargetTexture;
        }

        // 외부(테스트/버튼 등)에서 코인 획득 연출만 단독으로 재생하기 위한 진입점.
        public void OnTestPlayCoinGetEffect()
        {
            if (coinTimerHud != null)
                coinTimerHud.OnTestPlayCoinGetEffect();
        }

        // 게임 상태에 맞는 HUD 패널(lobby/inGame/collection) 하나만 활성화한다.
        public void SetActiveHud(GameStatus status)
        {
            bool isInGame = status == GameStatus.INGAME;

            lobbyHUD.SetActive(status == GameStatus.LOBBY);
            inGameHUD.SetActive(isInGame);
            if (collectionHUD != null)
                collectionHUD.SetActive(status == GameStatus.COLLECTION);

            // 보스 배틀(bossCamView)은 원래 게임 상태와 무관하게 항상 노출됐는데, 컬렉션(도감) 화면에서는 숨긴다.
            if (bossBattle != null)
                bossBattle.SetActive(status != GameStatus.COLLECTION);

            if (!isInGame)
                HideTimer();
        }

        public void ShowTimer(bool active, float duration = 0f, System.Action onComplete = null)
        {
            if (active)
                ShowTimer(duration, onComplete);
            else
                HideTimer();
        }

        public void ShowTimer(float duration, System.Action onComplete = null)
        {
            // 기존 타이머/연출 정리 후 다시 시작
            HideTimer();

            playTimer.SetActive(true);
            _timerCoroutine = StartCoroutine(TimerCoroutine(duration, onComplete));
        }

        public void HideTimer()
        {
            if (_timerCoroutine != null)
            {
                StopCoroutine(_timerCoroutine);
                _timerCoroutine = null;
            }

            StopTimerWarningAnim();

            if (playTimerText != null)
                playTimerText.color = _timerNormalColor;

            if (playTimer != null && playTimer.activeSelf)
                playTimer.SetActive(false);
        }

        private IEnumerator TimerCoroutine(float duration, System.Action onComplete)
        {
            int remaining = Mathf.CeilToInt(duration);

            // 숫자가 제자리에서 커지도록 피벗을 중앙으로 맞춤 (위치는 유지)
            CenterTextPivot();

            // 시작 상태 초기화
            playTimerText.color = _timerNormalColor;
            playTimerText.transform.localScale = Vector3.one;
            playTimerIcon.transform.localRotation = Quaternion.identity;

            while (remaining > 0)
            {
                playTimerText.text = remaining.ToString();

                // 5초 이하부터 빨간색 + 시계 아이콘 회전 + 매 초 박자에 맞춘 숫자 스케일 연출 + 카운트다운 효과음
                if (remaining <= TimerWarningThreshold)
                {
                    playTimerText.color = _timerWarningColor;

                    if (_timerIconTween == null)
                        PlayTimerIconAnim();

                    PlayTimerTextPulse();
                    AudioManager.Instance.PlaySfx(CountdownSoundName);
                }

                yield return new WaitForSeconds(1f);
                remaining--;
            }

            // 0초 도달: 연출 제거하고 멈춤
            playTimerText.text = "0";
            StopTimerWarningAnim();

            _timerCoroutine = null;
            onComplete?.Invoke();
        }

        // 시계 아이콘을 반시계 ↔ 시계 방향으로 계속 흔들어 긴박함을 연출
        private void PlayTimerIconAnim()
        {
            _timerIconTween?.Kill();
            Transform iconTransform = playTimerIcon.transform;
            // 반시계 방향(+각도)에서 시작해 시계 방향(-각도)으로 왕복
            iconTransform.localRotation = Quaternion.Euler(0f, 0f, TimerIconSwingAngle);

            _timerIconTween = iconTransform
                .DOLocalRotate(new Vector3(0f, 0f, -TimerIconSwingAngle), TimerIconSwingDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        // 초가 넘어가는 박자에 맞춰 숫자를 한 번 커졌다 작아지게 (제자리 펄스)
        private void PlayTimerTextPulse()
        {
            Transform textTransform = playTimerText.transform;

            _timerTextTween?.Kill();
            textTransform.localScale = Vector3.one;

            // 커졌다(1->TimerTextPulseScale) 다시 작아지는(->1) 1회 펄스
            _timerTextTween = textTransform
                .DOScale(TimerTextPulseScale, TimerTextPulseDuration)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo);
        }

        // 숫자가 제자리에서 스케일링되도록 RectTransform 피벗을 중앙(0.5, 0.5)으로 맞춤 (화면상 위치는 유지)
        private void CenterTextPivot()
        {
            RectTransform rect = playTimerText.rectTransform;
            Vector2 centerPivot = new Vector2(0.5f, 0.5f);

            if (rect.pivot == centerPivot)
                return;

            Vector2 size = rect.rect.size;
            Vector2 deltaPivot = rect.pivot - centerPivot;
            Vector3 deltaPosition = new Vector3(deltaPivot.x * size.x, deltaPivot.y * size.y, 0f);

            rect.pivot = centerPivot;
            rect.localPosition -= deltaPosition;
        }

        private void StopTimerWarningAnim()
        {
            _timerIconTween?.Kill();
            _timerIconTween = null;

            _timerTextTween?.Kill();
            _timerTextTween = null;

            if (playTimerIcon != null)
                playTimerIcon.transform.localRotation = Quaternion.identity;

            if (playTimerText != null)
                playTimerText.transform.localScale = Vector3.one;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Control.Deinitialize();
            GameMain.UnsubscribeStageLoaded(OnStageLoaded);

            if (GameQuestManager.IsCheckInstance())
            {
                GameQuestManager questManager = GameQuestManager.Instance;
                questManager.OnBossHpChanged -= OnBossHpChanged;
                questManager.OnStageChanged -= OnStageChangedRefreshBossHPGage;
            }

            _timerIconTween?.Kill();
            _timerTextTween?.Kill();
        }
    }

    // GameMain.OnChangedStatus 이벤트에 따라 HUD를 갱신하는 컨트롤
    public class UIHudControl
    {
        private UIHud _hud;
        private Crane _crane;

        // 크레인 조작 가능(CONTROL_MOVING) 시 표시할 제한 시간(초)
        private const float CraneControlTimeLimit = 15f;

        // resetButton은 현재 스테이지 인형(Actor)이 이 개수 이하이거나, 리셋 아이템을 보유 중이면 활성화한다.
        private const int ResetButtonMaxActorCount = 10;

        private int _actorCount;
        private int _resetItemCount;

        public void Initialize(UIHud hud)
        {
            _hud = hud;

            GameMain.SubscribeStatus(OnChangedGameStatus);

            // 인형 수에 따라 resetButton 활성/비활성 갱신
            StageManager.OnActorCountChanged += OnChangedActorCount;
            OnChangedActorCount(StageManager.ActorCount); // 현재 값으로 초기 반영

            // 리셋 아이템 보유 수에 따라 resetButton 활성/비활성 + 카운트 텍스트 갱신
            // (Attach는 현재 값으로 즉시 1회 콜백되므로 초기 표시도 여기서 처리된다)
            PlayerContext.AttachItemAmount(PlayerContext.RESET_ITEM_KEY, OnChangedResetItemCount);
        }

        public void Deinitialize()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            UnsubscribeCrane();
            StageManager.OnActorCountChanged -= OnChangedActorCount;
            PlayerContext.DetachItemAmount(PlayerContext.RESET_ITEM_KEY, OnChangedResetItemCount);

            if (GameQuestManager.IsCheckInstance())
            {
                var questManager = GameQuestManager.Instance;
                questManager.OnStageClear -= OnStageClear;
            }

            _hud = null;
        }

        // 스테이지 인형(Actor) 수 변경 시: ResetButtonMaxActorCount 이하이면 resetButton 활성화, 초과면 비활성화 (리셋 아이템 보유 조건과 별개로 갱신)
        private void OnChangedActorCount(int count)
        {
            _actorCount = count;
            RefreshResetButtonActive();
        }

        // 리셋 아이템 보유 수 변경 시: resetButton 활성 조건 + 카운트 표시 재평가
        private void OnChangedResetItemCount(long count)
        {
            _resetItemCount = (int)count;
            RefreshResetButtonActive();
        }

        // 인형 수 조건 OR 리셋 아이템 보유 조건 중 하나라도 만족하면 resetButton 활성화.
        // 카운트 배지는 아이템 보유 수에 인형 수 조건으로 열리는 리셋 1회를 더해 표시한다.
        private void RefreshResetButtonActive()
        {
            if (_hud == null)
                return;

            bool resetByActorCount = _actorCount <= ResetButtonMaxActorCount;

            _hud.SetActiveResetButton(resetByActorCount || _resetItemCount > 0);
            _hud.SetResetCountText(_resetItemCount + (resetByActorCount ? 1 : 0));
        }

        // 게임 상태 변경 시 HUD 갱신
        private void OnChangedGameStatus(GameStatus status)
        {
            if (_hud == null)
                return;

            _hud.SetActiveHud(status);

            // INGAME이 아니면(LOBBY/COLLECTION) 크레인 상태 구독을 해제하고 끝 - 그 외 갱신 불필요
            if (status != GameStatus.INGAME)
            {
                UnsubscribeCrane();
                return;
            }

            // 스테이지 클리어 이벤트 구독 (미션 텍스트/아이콘 갱신은 보스 배틀 도입으로 더 이상 사용하지 않음)
            var questManager = GameQuestManager.Instance;
            questManager.OnStageClear -= OnStageClear;
            questManager.OnStageClear += OnStageClear;

            // 크레인 상태에 따라 제한 시간 타이머 표시
            SubscribeCrane();
        }

        // 크레인 상태 구독 (구독 즉시 현재 상태가 반영됨)
        private void SubscribeCrane()
        {
            if (_crane == null && Crane.TryGetSetInstance(out Crane crane))
                _crane = crane;

            _crane?.SubscribeStatus(OnChangedCraneStatus);
        }

        private void UnsubscribeCrane()
        {
            _crane?.UnsubscribeStatus(OnChangedCraneStatus);
            _crane = null;
        }

        // 크레인 상태 변경 시: 조작 가능(CONTROL_MOVING)일 때만 제한 시간 타이머 표시
        private void OnChangedCraneStatus(int craneStatus)
        {
            if (_hud == null)
                return;

            _hud.ShowTimer(craneStatus == CraneStatus.CONTROL_MOVING,
                CraneControlTimeLimit,
                () =>
                {
                    if (Crane.TryGetSetInstance(out Crane crane))
                        crane.StartGrabSequence();
                });
            _hud.SetActivePlayButton(craneStatus == CraneStatus.READY);
            _hud.SetActiveGrabButton(craneStatus == CraneStatus.CONTROL_MOVING);

            bool isCanUIActive = craneStatus == CraneStatus.READY || craneStatus == CraneStatus.CONTROL_MOVING;

            UIManager.Instance.SetCanvasGroup(UILayer.Hud, isCanUIActive);
        }

        // 스테이지 클리어 시 호출되어 다음 스테이지의 인형 정보를 미션 클리어 패널로 표시
        private void OnStageClear(StageQuestData nextStageData)
        {
            // 이 시점의 CurrentStage는 아직 방금 클리어한 스테이지 (GoNextStage 호출 전)
            StageQuestData clearedStageData = GameQuestManager.Instance.GetCurrentStageData();

            // 마지막 스테이지 클리어(nextStageData == null)면 newAnimalKey를 null로 넘겨 올클리어 연출을 재생한다
            // (보스 → 일반 인형 변신 후, 다음 보스 등장 대신 ALL CLEAR 타이틀)
            var panel = UIMissionClearPanel.CreateOrGet();
            if (panel != null)
                panel.SetData(clearedStageData?.animalKey, nextStageData?.animalKey);
        }

        public void OnClickStageEnterBtn()
        {
            FireBaseAnalyticsManager.Instance.LogEventOnce("click_enter_stage");
            GameMain.Instance.SetGameStatus(GameStatus.INGAME);
        }

        // 뒤로가기: 컬렉션 화면이면 진입 전 상태로, 인게임 상태면 로비로 되돌린다.
        public void OnClickBackButton()
        {
            if (GameMain.Instance.CurrentStatus == GameStatus.COLLECTION)
                GameMain.Instance.SetGameStatus(GameMain.Instance.PreviousStatus);
            else if (GameMain.Instance.CurrentStatus == GameStatus.INGAME)
                GameMain.Instance.SetGameStatus(GameStatus.LOBBY);
            else if(GameMain.Instance.CurrentStatus == GameStatus.LOBBY)
                UIPopup.CreateOrGet().Set(
                    LanguageManager.Instance.Get("popup_exit_title"),
                    LanguageManager.Instance.Get("popup_exit_body"), () =>
                {
#if !UNITY_EDITOR
                    Application.Quit();
#endif
                });
        }

        public void OnClickPlayBtn()
        {
            if (PlayerContext.TrySpendCoin(100))
            {
                FireBaseAnalyticsManager.Instance.LogEventOnce("click_play");
                Debug.Log("[UIHudControl] 플레이 버튼 클릭: 100 코인 차감");
            }
            else
            {
                Debug.Log("[UIHudControl] 플레이 버튼 클릭: 코인 부족");
                return;
            }

            if (Crane.TryGetSetInstance(out Crane crane))
            {
                crane.SetStatus(CraneStatus.CONTROL_MOVING);
            }
        }

        public void OnClickGrapBtn()
        {
            if (Crane.TryGetSetInstance(out Crane crane))
            {
                crane.StartGrabSequence();
            }
        }

        public void OnClickResetButton()
        {
            GameDollCreator.Instance.ResetCurrentStage();
        }
    }
}