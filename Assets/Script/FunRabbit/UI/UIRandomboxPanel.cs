using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIRandomboxPanel",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIRandomboxPanel : BaseUIView<UIRandomboxPanel>
    {
        [SerializeField] Button openButton;
        [SerializeField] Animator doll_random_open;
        [SerializeField] Button closeButton;

        void Start()
        {
            // "doll_random_open" 애니메이션이 기본 상태로 자동 재생되므로,
            // 시작 시 speed=0으로 첫 프레임에서 멈춰 둔다. (openButton 클릭 시 재생)
            if (doll_random_open != null)
                doll_random_open.speed = 0f;

            if (openButton != null)
                openButton.onClick.AddListener(OnClickOpen);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        // 랜덤박스 열기 버튼: 멈춰 있던 오픈 애니메이션을 재생한다. (보상 처리 등은 추후 구현)
        private void OnClickOpen()
        {
            // TODO: 랜덤박스 보상 처리
            if (doll_random_open != null)
                doll_random_open.speed = 1f;
        }
    }
}
