using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 화면 하단 공통 메뉴 바 (UIHud의 Bottom을 별도 프리팹으로 분리한 뷰).
    // - 코인 보유량 표시(카운팅 연출 - 최소 10/프레임, 최대 3초 내 완료)
    // - 컬렉션(도감) 진입 버튼
    // - 코인 획득 연출(출발점 → 코인 아이콘으로 코인이 날아와 도착할 때마다 "표시"를 분할 반영)
    //   ※ 실제 지급(저장)은 연출 전에 호출자가 먼저 확정한다. 연출은 표시 보류(CoinDisplayHold)를
    //     도착한 코인 몫만큼 풀어, 숫자가 코인 도착에 맞춰 올라가 보이게 할 뿐이다.
    [UIOption(
        Path = "UI2/Prefabs/UIBottomBar",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIBottomBar : BaseUIView<UIBottomBar>
    {
        [SerializeField] TextMeshProUGUI coinText;
        [SerializeField] UIMenuButton shopButton;
        [SerializeField] UIMenuButton collectionButton;
        [SerializeField] UIMenuButton collectionListButton;
        [SerializeField] UIMenuButton adButton;
        [SerializeField] UIMenuButton settingButton;
        [SerializeField] RectTransform coinImage;  // 코인 비행 연출이 도착하는 코인 아이콘
        [SerializeField] RectTransform effectCoin; // 날아가는 코인 템플릿 (복제 원본, 런타임엔 숨김)
        [SerializeField] GameObject mainMenu;

        public UIBottomBarControl Control { get; private set; } = new UIBottomBarControl();

        // 코인 획득 연출(CoinGetTimer)의 도착 지점 (UICoinTimerHud가 런타임에 참조)
        public RectTransform CoinFlyTarget => coinImage;

        public UIMenuButton CollectionButton => collectionButton;
        public UIMenuButton CollectionListButton => collectionListButton;
        public UIMenuButton ShopButton => shopButton;
        public UIMenuButton AdButton => adButton;
        public UIMenuButton SettingButton => settingButton;
        public GameObject MainMenu => mainMenu;

        // 코인 표시 연출: 최초 셋팅은 즉시, 이후 변경은 목표값까지 카운팅.
        // 기본 속도는 매 프레임 10 이지만, 증감 폭이 커도 CoinCountMaxDuration(3초) 안에는 반드시 목표값에 도달한다.
        private const long CoinCountStep = 10;            // 최소 카운팅 속도 (프레임당)
        private const float CoinCountMaxDuration = 3f;    // 증감 표시가 끝나야 하는 최대 시간(초)
        private long _displayedCoin;        // 현재 화면에 표시 중인 값
        private long _targetCoin;           // 카운팅 목표 값
        private float _coinCountDeadline;   // 현재 목표값에 도달해야 하는 시각 (Time.unscaledTime 기준)
        private bool _coinInitialized;      // 최초 셋팅 여부
        private Coroutine _coinCountCoroutine;
        private long _actualCoin;           // 실제 보유 코인 (PlayerContext 저장값)
        private bool _coinTargetDirty;      // 실제 보유량/보류량이 바뀌어 LateUpdate 에서 표시 목표를 다시 잡아야 함

        // 금액이 올라가는 동안 CoinCountSoundInterval 간격으로 효과음을 연속 재생한다. (감소 시에는 재생 안 함)
        private const string CoinCountUpSoundName = "coin_count_up";
        private const float CoinCountSoundInterval = 0.1f;
        private float _nextCoinSoundTime;   // 다음 효과음 재생 가능 시각 (Time.unscaledTime 기준)

        private void Start()
        {
            Control.Initialize(this);

            // 템플릿은 복제 원본이므로 화면에 보이지 않게 숨겨둔다
            if (effectCoin != null)
                effectCoin.gameObject.SetActive(false);

            if (coinImage != null)
                _coinTargetBaseScale = coinImage.localScale;

            // Attach는 현재 값으로 즉시 1회 콜백되므로 초기 표시도 여기서 처리된다
            PlayerContext.AttachItemAmount(PlayerContext.COIN_ITEM_KEY, OnChangedCoinAmount);
        }

        protected override void OnDestroy()
        {
            PlayerContext.DetachItemAmount(PlayerContext.COIN_ITEM_KEY, OnChangedCoinAmount);
            Control.Deinitialize();

            // 비행 중인 연출을 모두 정리한다 (지급은 이미 확정돼 있어 유실 없음 - 복제 코인 제거 + onComplete 보장)
            FinishAllCoinFlyRequests(false);
            _coinBounceTween?.Kill();

            base.OnDestroy();
        }

        private void OnChangedCoinAmount(long newAmount)
        {
            _actualCoin = newAmount;

            // 최초 셋팅은 즉시 반영
            if (!_coinInitialized)
            {
                SetCoinText(DisplayTargetCoin);
                return;
            }

            // 이후 변경은 LateUpdate 에서 반영한다. 지급 직후 같은 프레임에 표시 보류(HoldCoinDisplay)가
            // 걸리면 보류분을 뺀 값으로 카운팅하므로, 숫자가 코인 도착보다 먼저 튀어 오르지 않는다.
            _coinTargetDirty = true;
        }

        private void LateUpdate()
        {
            ExpireCoinDisplayHolds();

            if (_coinTargetDirty)
            {
                _coinTargetDirty = false;
                SetCoinText(DisplayTargetCoin);
            }
        }

        public void SetCoinText(long amount)
        {
            if (coinText == null)
                return;

            _targetCoin = amount;
            // 목표가 바뀔 때마다 마감을 다시 잡는다 - 어떤 증감 폭이든 지금부터 3초 안에 끝난다
            _coinCountDeadline = Time.unscaledTime + CoinCountMaxDuration;

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

        // 표시값을 목표값까지 증가/감소시킨다. 프레임당 최소 CoinCountStep(10) 씩 움직이되,
        // 남은 시간(마감까지) 안에 끝나도록 필요한 만큼 스텝을 키운다. (마지막 스텝은 목표값에 정확히 맞춤)
        private IEnumerator CoinCountCoroutine()
        {
            while (_displayedCoin != _targetCoin)
            {
                long diff = _targetCoin - _displayedCoin;
                long remainingAbs = System.Math.Abs(diff);

                // 이번 프레임에 필요한 양 = 남은 차이 × (이번 프레임 시간 / 마감까지 남은 시간). 마감이 지났으면 한 번에 끝낸다.
                float dt = Time.unscaledDeltaTime;
                float remainingTime = _coinCountDeadline - Time.unscaledTime;
                long needed = remainingTime <= dt
                    ? remainingAbs
                    : (long)System.Math.Ceiling(remainingAbs * (dt / remainingTime));

                long step = System.Math.Min(remainingAbs, System.Math.Max(CoinCountStep, needed)) * System.Math.Sign(diff);
                _displayedCoin += step;
                ApplyCoinText(_displayedCoin);

                if (diff > 0 && Time.unscaledTime >= _nextCoinSoundTime)
                {
                    _nextCoinSoundTime = Time.unscaledTime + CoinCountSoundInterval;
                    AudioManager.Instance.PlaySfx(CoinCountUpSoundName);
                }
                yield return null;
            }
            _coinCountCoroutine = null;
        }

        // 세 자릿수마다 콤마를 붙여 표시한다. (예: 994299 → 994,299)
        private void ApplyCoinText(long amount)
        {
            coinText.text = amount.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── 표시 보류 (이미 지급된 코인을 연출 도착 전까지 숫자에 반영하지 않음) ──
        // 지급(저장)은 즉시 끝내고, 화면 숫자만 코인이 도착할 때 올라가게 하기 위한 장치.
        // 보류는 연출 도착/중단/만료 중 어느 경로로든 반드시 풀린다 (숫자가 실제 잔액보다 낮게 고착되지 않음).
        public sealed class CoinDisplayHold
        {
            internal long remaining;    // 아직 표시에 반영하지 않은 금액
            internal float expireTime;  // 이 시각(Time.unscaledTime)이 지나면 남은 보류를 자동 해제
        }

        private const float DefaultCoinDisplayHoldSeconds = 10f; // 보류 기본 만료 시간(초) - 연출 누락 안전장치
        private readonly List<CoinDisplayHold> _coinDisplayHolds = new List<CoinDisplayHold>();
        private long _heldCoinTotal; // 보류 중인 금액 합계

        // 표시 목표 = 실제 보유량 - 아직 도착하지 않은(표시 보류 중) 지급분
        private long DisplayTargetCoin => System.Math.Max(0, _actualCoin - _heldCoinTotal);

        // grantedAmount(이미 지급·저장된 코인)를 표시에서 보류한다.
        // ⚠️ 지급과 "같은 프레임"에 호출해야 한다 - 표시 반영은 LateUpdate 라 같은 프레임이면 숫자가 튀지 않는다.
        public CoinDisplayHold HoldCoinDisplay(long grantedAmount, float maxSeconds = DefaultCoinDisplayHoldSeconds)
        {
            if (grantedAmount <= 0)
                return null;

            var hold = new CoinDisplayHold
            {
                remaining = grantedAmount,
                expireTime = Time.unscaledTime + Mathf.Max(0.1f, maxSeconds),
            };
            _coinDisplayHolds.Add(hold);
            _heldCoinTotal += grantedAmount;
            _coinTargetDirty = true;
            return hold;
        }

        // 보류 중인 금액 중 amount 만큼을 표시에 반영한다 (amount 가 남은 보류보다 크면 전부).
        public void ReleaseCoinDisplay(CoinDisplayHold hold, long amount)
        {
            if (hold == null || hold.remaining <= 0 || amount <= 0)
                return;

            amount = System.Math.Min(amount, hold.remaining);
            hold.remaining -= amount;
            _heldCoinTotal = System.Math.Max(0, _heldCoinTotal - amount);
            if (hold.remaining <= 0)
                _coinDisplayHolds.Remove(hold);
            _coinTargetDirty = true;
        }

        // 남은 보류를 전부 표시에 반영한다.
        public void ReleaseCoinDisplay(CoinDisplayHold hold)
        {
            if (hold != null)
                ReleaseCoinDisplay(hold, hold.remaining);
        }

        private void ExpireCoinDisplayHolds()
        {
            for (int i = _coinDisplayHolds.Count - 1; i >= 0; i--)
            {
                CoinDisplayHold hold = _coinDisplayHolds[i];
                if (Time.unscaledTime >= hold.expireTime)
                    ReleaseCoinDisplay(hold);
            }
        }

        // ── 코인 획득 연출 (effectCoin 템플릿을 복제해 출발점 → 코인 아이콘으로 비행) ──
        // 요청마다 독립된 시퀀스로 재생한다. 연출이 겹쳐도 앞 연출을 끊지 않으며,
        // 어떤 경로로 끝나든(도착/중단/파괴) 복제 코인 정리 + 보류 해제 + onComplete 호출이 보장된다.
        private const int CoinFlyCount = 8;              // 한 번에 날아가는 코인 개수
        private const float CoinFlyDuration = 0.6f;      // 코인 1개 비행 시간(초)
        private const float CoinFlyInterval = 0.08f;     // 코인 발사 간격(초)
        private const float CoinCurveHeightRatio = 0.4f; // 베지어 곡선 높이(비행 거리 대비 비율)
        private const float CoinBounceScale = 1.3f;      // 도착 지점이 커지는 배율(이후 원래 크기로 복귀)
        private const float CoinBounceDuration = 0.15f;  // 도착 지점 바운스 각 구간 시간(초)

        // 연출 1건(요청 단위)의 상태
        private sealed class CoinFlyRequest
        {
            public Sequence seq;
            public CoinDisplayHold hold;          // 도착 시 풀어 줄 표시 보류 (없으면 표시 전용 연출)
            public long sharePerCoin;             // 코인 1개 도착당 표시 반영량 (마지막 코인이 나머지 정산)
            public System.Action onComplete;
            public readonly List<RectTransform> coins = new List<RectTransform>();
            public bool finished;
        }

        private readonly List<CoinFlyRequest> _coinFlyRequests = new List<CoinFlyRequest>();
        private Vector3 _coinTargetBaseScale; // 도착 지점 원래 스케일(바운스 복귀 기준)
        private Tween _coinBounceTween;

        // 이미 지급(저장)된 grantedAmount 코인의 획득 연출. 지급과 같은 프레임에 호출하면
        // 숫자는 코인이 도착할 때마다 나눠 올라간다. onComplete: 마지막 코인 도착(또는 연출 중단) 후 호출.
        // ⚠️ 이 메서드는 코인을 지급하지 않는다 - 호출 전에 PlayerContext 로 지급을 끝내야 한다.
        public void PlayCoinGetEffect(RectTransform startPoint, long grantedAmount, System.Action onComplete = null)
        {
            PlayCoinGetEffect(startPoint, HoldCoinDisplay(grantedAmount), onComplete);
        }

        // 미리 걸어 둔 표시 보류(hold)를 코인 도착에 맞춰 풀어 주는 연출.
        // (지급 시점과 연출 시작 시점이 떨어져 있을 때 - 광고 보상 팝업, 랜덤박스 확인 버튼 등)
        public void PlayCoinGetEffect(RectTransform startPoint, CoinDisplayHold hold, System.Action onComplete = null)
        {
            if (startPoint == null || coinImage == null || effectCoin == null)
            {
                ReleaseCoinDisplay(hold);
                onComplete?.Invoke();
                return;
            }

            Vector3 startPos = startPoint.position;

            var request = new CoinFlyRequest
            {
                hold = hold,
                sharePerCoin = hold != null ? hold.remaining / CoinFlyCount : 0,
                onComplete = onComplete,
            };
            // 보류가 만료되지 않도록 연출 길이만큼은 보장한다
            if (hold != null)
                hold.expireTime = Mathf.Max(hold.expireTime,
                    Time.unscaledTime + CoinFlyInterval * CoinFlyCount + CoinFlyDuration + 1f);

            request.seq = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < CoinFlyCount; i++)
            {
                bool isLast = i == CoinFlyCount - 1;
                request.seq.Insert(i * CoinFlyInterval, CreateCoinFly(request, startPos, isLast));
            }
            // 완료든 Kill 이든 시퀀스가 끝나면 정리 (FinishCoinFlyRequest 는 1회만 동작)
            request.seq.OnKill(() => FinishCoinFlyRequest(request));

            _coinFlyRequests.Add(request);
        }

        // 표시 보류 없이 연출만 재생한다 (테스트용 등).
        public void PlayCoinGetEffect(RectTransform startPoint, System.Action onComplete = null)
        {
            PlayCoinGetEffect(startPoint, (CoinDisplayHold)null, onComplete);
        }

        // 코인 1개 비행 트윈 생성 (2차 베지어 곡선). isLast = 마지막으로 도착하는 코인 여부.
        private Tween CreateCoinFly(CoinFlyRequest request, Vector3 startPos, bool isLast)
        {
            RectTransform coin = Instantiate(effectCoin, effectCoin.parent);
            coin.SetAsLastSibling();
            coin.gameObject.SetActive(true);
            request.coins.Add(coin);

            Vector3 p0 = startPos;                // 시작(월드)
            Vector3 p2 = coinImage.position;      // 도착(월드)
            Vector3 mid = (p0 + p2) * 0.5f;
            float distance = Vector3.Distance(p0, p2);
            Vector3 p1 = mid + Vector3.up * (distance * CoinCurveHeightRatio); // 위로 볼록한 제어점

            coin.position = p0;

            float t = 0f;
            return DOTween.To(() => t, x =>
                {
                    t = x;
                    float u = 1f - t;
                    if (coin != null)
                        coin.position = u * u * p0 + 2f * u * t * p1 + t * t * p2; // 2차 베지어
                }, 1f, CoinFlyDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    request.coins.Remove(coin);
                    if (coin != null)
                        Destroy(coin.gameObject);
                    BounceTargetCoin();

                    // 도착한 몫만큼 표시에 반영 (마지막 코인은 나눗셈 나머지까지 정산)
                    if (request.hold != null)
                        ReleaseCoinDisplay(request.hold, isLast ? request.hold.remaining : request.sharePerCoin);

                    if (isLast)
                        FinishCoinFlyRequest(request);
                });
        }

        // 연출 1건 종료 처리: 남은 복제 코인 제거 + 남은 보류 해제 + onComplete 1회 호출.
        private void FinishCoinFlyRequest(CoinFlyRequest request)
        {
            if (request == null || request.finished)
                return;
            request.finished = true;
            _coinFlyRequests.Remove(request);

            foreach (RectTransform coin in request.coins)
            {
                if (coin != null)
                    Destroy(coin.gameObject);
            }
            request.coins.Clear();

            ReleaseCoinDisplay(request.hold);

            try
            {
                request.onComplete?.Invoke();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        // 진행 중인 모든 연출을 끝낸다. complete=true 면 즉시 도착 처리(바운스/표시 반영), false 면 중단.
        // 어느 쪽이든 각 요청의 onComplete 는 호출된다.
        private void FinishAllCoinFlyRequests(bool complete)
        {
            foreach (CoinFlyRequest request in _coinFlyRequests.ToArray())
            {
                if (request.seq != null && request.seq.IsActive())
                {
                    if (complete)
                        request.seq.Complete(true);
                    else
                        request.seq.Kill();
                }
                FinishCoinFlyRequest(request);
            }
        }

        // 도착 지점(코인 아이콘)이 커졌다가 다시 원래 크기로 돌아오는 연출
        private void BounceTargetCoin()
        {
            if (coinImage == null)
                return;

            _coinBounceTween?.Kill();
            coinImage.localScale = _coinTargetBaseScale;
            _coinBounceTween = coinImage
                .DOScale(_coinTargetBaseScale * CoinBounceScale, CoinBounceDuration)
                .SetLoops(2, LoopType.Yoyo)   // 커짐 → 원래 크기 복귀
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        // 계정 전환 직전 등: 비행 중 연출을 즉시 완료하고 모든 표시 보류를 푼다.
        // (지급은 이미 원장에 확정돼 있으므로 여기서 추가 지급은 없다)
        public void CompletePendingRewards()
        {
            FinishAllCoinFlyRequests(true);
            foreach (CoinDisplayHold hold in _coinDisplayHolds.ToArray())
                ReleaseCoinDisplay(hold);
        }
    }

    public class UIBottomBarControl
    {
        private UIBottomBar _bottomBar;
        private Crane _crane;

        public void Initialize(UIBottomBar bottomBar)
        {
            _bottomBar = bottomBar;

            if (_bottomBar.CollectionButton != null)
                _bottomBar.CollectionButton.SetButton(true, () => OnClickCollectionBtn());

            if (_bottomBar.ShopButton != null)
                _bottomBar.ShopButton.SetButton(true, () => OnClickShopBtn());

            if (_bottomBar.CollectionListButton != null)
                _bottomBar.CollectionListButton.SetButton(true, () => OnClickCollectionListBtn());

            if (_bottomBar.AdButton != null)
                _bottomBar.AdButton.SetButton(true, () => OnClickAdBtn());

            if (_bottomBar.SettingButton != null)
                _bottomBar.SettingButton.SetButton(true, () => OnClickSettingBtn());

            GameMain.SubscribeStatus(OnChangedGameStatus);

            UIManager.Instance.OnLayerOpened += OnLayerOpened;
            UIManager.Instance.OnLayerClosed += OnLayerClosed;

            // 구독 시점의 현재 상태로 즉시 1회 동기화
            SetMainMenuActive(!UIManager.Instance.IsLayerOpen(UILayer.Contents));
        }

        public void Deinitialize()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);

            if(UIManager.IsCheckInstance())
            {
                UIManager.Instance.OnLayerOpened -= OnLayerOpened;
                UIManager.Instance.OnLayerClosed -= OnLayerClosed;
            }

            UnsubscribeCrane();

            _bottomBar = null;
        }

        // Contents 레이어(도감 패널 등)가 열리면 mainMenu(하단 버튼 묶음)를 숨기고, 닫히면 다시 보여준다.
        private void OnLayerOpened(UILayer layer)
        {
            if (layer == UILayer.Contents)
                SetMainMenuActive(false);
        }

        private void OnLayerClosed(UILayer layer)
        {
            if (layer == UILayer.Contents)
                SetMainMenuActive(true);
        }

        private void SetMainMenuActive(bool isActive)
        {
            if (_bottomBar != null && _bottomBar.MainMenu != null)
                _bottomBar.MainMenu.SetActive(isActive);
        }

        // 컬렉션(도감) 진입. 진입 전 상태 복귀는 GameMain.PreviousStatus 기반으로 뒤로가기가 처리한다.
        public void OnClickCollectionBtn()
        {
            if (GameMain.Instance.CurrentStatus == GameStatus.COLLECTION)
                return;

            UIManager.Instance.CloseAllInLayer(UILayer.Contents);
            GameMain.Instance.SetGameStatus(GameStatus.COLLECTION);
        }

        // 상점 패널을 띄운다 (Contents 레이어 - 다른 Contents 패널은 닫는다)
        public void OnClickShopBtn()
        {
            UIShopPanel.OpenExclusive();
        }

        // 광고 시청 팝업(확인 → 리워드 광고 → 코인 지급)을 띄운다.
        public void OnClickAdBtn()
        {
            GameMain.Instance.ShowWatchAdForCoinsPopup();
        }

        // 설정 패널(사운드/음악 볼륨 등)을 띄운다.
        public void OnClickSettingBtn()
        {
            if (UISettingPanel.Get() != null)
                return;

            UISettingPanel.CreateOrGet();
        }

        public void OnClickCollectionListBtn()
        {
            if (UICollectionPanel.Get() != null)
                return;

            UIManager.Instance.CloseAllInLayer(UILayer.Contents);
            UICollectionPanel.CreateOrGet();
        }

        private void OnChangedGameStatus(GameStatus status)
        {
            switch (status)
            {
                case GameStatus.COLLECTION:
                    _bottomBar.CollectionListButton.gameObject.SetActive(true);
                    _bottomBar.CollectionButton.gameObject.SetActive(false);
                    break;

                default:
                    _bottomBar.CollectionListButton.gameObject.SetActive(false);
                    _bottomBar.CollectionButton.gameObject.SetActive(true);
                    break;
            }

            if (status == GameStatus.INGAME)
                SubscribeCrane();
            else
                UnsubscribeCrane();
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

        private void OnChangedCraneStatus(int craneStatus)
        {
        }
    }
}
