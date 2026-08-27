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

        [Header("Title 텍스트 (LanguageManager 키)")]
        [SerializeField] string clearedTitleKey = "mission_clear_title";
        [SerializeField] string newTitleKey = "mission_new_title";
        [SerializeField] string allClearTitleKey = "mission_all_clear_title"; // 마지막 스테이지 클리어(올클리어) 타이틀
        [SerializeField] float titleScaleFrom = 0.5f;     // 타이틀 등장 시작 스케일 배율(원래 크기로 팝)
        [SerializeField] float titleScaleDuration = 0.4f; // 타이틀 스케일 연출 시간(초)

        [Header("연출")]
        // 새 모델 패널 전용 레이어. 클리어 모델(layer 9 "doll")과 분리해 두 인형이 서로의 카메라에 겹쳐 잡히지 않게 한다.
        [SerializeField] int newModelLayer = 10;
        [SerializeField] float showDuration = 2f;      // 클리어 모델 노출 시간(초)
        // 클리어 모델 노출 중, 보스 인형이 일반 인형으로 "변신"하는 시점(초). showDuration보다 작아야 한다.
        [SerializeField] float bossToNormalDelay = 1f;
        [SerializeField] float slideDuration = 0.5f;   // 슬라이드 애니메이션 시간(초)
        // 슬라이드 이동 거리. 0이면 런타임에 패널 폭(=화면 폭)으로 자동 계산해 화면 밖까지 확실히 보낸다.
        [SerializeField] float slideDistance = 0f;

        [Header("변신 깜빡임 연출 (보스 → 일반 인형)")]
        [SerializeField] int flickerCount = 4;           // 깜빡이는 횟수
        [SerializeField] float flickerInterval = 0.08f;  // 깜빡임 한 번(꺼짐 또는 켜짐)의 시간(초)
        [SerializeField] float revealPunchScale = 0.15f; // 일반 인형이 드러날 때의 스케일 펀치 크기

        [Header("코인 보상 연출")]
        // 코인이 날아가기 시작하는 위치. 실제 비행/도착 바운스/카운트업 연출은 상시 노출되는
        // UIBottomBar가 전담한다(도착 지점 = UIBottomBar의 coinImage) - 이 패널은 시작 위치만 제공한다.
        [SerializeField] RectTransform coinFlyImage;
        [SerializeField] int rewardCoin = 500;              // 코인 보상량

        Sequence _seq;
        Tween _titleTween;
        Sequence _flickerSeq;
        Vector3 _titleBaseScale = Vector3.one;
        string _clearedAnimalKey; // bossToNormalDelay 시점에 "변신"시킬 일반 인형을 로드하기 위해 기억해둔다.
        bool _isAllClear;         // newAnimalKey 없음 = 마지막 스테이지 클리어. 다음 보스 등장 대신 ALL CLEAR 타이틀을 띄운다.

        void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        // clearedAnimalKey: 방금 클리어한 스테이지의 animalKey / newAnimalKey: 다음(새) 스테이지의 animalKey.
        // 두 쪽 다 처음엔 보스 인형을 보여준다 - 클리어 쪽은 bossToNormalDelay 후 일반 인형으로 "변신"한다.
        // newAnimalKey가 null/빈 값이면 올클리어 연출 - 변신까지는 같고, 다음 보스 등장 없이 ALL CLEAR 타이틀로 마무리한다.
        // reward: 코인 보상량(-1이면 인스펙터 rewardCoin 사용). 카운트 시작값은 PlayerContext.GetItemAmount(PlayerContext.COIN_ITEM_KEY)를 사용한다.
        //
        // 필요한 모델 3종(클리어 보스/클리어 일반/새 보스)을 전부 동기 로드로 미리 준비해둔 뒤에
        // 연출을 시작한다 - 비동기 로드가 아직 안 끝난 상태로 연출이 먼저 시작돼 모델이 뒤늦게
        // 나타나 보이는 문제를 없애기 위함이다.
        public void SetData(string clearedAnimalKey, string newAnimalKey, int reward = -1)
        {
            if (reward >= 0)
                rewardCoin = reward;

            _clearedAnimalKey = clearedAnimalKey;
            _isAllClear = string.IsNullOrEmpty(newAnimalKey);

            // 새 모델 패널은 별도 레이어(카메라 컬링 + 모델 레이어)로 분리해 겹침을 방지한다.
            if (uiModelViewPanelNew != null)
                uiModelViewPanelNew.SetModelLayer(newModelLayer);

            // 변신 시점(TransformClearedModelToNormal)에 쓸 일반 인형은 지금 화면엔 안 띄우고
            // 에셋만 미리 캐시에 데워둔다 (그 순간 LoadModelImmediate가 지연 없이 인스턴스화하도록).
            if (!string.IsNullOrEmpty(clearedAnimalKey))
                UIModelViewPanelControl.Preload(GameCommon.GetModelPrefabFullPath(clearedAnimalKey));

            if (uiModelViewPanel != null && !string.IsNullOrEmpty(clearedAnimalKey))
                uiModelViewPanel.LoadModelImmediate(GameCommon.GetBossModelPrefabFullPath(clearedAnimalKey));
            if (uiModelViewPanelNew != null && !string.IsNullOrEmpty(newAnimalKey))
                uiModelViewPanelNew.LoadModelImmediate(GameCommon.GetBossModelPrefabFullPath(newAnimalKey));

            PlaySequence();
        }

        // 보스 인형 → 일반 인형 "변신". 화면이 빠르게 flickerCount번 깜빡이다 완전히 꺼진 순간
        // 실제 모델을 교체(LoadModelImmediate가 기존 보스 인형을 파괴하고 일반 인형으로 바꿔준다)하고,
        // 다시 나타날 때 스케일 펀치를 곁들여 "짠!" 하고 바뀐 느낌을 강조한다.
        void TransformClearedModelToNormal()
        {
            if (uiModelViewPanel == null || string.IsNullOrEmpty(_clearedAnimalKey))
                return;

            RectTransform imgRect = uiModelViewPanel.ImageRect;
            Graphic graphic = imgRect != null ? imgRect.GetComponent<Graphic>() : null;
            if (graphic == null)
            {
                uiModelViewPanel.LoadModelImmediate(GameCommon.GetModelPrefabFullPath(_clearedAnimalKey));
                return;
            }

            _flickerSeq?.Kill();
            _flickerSeq = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < flickerCount; i++)
            {
                bool isLast = i == flickerCount - 1;

                _flickerSeq.Append(graphic.DOFade(0f, flickerInterval * 0.5f));

                if (isLast)
                {
                    // 완전히 꺼진(가장 안 보이는) 순간 모델을 교체한다. SetData에서 이미 Preload로
                    // 캐시를 데워둬서 LoadModelImmediate가 지연 없이 바로 인스턴스화한다.
                    _flickerSeq.AppendCallback(() => uiModelViewPanel.LoadModelImmediate(GameCommon.GetModelPrefabFullPath(_clearedAnimalKey)));
                    _flickerSeq.Append(graphic.DOFade(1f, flickerInterval * 0.5f));
                    _flickerSeq.Join(imgRect.DOPunchScale(Vector3.one * revealPunchScale, flickerInterval * 4f, 6, 0.8f));
                }
                else
                {
                    _flickerSeq.Append(graphic.DOFade(1f, flickerInterval * 0.5f));
                }
            }
        }

        // 클리어 모델 2초 노출 → 화면 밖 왼쪽으로 퇴장 → 새 모델이 화면 밖 오른쪽에서 중앙으로 등장
        // 카메라/모델은 고정하고, 각 패널의 RawImage만 좌우로 움직인다.
        void PlaySequence()
        {
            _seq?.Kill();
            _flickerSeq?.Kill();

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
                uiModelViewPanelNew.gameObject.SetActive(!_isAllClear); // 올클리어면 새 보스 패널은 쓰지 않는다

            if (clearedImg != null)
                clearedImg.anchoredPosition = new Vector2(0f, clearedImg.anchoredPosition.y);
            if (newImg != null)
                newImg.anchoredPosition = new Vector2(dist, newImg.anchoredPosition.y);

            // 클리어 타이틀 등장 (스케일 팝)
            ShowTitle(LanguageManager.Instance.Get(clearedTitleKey));

            // 일시정지(timeScale 0)에서도 동작하도록 unscaled 타임 사용
            _seq = DOTween.Sequence().SetUpdate(true);

            // showDuration 노출 중 bossToNormalDelay 시점에 보스 인형 → 일반 인형 "변신"이 끼어든다.
            float toNormalDelay = Mathf.Clamp(bossToNormalDelay, 0f, showDuration);
            _seq.AppendInterval(toNormalDelay);
            _seq.AppendCallback(TransformClearedModelToNormal);
            _seq.AppendInterval(showDuration - toNormalDelay);

            if (_isAllClear)
            {
                // 올클리어: 일반 인형으로 변신한 클리어 모델을 중앙에 그대로 둔 채,
                // 다음 보스 등장 대신 ALL CLEAR 타이틀(다국어)을 띄우고 코인 보상으로 마무리한다.
                _seq.AppendCallback(() => ShowTitle(LanguageManager.Instance.Get(allClearTitleKey)));
                _seq.AppendCallback(PlayCoinReward);
                return;
            }

            // 클리어 RawImage: 중앙 → 화면 밖 왼쪽
            if (clearedImg != null)
                _seq.Append(clearedImg.DOAnchorPosX(-dist, slideDuration).SetEase(Ease.InBack));

            // 완전히 사라진 뒤: 타이틀 교체 + 클리어 패널 비활성(불필요한 카메라 렌더 중지)
            _seq.AppendCallback(() =>
            {
                if (uiModelViewPanel != null)
                    uiModelViewPanel.gameObject.SetActive(false);
                // 새 타이틀 등장 (스케일 팝)
                ShowTitle(LanguageManager.Instance.Get(newTitleKey));
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
            _flickerSeq?.Kill();
            base.OnDestroy();
        }
    }
}
