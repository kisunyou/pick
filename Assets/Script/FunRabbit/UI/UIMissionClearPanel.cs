using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIMissionClearPanel",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIMissionClearPanel : BaseUIView<UIMissionClearPanel>
    {
        [SerializeField] UIModelViewPanel uiModelViewPanel;       // 클리어한 스테이지 모델 (기존 패널, layer 9 "doll")
        [SerializeField] UIModelViewPanel uiModelViewPanelNew;    // 새 스테이지 모델 (추가 패널, 전용 레이어)
        [SerializeField] Button closeButton;
        [SerializeField] TextMeshProUGUI titleText;

        [Header("Title 텍스트")]
        [SerializeField] string clearedTitle = "Clear Mission";
        [SerializeField] string newTitle = "Try New Doll";
        [SerializeField] float titleScaleFrom = 0.5f;     // 타이틀 등장 시작 스케일 배율(원래 크기로 팝)
        [SerializeField] float titleScaleDuration = 0.4f; // 타이틀 스케일 연출 시간(초)

        [Header("연출")]
        // 새 모델 패널 전용 레이어. 클리어 모델(layer 9 "doll")과 분리해 두 인형이 서로의 카메라에 겹쳐 잡히지 않게 한다.
        [SerializeField] int newModelLayer = 10;
        [SerializeField] float showDuration = 2f;      // 클리어 모델 노출 시간(초)
        [SerializeField] float slideDuration = 0.5f;   // 슬라이드 애니메이션 시간(초)
        // 슬라이드 이동 거리. 0이면 런타임에 패널 폭(=화면 폭)으로 자동 계산해 화면 밖까지 확실히 보낸다.
        [SerializeField] float slideDistance = 0f;

        [Header("코인 보상 연출")]
        [SerializeField] RectTransform coinFlyImage;        // 날아가는 코인(비활성 템플릿, 복제해서 사용)
        [SerializeField] RectTransform targetCoinImage;     // 코인 도착 지점(커졌다 원래 크기로 복귀)
        [SerializeField] TextMeshProUGUI coinText;          // 코인 수치 텍스트
        [SerializeField] int rewardCoin = 500;              // 가변 보상량. coinPerFly 단위로 코인 개수 결정(500→5개, 1000→10개)
        [SerializeField] int coinPerFly = 100;              // 코인 1개가 더하는 값
        [SerializeField] float coinFlyDuration = 0.6f;      // 코인 1개 비행 시간(초)
        [SerializeField] float coinFlyInterval = 0.12f;     // 코인 발사 간격(초)
        [SerializeField] float coinCurveHeightRatio = 0.4f; // 베지어 곡선 높이(비행 거리 대비 비율)
        [SerializeField] float coinCountUpDuration = 0.3f;  // 코인 1개 도착 시 텍스트 카운트업 시간(초)
        [SerializeField] float coinBounceScale = 1.3f;      // 도착 지점이 커지는 배율(이후 원래 크기로 복귀)

        Sequence _seq;
        Sequence _coinSeq;
        Tween _coinCountTween;
        Tween _coinBounceTween;
        Tween _titleTween;
        Vector3 _titleBaseScale = Vector3.one;
        long _startCoin;          // 연출 시작 시점의 PlayerContext.CoinAmount.Value
        long _coinDisplay;        // 현재 coinText에 표시 중인 값
        Vector3 _targetCoinBaseScale = Vector3.one;

        void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        // clearedModelPath: 방금 클리어한 스테이지 모델 / newModelPath: 다음(새) 스테이지 모델
        // reward: 코인 보상량(-1이면 인스펙터 rewardCoin 사용). 카운트 시작값은 PlayerContext.CoinAmount.Value를 사용한다.
        public void SetData(string clearedModelPath, string newModelPath, int reward = -1)
        {
            if (reward >= 0)
                rewardCoin = reward;

            // 새 모델 패널은 별도 레이어(카메라 컬링 + 모델 레이어)로 분리해 겹침을 방지한다.
            if (uiModelViewPanelNew != null)
                uiModelViewPanelNew.SetModelLayer(newModelLayer);

            if (uiModelViewPanel != null)
                _ = uiModelViewPanel.LoadModel(clearedModelPath);
            if (uiModelViewPanelNew != null)
                _ = uiModelViewPanelNew.LoadModel(newModelPath);

            PlaySequence();
        }

        // 클리어 모델 2초 노출 → 화면 밖 왼쪽으로 퇴장 → 새 모델이 화면 밖 오른쪽에서 중앙으로 등장
        // 카메라/모델은 고정하고, 각 패널의 RawImage만 좌우로 움직인다.
        void PlaySequence()
        {
            _seq?.Kill();
            _coinSeq?.Kill();
            _coinCountTween?.Kill();

            // 연출 시작 시 닫기 버튼 숨김 (모든 연출이 끝난 뒤 활성화)
            if (closeButton != null)
                closeButton.gameObject.SetActive(false);

            // 코인 텍스트 초기값: 현재 보유 코인(PlayerContext)을 그대로 표시
            _startCoin = PlayerContext.CoinAmount.Value;
            _coinDisplay = _startCoin;
            if (coinText != null)
                coinText.text = _coinDisplay.ToString();

            // 도착 지점 원래 크기 기억 (커졌다 복귀 연출 기준)
            if (targetCoinImage != null)
                _targetCoinBaseScale = targetCoinImage.localScale;

            // 타이틀 원래 크기 기억 (스케일 팝 연출 기준)
            if (titleText != null)
                _titleBaseScale = titleText.transform.localScale;

            RectTransform clearedImg = uiModelViewPanel != null ? uiModelViewPanel.ImageRect : null;
            RectTransform newImg = uiModelViewPanelNew != null ? uiModelViewPanelNew.ImageRect : null;

            // 슬라이드 거리: 지정값이 있으면 사용, 없으면 패널 폭(=화면 폭)으로 자동 계산해 화면 밖까지 확실히 보낸다.
            RectTransform panelRoot = uiModelViewPanel != null ? uiModelViewPanel.transform as RectTransform : null;
            float dist = slideDistance > 0f
                ? slideDistance
                : (panelRoot != null && panelRoot.rect.width > 1f ? panelRoot.rect.width : 1500f);

            // 패널/카메라/모델은 고정. 초기 배치: 클리어 RawImage는 중앙(x=0), 새 RawImage는 화면 밖 오른쪽(x=+dist)
            if (uiModelViewPanel != null)
                uiModelViewPanel.gameObject.SetActive(true);
            if (uiModelViewPanelNew != null)
                uiModelViewPanelNew.gameObject.SetActive(true);

            if (clearedImg != null)
                clearedImg.anchoredPosition = new Vector2(0f, clearedImg.anchoredPosition.y);
            if (newImg != null)
                newImg.anchoredPosition = new Vector2(dist, newImg.anchoredPosition.y);

            // 클리어 타이틀 등장 (스케일 팝)
            ShowTitle(clearedTitle);

            // 일시정지(timeScale 0)에서도 동작하도록 unscaled 타임 사용
            _seq = DOTween.Sequence().SetUpdate(true);
            _seq.AppendInterval(showDuration);

            // 클리어 RawImage: 중앙 → 화면 밖 왼쪽
            if (clearedImg != null)
                _seq.Append(clearedImg.DOAnchorPosX(-dist, slideDuration).SetEase(Ease.InBack));

            // 완전히 사라진 뒤: 타이틀 교체 + 클리어 패널 비활성(불필요한 카메라 렌더 중지)
            _seq.AppendCallback(() =>
            {
                if (uiModelViewPanel != null)
                    uiModelViewPanel.gameObject.SetActive(false);
                // 새 타이틀 등장 (스케일 팝)
                ShowTitle(newTitle);
            });

            // 새 RawImage: 화면 밖 오른쪽 → 중앙
            if (newImg != null)
                _seq.Append(newImg.DOAnchorPosX(0f, slideDuration).SetEase(Ease.OutBack));

            // 새 모델 등장이 끝나면 코인 보상 연출 시작
            _seq.AppendCallback(PlayCoinReward);
        }

        // 타이틀 텍스트를 바꾸며 작은 크기에서 원래 크기로 팝(스케일) 등장시킨다.
        void ShowTitle(string text)
        {
            if (titleText == null)
                return;

            titleText.text = text;

            _titleTween?.Kill();
            Transform t = titleText.transform;
            t.localScale = _titleBaseScale * titleScaleFrom;
            _titleTween = t.DOScale(_titleBaseScale, titleScaleDuration)
                .SetEase(Ease.OutBack)   // 살짝 오버슈트 후 원래 크기로
                .SetUpdate(true);
        }

        // 코인 보상 연출: coinPerFly(=100)당 코인 1개씩 targetCoinImage로 베지어 곡선 비행 →
        // 도착할 때마다 도착 지점 젤리 바운스 + 코인 텍스트 카운트업 → 모두 끝나면 닫기 버튼 활성화
        void PlayCoinReward()
        {
            _coinSeq?.Kill();

            if (rewardCoin <= 0 || coinFlyImage == null || targetCoinImage == null)
            {
                // 코인 연출 생략 - 바로 닫기 버튼 활성화
                ActivateCloseButton();
                return;
            }

            int coinCount = Mathf.Max(1, rewardCoin / Mathf.Max(1, coinPerFly));

            _coinSeq = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < coinCount; i++)
            {
                int idx = i;
                // idx번째 코인 도착 후 누적 표시값 (마지막은 정확히 _startCoin + rewardCoin)
                long targetValue = _startCoin + (long)Mathf.RoundToInt(rewardCoin * (idx + 1f) / coinCount);
                _coinSeq.Insert(idx * coinFlyInterval, CreateCoinFly(() => OnCoinArrived(targetValue)));
            }

            // 마지막 코인 도착 + 카운트업까지 끝난 뒤: 최종 코인값 반영 + 닫기 버튼 활성화
            float lastArrival = (coinCount - 1) * coinFlyInterval + coinFlyDuration;
            _coinSeq.InsertCallback(lastArrival + coinCountUpDuration + 0.05f, OnCoinRewardComplete);
        }

        // 코인 1개 비행 트윈 생성 (2차 베지어 곡선)
        Tween CreateCoinFly(TweenCallback onArrived)
        {
            RectTransform coin = Instantiate(coinFlyImage, transform);
            coin.SetAsLastSibling();
            coin.gameObject.SetActive(true);

            Vector3 p0 = coinFlyImage.position;                 // 시작(월드)
            Vector3 p2 = targetCoinImage.position;              // 도착(월드)
            Vector3 mid = (p0 + p2) * 0.5f;
            float distance = Vector3.Distance(p0, p2);
            Vector3 p1 = mid + transform.up * (distance * coinCurveHeightRatio); // 위로 볼록한 제어점

            coin.position = p0;

            float t = 0f;
            return DOTween.To(() => t, x =>
                {
                    t = x;
                    float u = 1f - t;
                    coin.position = u * u * p0 + 2f * u * t * p1 + t * t * p2; // 2차 베지어
                }, 1f, coinFlyDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (coin != null)
                        Destroy(coin.gameObject);
                    onArrived?.Invoke();
                });
        }

        // 코인 도착 시: 도착 지점이 커졌다 복귀 + 코인 텍스트 빠른 카운트업(+1씩)
        void OnCoinArrived(long targetValue)
        {
            BounceTargetCoin();

            if (coinText != null)
            {
                _coinCountTween?.Kill();
                long from = _coinDisplay;
                _coinCountTween = DOTween.To(() => from, v =>
                    {
                        _coinDisplay = v;
                        coinText.text = _coinDisplay.ToString();
                    }, targetValue, coinCountUpDuration)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true);
            }
            else
            {
                _coinDisplay = targetValue;
            }
        }

        // 도착 지점 이미지가 커졌다가 다시 원래 크기로 돌아오는 연출
        void BounceTargetCoin()
        {
            if (targetCoinImage == null)
                return;

            _coinBounceTween?.Kill();
            targetCoinImage.localScale = _targetCoinBaseScale;
            _coinBounceTween = targetCoinImage
                .DOScale(_targetCoinBaseScale * coinBounceScale, coinCountUpDuration * 0.5f)
                .SetLoops(2, LoopType.Yoyo)   // 커짐 → 원래 크기 복귀
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        // 모든 코인 연출 종료: 최종 코인값을 정확히 표시하고 PlayerContext에 반영 후 닫기 버튼 활성화
        void OnCoinRewardComplete()
        {
            long finalValue = _startCoin + rewardCoin;

            _coinCountTween?.Kill();
            _coinDisplay = finalValue;
            if (coinText != null)
                coinText.text = finalValue.ToString();

            // 최종 코인값을 실제 데이터에 반영 (PlayerPrefs 저장 + HUD 등 옵저버 통지)
            PlayerContext.SetCoinAmount(finalValue);

            ActivateCloseButton();
        }

        void ActivateCloseButton()
        {
            if (closeButton != null)
                closeButton.gameObject.SetActive(true);
        }

        protected override void OnDestroy()
        {
            _seq?.Kill();
            _coinSeq?.Kill();
            _coinCountTween?.Kill();
            _coinBounceTween?.Kill();
            _titleTween?.Kill();
            base.OnDestroy();
        }
    }
}
