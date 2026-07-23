using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UICollectionDetailPopup",
        Layer = UILayer.Contents,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UICollectionDetailPopup : BaseUIView<UICollectionDetailPopup>
    {
        [SerializeField] Button closeButton;
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] UIModelViewPanel uIModelViewPanel;

        void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        // 타이틀 설정 + 모델 비동기 로드
        public void SetData(string title, string modelFullPath)
        {
            if (titleText != null)
                titleText.text = title;

            if (uIModelViewPanel != null)
                _ = uIModelViewPanel.LoadModel(modelFullPath);
        }
    }
}
