using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIRewardPopup",
        Layer = UILayer.Popup,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIRewardPopup : BaseUIView<UIRewardPopup>
    {
        [SerializeField] Transform popup;              // "짜잔" 스케일 인 대상 (Popup 오브젝트)
        [SerializeField] Image iconImage;
        [SerializeField] TextMeshProUGUI iconTitleText;
        [SerializeField] TextMeshProUGUI iconDescText;
        [SerializeField] Button okButton;

        [Header("등장 연출")]
        [SerializeField] float showScaleFrom = 0.5f;   // 등장 시작 스케일 배율
        [SerializeField] float showDuration = 0.22f;   // 스케일 인 시간(초) - 짧고 스냅감 있게
        [SerializeField] float showOvershoot = 4f;     // OutBack 오버슈트(클수록 스프링처럼 더 튕김)

        private System.Action _onOk;
        private Tween _showTween;

        // 팝업이 닫힐 때(파괴 직전) 호출되는 콜백. (외부에서 상태 복구 등에 사용)
        public System.Action OnClosed { get; set; }

        // 코인 지급 연출(UIBottomBar.PlayCoinGetEffect)의 시작 지점으로 쓰기 위한 아이콘 RectTransform
        public RectTransform IconTransform => iconImage != null ? iconImage.rectTransform : null;

        void Start()
        {
            if (okButton != null)
                okButton.onClick.AddListener(OnClickOk);

            PlayShowAnimation();
        }

        // 아이콘 이미지 / 타이틀 / 설명 / 확인 콜백 설정
        public void Set(Sprite iconImageSprite, string iconTitleName, System.Action onOkButtonEvent, string iconDescription = null)
        {
            if (iconImage != null)
            {
                iconImage.sprite = iconImageSprite;
                iconImage.SetNativeSize();
            }

            if (iconTitleText != null)
                iconTitleText.text = iconTitleName;

            if (iconDescText != null)
                iconDescText.text = iconDescription;

            _onOk = onOkButtonEvent;
        }

        // Popup 오브젝트가 작게 시작해 살짝 오버슈트하며 원래 크기로 등장 ("짜잔")
        private void PlayShowAnimation()
        {
            if (popup == null)
                return;

            _showTween?.Kill();
            Vector3 baseScale = popup.localScale;       // 원래(목표) 스케일
            popup.localScale = baseScale * showScaleFrom;
            _showTween = popup
                .DOScale(baseScale, showDuration)
                .SetEase(Ease.OutBack, showOvershoot);  // 스프링처럼 튕겨 등장 (짜잔)
        }

        // OK 버튼: 콜백 실행 후 팝업을 닫는다.
        private void OnClickOk()
        {
            _onOk?.Invoke();
            Close();
        }

        // UIManager.Close가 파괴 직전 호출 → 닫힘 콜백 전달
        public override void OnClose()
        {
            OnClosed?.Invoke();
        }

        protected override void OnDestroy()
        {
            _showTween?.Kill();
            base.OnDestroy();
        }
    }
}
