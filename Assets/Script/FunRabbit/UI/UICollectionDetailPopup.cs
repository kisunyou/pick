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

        // 타이틀 설정 + 모델 비동기 로드.
        // 미클리어(미획득) 여부는 호출부가 모델 경로로 표현한다(미클리어 = 보스 프리팹 경로 전달) -
        // 여기서는 검은 실루엣 처리 없이 항상 원색으로 보여준다.
        public void SetData(string title, string modelFullPath)
        {
            if (titleText != null)
                titleText.text = title;

            if (uIModelViewPanel != null)
            {
                uIModelViewPanel.SetImageColor(Color.white);
                _ = uIModelViewPanel.LoadModel(modelFullPath);
            }
        }
    }
}
