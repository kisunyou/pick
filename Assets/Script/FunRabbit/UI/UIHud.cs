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
        
        /// <summary>
        /// mission
        /// </summary>
        [SerializeField] UIMissionHud missionHud;

        [SerializeField] GameObject playTimer;
        [SerializeField] Image playTimerIcon;
        [SerializeField] TextMeshProUGUI playTimerText;

        [SerializeField] TextMeshProUGUI coinText;
        [SerializeField] Button playButton;

        [SerializeField] GameObject grap;
        [SerializeField] Button grapButton;
        [SerializeField] Button collectionButton;

        [SerializeField] Button resetButton;
        [SerializeField] Button backButton;

        /// <summary>
        /// coin timer
        /// </summary>
        [SerializeField] UICoinTimerHud coinTimerHud;

        /// <summary>
        /// get doll trail
        /// </summary>
        [SerializeField] UIGetDollTrailHud getDollTrailHud;


        


        public UIGetDollTrailHud GetDollTrailHud => getDollTrailHud;

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

        // 코인 표시 연출: 최초 셋팅은 즉시, 이후 변경은 매 프레임 10씩 목표값까지 카운팅.
        private const long CoinCountStep = 10;
        private long _displayedCoin;        // 현재 화면에 표시 중인 값
        private long _targetCoin;           // 카운팅 목표 값
        private bool _coinInitialized;      // 최초 셋팅 여부
        private Coroutine _coinCountCoroutine;

        public void SetCoinText(long amount)
        {
            if (coinText == null)
                return;

            _targetCoin = amount;

            // 초기화(최초 셋팅)는 연출 없이 즉시 반영
            if (!_coinInitialized)
            {
                _coinInitialized = true;
                _displayedCoin = amount;
                ApplyCoinText(amount);
                return;
            }

            // 이미 카운팅 중이면 목표값만 갱신 (진행 중인 코루틴이 새 목표로 이어감)
            if (_coinCountCoroutine == null && _displayedCoin != _targetCoin)
                _coinCountCoroutine = StartCoroutine(CoinCountCoroutine());
        }

        // 표시값을 목표값까지 매 프레임 10씩 증가/감소시킨다. (마지막 스텝은 목표값에 정확히 맞춤)
        private IEnumerator CoinCountCoroutine()
        {
            while (_displayedCoin != _targetCoin)
            {
                long diff = _targetCoin - _displayedCoin;
                long step = System.Math.Min(System.Math.Abs(diff), CoinCountStep) * System.Math.Sign(diff);
                _displayedCoin += step;
                ApplyCoinText(_displayedCoin);
                yield return null;
            }
            _coinCountCoroutine = null;
        }

        // 세 자릿수마다 콤마를 붙여 표시한다. (예: 994299 → 994,299)
        private void ApplyCoinText(long amount)
        {
            coinText.text = amount.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture);
        }

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

        private void Start()
        {
            enterStageButton.onClick.AddListener(() => Control.OnClickStageEnterBtn());
            playButton.onClick.AddListener(() => Control.OnClickPlayBtn());
            grapButton.onClick.AddListener(()=> Control.OnClickGrapBtn());
            collectionButton.onClick.AddListener(() => Control.OnClickCollectionBtn());
            resetButton.onClick.AddListener(() => Control.OnClickResetButton());
            if (backButton != null)
                backButton.onClick.AddListener(() => Control.OnClickBackButton());
            Control.Initialize(this);
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

            if (!isInGame)
                HideTimer();
        }

        public void UpdateMissionProgressText(int current, int total)
        {
            if (missionHud != null)
                missionHud.UpdateMissionProgressText(current, total);
        }

        public void SetMissionIcon(string prefabPath)
        {
            if (missionHud != null)
                missionHud.SetMissionIcon(prefabPath);
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
            _timerIconTween?.Kill();
            _timerTextTween?.Kill();
        }
    }

    // GameMain.OnChangedStatus 이벤트에 따라 HUD를 갱신하는 컨트롤
    public class UIHudControl
    {
        private UIHud _hud;
        private Crane _crane;

        // 컬렉션 화면 진입 직전 상태 (뒤로가기 시 복귀할 상태)
        private GameStatus _statusBeforeCollection = GameStatus.LOBBY;

        // 크레인 조작 가능(CONTROL_MOVING) 시 표시할 제한 시간(초)
        private const float CraneControlTimeLimit = 15f;

        // resetButton은 현재 스테이지 인형(Actor)이 이 개수 이하일 때만 활성화한다.
        private const int ResetButtonMaxActorCount = 10;

        public void Initialize(UIHud hud)
        {
            _hud = hud;

            PlayerContext.CoinAmount.Attach(OnChangedCoinAmount);
            GameMain.SubscribeStatus(OnChangedGameStatus);

            // 인형 수에 따라 resetButton 활성/비활성 갱신
            StageManager.OnActorCountChanged += OnChangedActorCount;
            OnChangedActorCount(StageManager.ActorCount); // 현재 값으로 초기 반영
        }

        public void Deinitialize()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            UnsubscribeCrane();
            StageManager.OnActorCountChanged -= OnChangedActorCount;

            if (GameQuestManager.IsCheckInstance())
            {
                var questManager = GameQuestManager.Instance;
                questManager.OnMissionCountChanged -= OnMissionCountChanged;
                questManager.OnStageChanged -= OnStageChanged;
                questManager.OnStageClear -= OnStageClear;
            }

            _hud = null;
        }

        // 스테이지 인형(Actor) 수 변경 시: 7개 이하이면 resetButton 활성화, 초과면 비활성화
        private void OnChangedActorCount(int count)
        {
            if (_hud != null)
                _hud.SetActiveResetButton(count <= ResetButtonMaxActorCount);
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

            // 미션 카운트/스테이지 변경 이벤트 구독 후, GameQuestManager 정보로 HUD 초기 갱신
            var questManager = GameQuestManager.Instance;
            questManager.OnMissionCountChanged -= OnMissionCountChanged;
            questManager.OnMissionCountChanged += OnMissionCountChanged;
            questManager.OnStageChanged -= OnStageChanged;
            questManager.OnStageChanged += OnStageChanged;
            questManager.OnStageClear -= OnStageClear;
            questManager.OnStageClear += OnStageClear;
            RefreshMissionText();
            RefreshMissionIcon();

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
        }

        // GameQuestManager의 MissionCount 변경 시 호출되어 HUD 미션 텍스트를 갱신
        private void OnMissionCountChanged(int current, int total)
        {
            if (_hud != null)
                _hud.UpdateMissionProgressText(current, total);
        }

        // 현재 미션 진행도를 GameQuestManager에서 얻어 HUD에 갱신
        private void RefreshMissionText()
        {
            var questManager = GameQuestManager.Instance;
            OnMissionCountChanged(questManager.MissionCount, questManager.TotalMissionCount);
        }

        // GameQuestManager의 스테이지 변경 시 호출되어 HUD 미션 아이콘을 갱신
        private void OnStageChanged(int stage, bool isClear)
        {
            StageQuestData questData = GameQuestData.GetStage(stage);
            if (questData == null)
            {
                Debug.LogError($"[UIHudControl] No quest data for stage {stage}");
                return;
            }

            // 미션 아이콘은 항상 현재 스테이지 기준으로 갱신
            if (_hud != null)
                _hud.SetMissionIcon(questData.Doll.GetIconPrefabFullPath());
        }

        // 현재 스테이지 정보를 얻어 HUD 미션 아이콘을 갱신 (단순 갱신이므로 isClear=false)
        private void RefreshMissionIcon()
        {
            OnStageChanged(GameQuestManager.Instance.CurrentStage, false);
        }

        // 스테이지 클리어 시 호출되어 다음 스테이지의 인형 정보를 미션 클리어 패널로 표시
        private void OnStageClear(StageQuestData nextStageData)
        {
            if (nextStageData == null)
            {
                // 마지막 스테이지 클리어 - 표시할 다음 인형이 없음
                Debug.Log("[UIHudControl] 마지막 스테이지 클리어 - 미션 클리어 패널 생략");
                return;
            }

            // 이 시점의 CurrentStage는 아직 방금 클리어한 스테이지 (GoNextStage 호출 전)
            StageQuestData clearedStageData = GameQuestManager.Instance.GetCurrentStageData();

            var panel = UIMissionClearPanel.CreateOrGet();
            if (panel != null)
                panel.SetData(clearedStageData?.Doll.GetModelPrefabFullPath(), nextStageData.Doll.GetModelPrefabFullPath());
        }

        public void OnClickStageEnterBtn()
        {
            GameMain.Instance.SetGameStatus(GameStatus.INGAME);
        }

        // 뒤로가기: 컬렉션 화면이면 진입 전 상태로, 인게임 상태면 로비로 되돌린다.
        public void OnClickBackButton()
        {
            if (GameMain.Instance.CurrentStatus == GameStatus.COLLECTION)
                GameMain.Instance.SetGameStatus(_statusBeforeCollection);
            else if (GameMain.Instance.CurrentStatus == GameStatus.INGAME)
                GameMain.Instance.SetGameStatus(GameStatus.LOBBY);
            else if(GameMain.Instance.CurrentStatus == GameStatus.LOBBY)
                UIPopup.CreateOrGet().Set("Exit", "Are you sure you want to quit?", () =>
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

        private void OnChangedCoinAmount(long newAmount)
        {
            if (_hud != null)
                _hud.SetCoinText(newAmount);
        }

        public void OnClickCollectionBtn()
        {
            // 이미 컬렉션 화면이면 진입 전 상태를 덮어쓰지 않는다
            if (GameMain.Instance.CurrentStatus != GameStatus.COLLECTION)
                _statusBeforeCollection = GameMain.Instance.CurrentStatus;

            GameMain.Instance.SetGameStatus(GameStatus.COLLECTION);

            //if(UICollectionPanel.Get() == null)
            //    UICollectionPanel.CreateOrGet();
        }

        public void OnClickResetButton()
        {
            GameDollCreator.Instance.ResetCurrentStage();
        }
    }
}