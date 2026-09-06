using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 연속 뽑기 성공 콤보 HUD + 콤보 상태 관리.
    // - 인형(랜덤박스 포함)을 뽑을 때마다 콤보 +1 (한 플레이에 2개 들어오면 2콤보),
    //   빈손 플레이면 다음 플레이 시작 시 리셋
    // - 2콤보부터 표시: 콤보 수에 맞는 이미지(combo_Double/Triple/Quadra/Penta) + "n Combo" 텍스트
    // - 연출: 매 콤보마다 화면 밖 오른쪽 → 가운데로 스프링(OutElastic) 진입, 도착 시 텍스트 스케일 1.0 → 1.5 → 1.0 펀치
    // - 아군 추가 소환: 3콤보 +1 / 4콤보 +2 / 5콤보 이상 +3 (Basket이 AllyBonusCount를 더한다)
    public class UIComboHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI comboText;
        [SerializeField] GameObject comboDoubleImage;   // 2콤보
        [SerializeField] GameObject comboTripleImage;   // 3콤보
        [SerializeField] GameObject comboQuadraImage;   // 4콤보
        [SerializeField] GameObject comboPentaImage;    // 5콤보 이상
        [SerializeField] CanvasGroup canvasGroup;

        // 콤보 표기 다국어 키 ("{0} 콤보" / "{0} Combo" 형식 - stringData.json)
        const string ComboTextKey = "combo_text";

        [Header("연출")]
        [SerializeField] float flyInDuration = 0.6f;     // 화면 밖 → 가운데 진입 시간
        [SerializeField] float springAmplitude = 1.1f;   // 스프링(OutElastic) 진폭 - 클수록 크게 튕긴다
        [SerializeField] float springPeriod = 0.55f;     // 스프링 주기 - 작을수록 잘게 떨린다
        [SerializeField] float punchUpDuration = 0.15f;  // 텍스트 스케일 1.0 → 1.5
        [SerializeField] float punchDownDuration = 0.2f; // 텍스트 스케일 1.5 → 1.0
        [SerializeField] float holdDuration = 1.2f;      // 연출 후 표시 유지 시간
        [SerializeField] float fadeOutDuration = 0.25f;  // 사라지는 시간

        RectTransform _rect;
        Vector2 _basePosition;                 // 프리팹에 배치된 원래 위치 (진입 도착 지점)
        Vector3 _textBaseScale = Vector3.one;

        int _combo;
        bool _collectedThisPlay;   // 이번 플레이에서 인형을 뽑았는지 (빈손 플레이 판정용)
        bool _playStarted;         // 첫 플레이 전에는 빈손 판정을 하지 않는다
        bool _isShown;             // 표시 중 여부
        bool _craneSubscribed;     // 크레인 상태 구독 완료 여부 (Start 시점에 크레인이 없으면 지연 구독)

        Sequence _seq;

        // 현재 콤보의 아군 추가 소환 수 (3콤보 +1 / 4콤보 +2 / 5콤보 이상 +3)
        public int AllyBonusCount =>
            _combo >= 5 ? 3 :
            _combo == 4 ? 2 :
            _combo == 3 ? 1 : 0;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _basePosition = _rect.anchoredPosition;

            if (comboText != null)
                _textBaseScale = comboText.transform.localScale;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            SetImagesActive(0);
        }

        void Start()
        {
            EnsureCraneSubscription();
        }

        // 플레이 시작(CONTROL_MOVING) 감지로 "빈손 플레이 = 콤보 리셋"을 판정하기 위한 구독.
        // UIHud 생성 시점에 크레인이 아직 없을 수 있어, 성공할 때까지 지연 재시도한다.
        void EnsureCraneSubscription()
        {
            if (_craneSubscribed)
                return;

            if (Crane.TryGetSetInstance(out Crane crane) && crane != null)
            {
                crane.OnChangedStatus -= OnChangedCraneStatus;
                crane.OnChangedStatus += OnChangedCraneStatus;
                _craneSubscribed = true;
            }
        }

        void OnDestroy()
        {
            _seq?.Kill();

            if (Crane.TryGetSetInstance(out Crane crane) && crane != null)
                crane.OnChangedStatus -= OnChangedCraneStatus;
        }

        // 인형이 바구니에 들어왔을 때(Basket) 호출 - 인형 1개마다 콤보 +1.
        // 한 플레이에 2개가 들어오면 그대로 2콤보가 된다. 랜덤박스도 뽑기 성공이므로
        // 콤보가 이어진다 (아군 배수는 동물 인형에만 적용).
        public void OnDollCollected()
        {
            EnsureCraneSubscription();

            _collectedThisPlay = true;
            _combo++;

            if (_combo >= 2)
                Show(_combo);
        }

        void OnChangedCraneStatus(int craneStatus)
        {
            if (craneStatus != CraneStatus.CONTROL_MOVING)
                return;

            // 새 플레이 시작 시점에 직전 플레이를 판정한다 - 빈손이었으면 콤보 리셋.
            // (READY 복귀 시점 판정은 인형이 아직 바구니로 떨어지는 중일 수 있어 오판한다)
            if (_playStarted && !_collectedThisPlay)
                ResetCombo();

            _playStarted = true;
            _collectedThisPlay = false;
        }

        public void ResetCombo()
        {
            _combo = 0;
            Hide();
        }

        // combo 수에 맞는 이미지/텍스트로 표시 연출을 재생한다 (2 이상에서 호출)
        public void Show(int combo)
        {
            SetImagesActive(combo);

            if (comboText != null)
            {
                comboText.gameObject.SetActive(true);
                comboText.text = LanguageManager.Instance.Get(ComboTextKey, combo);
            }

            _seq?.Kill();

            if (canvasGroup != null)
                canvasGroup.alpha = 1f;

            _seq = DOTween.Sequence().SetUpdate(true);

            // 매 콤보마다 화면 밖 오른쪽에서 가운데(원래 위치)로 스프링 느낌으로 진입한다.
            // (3콤보 → 4콤보가 연속으로 이어져도 그때마다 다시 날아온다)
            _isShown = true;
            _rect.anchoredPosition = new Vector2(_basePosition.x + GetOffscreenOffset(), _basePosition.y);
            _seq.Append(_rect.DOAnchorPosX(_basePosition.x, flyInDuration)
                .SetEase(Ease.OutElastic, springAmplitude, springPeriod));

            // 가운데 도착 시 텍스트 스케일 펀치: 1.0 → 1.5 → 1.0
            if (comboText != null)
            {
                Transform textTransform = comboText.transform;
                textTransform.localScale = _textBaseScale;
                _seq.Append(textTransform.DOScale(_textBaseScale * 1.5f, punchUpDuration).SetEase(Ease.OutQuad));
                _seq.Append(textTransform.DOScale(_textBaseScale, punchDownDuration).SetEase(Ease.OutBack));
            }

            // 잠시 유지 후 서서히 사라진다 (콤보가 이어지면 Show가 다시 불리며 시퀀스가 갱신된다)
            _seq.AppendInterval(holdDuration);
            if (canvasGroup != null)
                _seq.Append(canvasGroup.DOFade(0f, fadeOutDuration));
            _seq.AppendCallback(HideImmediate);
        }

        // 화면 밖 오른쪽까지의 X 오프셋 (부모 폭의 절반 + 자기 폭 = 확실히 화면 밖)
        float GetOffscreenOffset()
        {
            RectTransform parentRect = transform.parent as RectTransform;
            float parentWidth = parentRect != null && parentRect.rect.width > 1f ? parentRect.rect.width : 1080f;
            return parentWidth * 0.5f + _rect.rect.width;
        }

        // 콤보 리셋 시 호출 - 표시 중이면 짧게 페이드 아웃, 아니면 즉시 정리
        void Hide()
        {
            _seq?.Kill();

            if (_isShown && canvasGroup != null && canvasGroup.alpha > 0f)
            {
                _seq = DOTween.Sequence().SetUpdate(true);
                _seq.Append(canvasGroup.DOFade(0f, fadeOutDuration));
                _seq.AppendCallback(HideImmediate);
            }
            else
            {
                HideImmediate();
            }
        }

        void HideImmediate()
        {
            _isShown = false;

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            SetImagesActive(0);

            if (comboText != null)
                comboText.gameObject.SetActive(false);
        }

        // combo 수에 해당하는 이미지 하나만 켜고 나머지는 끈다 (0 = 전부 끔)
        void SetImagesActive(int combo)
        {
            if (comboDoubleImage != null) comboDoubleImage.SetActive(combo == 2);
            if (comboTripleImage != null) comboTripleImage.SetActive(combo == 3);
            if (comboQuadraImage != null) comboQuadraImage.SetActive(combo == 4);
            if (comboPentaImage != null) comboPentaImage.SetActive(combo >= 5);
        }
    }
}
