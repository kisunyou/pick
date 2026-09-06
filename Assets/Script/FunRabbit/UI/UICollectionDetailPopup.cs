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
        // animalKey를 주면 로드 완료 후 그 actor.json 행의 texture(_g/_r 변형색)를 적용한다.
        public void SetData(string title, string modelFullPath, string animalKey = null)
        {
            if (titleText != null)
                titleText.text = title;

            if (uIModelViewPanel != null)
            {
                uIModelViewPanel.SetImageColor(Color.white);
                _ = LoadModelWithTexture(modelFullPath, animalKey);
            }
        }

        // 모델 비동기 로드가 끝난 뒤 변형 텍스처를 덧입힌다 (원본 등급은 texture 필드가 없어 no-op).
        async Awaitable LoadModelWithTexture(string modelFullPath, string animalKey)
        {
            await uIModelViewPanel.LoadModel(modelFullPath);

            // 로드 중 팝업이 닫혔으면(파괴) 모델도 함께 정리된 상태 - 아무것도 하지 않는다
            if (!string.IsNullOrEmpty(animalKey)
                && uIModelViewPanel != null && uIModelViewPanel.ModelInstance != null)
                GameCommon.ApplyDataTexture(uIModelViewPanel.ModelInstance, animalKey);
        }
    }
}
