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

        private System.Action _onClickOk;

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
        public void Set(string titleText, string descriptionText, System.Action onClickOkButtonEvent)
        {
            if (this.titleText != null)
                this.titleText.text = titleText;

            if (this.descriptionText != null)
                this.descriptionText.text = descriptionText;

            _onClickOk = onClickOkButtonEvent;
        }

        // OK 버튼: 콜백 실행 후 팝업을 닫는다.
        private void OnClickOk()
        {
            _onClickOk?.Invoke();
            Close();
        }
    }
}
