using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 화면 하단 공통 메뉴 바 (UIHud의 Bottom을 별도 프리팹으로 분리한 뷰).
    // - 코인 보유량 표시(10단위 카운팅 연출 포함)
    // - 컬렉션(도감) 진입 버튼
    // - 코인 획득 연출(출발점 → 코인 아이콘으로 코인이 날아와 도착할 때마다 분할 지급)
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
        public GameObject MainMenu => mainMenu;

        // 코인 표시 연출: 최초 셋팅은 즉시, 이후 변경은 매 프레임 10씩 목표값까지 카운팅.
        private const long CoinCountStep = 10;
        private long _displayedCoin;        // 현재 화면에 표시 중인 값
        private long _targetCoin;           // 카운팅 목표 값
        private bool _coinInitialized;      // 최초 셋팅 여부
        private Coroutine _coinCountCoroutine;

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

            FlushPendingCoinReward(); // 비행 중이던 보상 잔액 유실 방지
            _coinSeq?.Kill();
            _coinBounceTween?.Kill();

            base.OnDestroy();
        }

        private void OnChangedCoinAmount(long newAmount)
        {
            SetCoinText(newAmount);
        }

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

        // ── 코인 획득 연출 (effectCoin 템플릿을 복제해 출발점 → 코인 아이콘으로 비행) ──
        private const int CoinFlyCount = 8;              // 한 번에 날아가는 코인 개수
        private const float CoinFlyDuration = 0.6f;      // 코인 1개 비행 시간(초)
        private const float CoinFlyInterval = 0.08f;     // 코인 발사 간격(초)
        private const float CoinCurveHeightRatio = 0.4f; // 베지어 곡선 높이(비행 거리 대비 비율)
        private const float CoinBounceScale = 1.3f;      // 도착 지점이 커지는 배율(이후 원래 크기로 복귀)
        private const float CoinBounceDuration = 0.15f;  // 도착 지점 바운스 각 구간 시간(초)

        private Vector3 _coinTargetBaseScale; // 도착 지점 원래 스케일(바운스 복귀 기준)
        private Sequence _coinSeq;
        private Tween _coinBounceTween;

        // 도착 지급: 보상은 즉시 지급하지 않고 날아간 코인이 도착할 때마다 나눠 지급한다.
        private long _pendingCoinReward;   // 아직 지급되지 않은(비행 중) 잔액
        private long _rewardSharePerCoin;  // 코인 1개 도착당 지급량 (마지막 코인이 나머지 정산)

        // 연출과 함께 rewardAmount 코인을 "도착한 코인마다" 나눠 지급한다.
        // (연출을 재생할 수 없으면 전액 즉시 지급으로 폴백). onComplete: 마지막 코인 도착 후 호출(선택).
        public void PlayCoinGetEffect(RectTransform startPoint, long rewardAmount, System.Action onComplete = null)
        {
            // 이전 비행에서 아직 지급되지 않은 잔액이 있으면 먼저 정산한다 (유실 방지)
            FlushPendingCoinReward();

            if (startPoint == null || coinImage == null || effectCoin == null)
            {
                PlayerContext.AddCoinAmount(rewardAmount);
                onComplete?.Invoke();
                return;
            }

            _pendingCoinReward = rewardAmount;
            _rewardSharePerCoin = rewardAmount / CoinFlyCount;
            PlayCoinGetEffect(startPoint, onComplete);
        }

        // 코인 획득 연출만 재생한다: 코인 여러 개가 출발점에서 코인 아이콘으로
        // 베지어 곡선을 그리며 날아가고, 도착할 때마다 아이콘이 젤리처럼 바운스한다.
        // onComplete: 마지막 코인이 도착한 직후 호출(선택, 재생 불가 시 즉시 호출).
        public void PlayCoinGetEffect(RectTransform startPoint, System.Action onComplete = null)
        {
            if (startPoint == null || coinImage == null || effectCoin == null)
            {
                onComplete?.Invoke();
                return;
            }

            Vector3 startPos = startPoint.position;

            _coinSeq?.Kill();
            _coinSeq = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < CoinFlyCount; i++)
            {
                bool isLast = i == CoinFlyCount - 1;
                _coinSeq.Insert(i * CoinFlyInterval, CreateCoinFly(startPos, isLast, isLast ? onComplete : null));
            }
        }

        // 코인 1개 비행 트윈 생성 (2차 베지어 곡선). isLast = 마지막으로 도착하는 코인 여부.
        private Tween CreateCoinFly(Vector3 startPos, bool isLast, System.Action onLastArrived)
        {
            RectTransform coin = Instantiate(effectCoin, effectCoin.parent);
            coin.SetAsLastSibling();
            coin.gameObject.SetActive(true);

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
                    coin.position = u * u * p0 + 2f * u * t * p1 + t * t * p2; // 2차 베지어
                }, 1f, CoinFlyDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (coin != null)
                        Destroy(coin.gameObject);
                    BounceTargetCoin();

                    // 도착한 몫만큼 지급 (마지막 코인은 나눗셈 나머지까지 정산)
                    GrantCoinShare(isLast ? _pendingCoinReward : _rewardSharePerCoin);

                    if (isLast)
                        onLastArrived?.Invoke();
                });
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

        // 도착한 코인 몫만큼 지급하고 잔액에서 차감한다.
        private void GrantCoinShare(long share)
        {
            if (_pendingCoinReward <= 0)
                return;

            share = System.Math.Min(share, _pendingCoinReward);
            if (share <= 0)
                return;

            _pendingCoinReward -= share;
            PlayerContext.AddCoinAmount(share);
        }

        // 비행 중 잔액을 전액 즉시 지급한다. (연출 중단/파괴 시에도 보상이 유실되지 않도록)
        private void FlushPendingCoinReward()
        {
            if (_pendingCoinReward <= 0)
                return;

            PlayerContext.AddCoinAmount(_pendingCoinReward);
            _pendingCoinReward = 0;
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

        public void OnClickShopBtn()
        {
        }

        // 광고 시청 팝업(확인 → 리워드 광고 → 코인 지급)을 띄운다.
        public void OnClickAdBtn()
        {
            GameMain.Instance.ShowWatchAdForCoinsPopup();
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
