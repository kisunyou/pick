using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIPopup",
        Layer = UILayer.Popup,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIPopup : BaseUIView<UIPopup>
    {
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI descriptionText;
        [SerializeField] Button okButton;
        [SerializeField] Button cancelButton;
        [SerializeField] Button closeButton;
        [SerializeField] Button dimedButton;
        [SerializeField] Image coinIcon;

        const string CoinIconPath = "UI2/Images/UI_Etc/ResourceBar_Icon_Coin";

        private System.Action _onClickOk;

        // 코인 지급 연출(UIBottomBar.PlayCoinGetEffect)의 시작 지점으로 쓰기 위한 코인 아이콘 RectTransform
        public RectTransform CoinIconTransform => coinIcon != null ? coinIcon.rectTransform : null;

        void Start()
        {
            if (okButton != null)
                okButton.onClick.AddListener(OnClickOk);

            // 딤 배경 / 닫기 / 취소 버튼은 모두 팝업을 닫는다.
            if (dimedButton != null)
                dimedButton.onClick.AddListener(Close);
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(Close);
        }

        // 팝업 내용(제목/설명)과 확인 버튼 콜백을 설정한다.
        // showCoinIcon이 true면 코인 아이콘을 표시. showButtons가 false면 닫기/OK/Cancel 버튼을 숨겨
        // 사용자 조작 없이 보여주기만 하는 연출용 팝업으로 쓸 수 있다(닫기는 호출부가 Close()로 직접 처리).
        public void Set(string titleText, string descriptionText, System.Action onClickOkButtonEvent, bool showCoinIcon = false, bool showButtons = true)
        {
            if (this.titleText != null)
                this.titleText.text = titleText;

            if (this.descriptionText != null)
                this.descriptionText.text = descriptionText;

            _onClickOk = onClickOkButtonEvent;

            SetCoinIconActive(showCoinIcon);
            SetButtonsActive(showButtons);
        }

        // 닫기/OK/Cancel 버튼 표시 여부. 딤 배경은 보여주기 연출을 위해 화면엔 계속 남기되,
        // 조작으로 조기 닫힘을 막기 위해 interactable만 함께 끈다.
        private void SetButtonsActive(bool active)
        {
            if (okButton != null)
                okButton.gameObject.SetActive(active);
            if (cancelButton != null)
                cancelButton.gameObject.SetActive(active);
            if (closeButton != null)
                closeButton.gameObject.SetActive(active);
            if (dimedButton != null)
                dimedButton.interactable = active;
        }

        // 코인 아이콘 표시. 켤 때 스프라이트가 비어있으면 로드해서 채운다.
        private void SetCoinIconActive(bool active)
        {
            if (coinIcon == null)
                return;

            if (active && coinIcon.sprite == null)
            {
                Sprite sprite = Resources.Load<Sprite>(CoinIconPath);
                if (sprite != null)
                    coinIcon.sprite = sprite;
            }

            coinIcon.gameObject.SetActive(active);
        }

        // OK 버튼: 콜백 실행 후 팝업을 닫는다.
        private void OnClickOk()
        {
            _onClickOk?.Invoke();
            Close();
        }
    }
}
