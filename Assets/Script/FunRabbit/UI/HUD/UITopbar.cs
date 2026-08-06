using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 화면 상단 바 (UIHud 프리팹 하위 uiTopbar에 부착).
    // 뒤로가기 버튼 입력과 타이틀 표시를 담당하고, 로직은 UITopbarControl이 처리한다.
    public class UITopbar : MonoBehaviour
    {
        [SerializeField] GameObject topbarBack;
        [SerializeField] TextMeshProUGUI topbarTitle;
        [SerializeField] Button backButton;

        public UITopbarControl Control { get; private set; } = new UITopbarControl();

        private void Start()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => Control.OnClickBackButton());

            Control.Initialize(this);

            // 구독 즉시 현재 상태가 1회 반영된다
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        private void OnDestroy()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            Control.Deinitialize();
        }

        // 게임 상태 변경 시 상단 바 갱신
        private void OnChangedGameStatus(GameStatus status)
        {
            topbarBack.SetActive(false);
            switch (status)
            {
                case GameStatus.LOBBY:
                    break;

                case GameStatus.INGAME:
                    break;

                case GameStatus.COLLECTION:
                    topbarBack.SetActive(true);
                    topbarTitle.text = LanguageManager.Instance.Get("topbar_collection_title");
                    break;
            }
        }

        // ===== View (UITopbarControl이 호출) =====

        public void SetTitle(string title)
        {
            if (topbarTitle != null)
                topbarTitle.text = title;
        }

        public void SetActiveBackground(bool isActive)
        {
            if (topbarBack != null && topbarBack.activeSelf != isActive)
                topbarBack.SetActive(isActive);
        }
    }

    // 상단 바 로직 (뒤로가기 상태 전환)
    public class UITopbarControl
    {
        private UITopbar _topbar;

        public void Initialize(UITopbar topbar)
        {
            _topbar = topbar;
        }

        public void Deinitialize()
        {
            _topbar = null;
        }

        // 뒤로가기: 컬렉션 화면이면 진입 전 상태로, 인게임 상태면 로비로,
        // 로비면 종료 확인 팝업을 띄운다.
        public void OnClickBackButton()
        {
            if (GameMain.Instance.CurrentStatus == GameStatus.COLLECTION)
                GameMain.Instance.SetGameStatus(GameMain.Instance.PreviousStatus);
            else if (GameMain.Instance.CurrentStatus == GameStatus.INGAME)
                GameMain.Instance.SetGameStatus(GameStatus.LOBBY);
            else if (GameMain.Instance.CurrentStatus == GameStatus.LOBBY)
                UIPopup.CreateOrGet().Set("Exit", "Are you sure you want to quit?", () =>
                {
#if !UNITY_EDITOR
                    Application.Quit();
#endif
                });
        }
    }
}
