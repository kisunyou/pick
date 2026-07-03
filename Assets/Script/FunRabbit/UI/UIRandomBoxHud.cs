using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 랜덤박스 관련 HUD 컴포넌트. 열기 버튼 클릭 시 UIRandomboxPanel을 연다.
    public class UIRandomBoxHud : MonoBehaviour
    {
        [SerializeField] Button openRandomBoxPanelButton;

        private void Start()
        {
            if (openRandomBoxPanelButton != null)
                openRandomBoxPanelButton.onClick.AddListener(OnClickOpenRandomBoxPanel);
        }

        // 랜덤박스 패널 열기
        private void OnClickOpenRandomBoxPanel()
        {
            UIRandomboxPanel.CreateOrGet();
        }
    }
}
