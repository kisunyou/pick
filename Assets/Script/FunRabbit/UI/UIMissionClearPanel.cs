using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIMissionClearPanel",
        Layer = UILayer.Contents,
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
        // 코인이 날아가기 시작하는 위치. 실제 비행/도착 바운스/카운트업 연출은 상시 노출되는
        // UIBottomBar가 전담한다(도착 지점 = UIBottomBar의 coinImage) - 이 패널은 시작 위치만 제공한다.
        [SerializeField] RectTransform coinFlyImage;
        [SerializeField] int rewardCoin = 500;              // 코인 보상량

        Sequence _seq;
        Tween _titleTween;
        Vector3 _titleBaseScale = Vector3.one;

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

            // 연출 시작 시 닫기 버튼 숨김 (모든 연출이 끝난 뒤 활성화)
            if (closeButton != null)
                closeButton.gameObject.SetActive(false);

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

        // 코인 보상 연출: UIBottomBar가 소유한 공용 연출(비행/도착 바운스/분할 지급)에 위임한다.
        // 도착 지점 = UIBottomBar의 coinImage(상시 노출), 시작 지점만 이 패널의 coinFlyImage를 사용.
        // 연출이 끝나면(또는 재생 불가 시 즉시) 닫기 버튼을 활성화한다.
        void PlayCoinReward()
        {
            if (rewardCoin <= 0 || coinFlyImage == null || UIBottomBar.Instance == null)
            {
                ActivateCloseButton();
                return;
            }

            UIBottomBar.Instance.PlayCoinGetEffect(coinFlyImage, rewardCoin, ActivateCloseButton);
        }

        void ActivateCloseButton()
        {
            if (closeButton != null)
                closeButton.gameObject.SetActive(true);
        }

        protected override void OnDestroy()
        {
            _seq?.Kill();
            _titleTween?.Kill();
            base.OnDestroy();
        }
    }
}
