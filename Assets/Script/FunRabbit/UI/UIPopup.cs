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

        // 취소(취소/X/딤/뒤로가기/다른 내용으로 덮어씀/파괴)로 끝났을 때 1회 호출 - SetOnCancel 로 지정
        private System.Action _onCancel;

        // UIPopup 은 Single 재사용이라 여러 호출부가 같은 인스턴스를 덮어쓴다. Set 마다 전역 증가 토큰을 발급해,
        // 자동 닫기(광고 보상 2초 / "연동 중" 모달 등)는 자기가 띄운 내용일 때만 닫도록 비교에 쓴다 (CloseIfCurrent).
        static int s_lastContentToken;
        public int ContentToken { get; private set; }

        // 버튼 없는 모달(showButtons=false - 연동 중/보상 연출)인지. 뒤로가기로 닫히지 않는다.
        public bool IsButtonless { get; private set; }

        // popup 이 아직 살아 있고 token 내용을 그대로 보여주고 있을 때만 닫는다 (다른 호출부가 덮어쓴 새 내용은 유지)
        public static bool CloseIfCurrent(UIPopup popup, int token)
        {
            if (popup == null || popup.ContentToken != token)
                return false;

            popup.Close();
            return true;
        }

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
        // onClickOkButtonEvent 가 null 이면(확인해도 따로 할 동작이 없는 안내 팝업) 취소 버튼을 숨기고 확인 버튼만
        // 가운데에 표시한다 - 확인/취소가 같은 "닫기" 라 두 버튼이 무의미하기 때문.
        // 반환값 = 이번 내용의 토큰 (CloseIfCurrent 에 사용)
        public int Set(string titleText, string descriptionText, System.Action onClickOkButtonEvent, bool showCoinIcon = false, bool showButtons = true)
        {
            // 이전 내용이 응답 대기 중(취소 콜백 보유)이었다면 덮어쓰기 = 취소로 처리 (새 내용을 다 세팅한 뒤 호출)
            System.Action supersededCancel = _onCancel;
            _onCancel = null;
            ContentToken = ++s_lastContentToken;
            IsButtonless = !showButtons;

            if (this.titleText != null)
                this.titleText.text = titleText;

            if (this.descriptionText != null)
                this.descriptionText.text = descriptionText;

            _onClickOk = onClickOkButtonEvent;

            SetCoinIconActive(showCoinIcon);
            SetButtonsActive(showButtons);

            // 안내 전용 팝업(확인 콜백 없음)은 취소 버튼 생략 - 버튼 묶음은 가운데 정렬 레이아웃이라 확인 버튼이 중앙에 온다
            if (cancelButton != null && showButtons && onClickOkButtonEvent == null)
                cancelButton.gameObject.SetActive(false);

            // 팝업은 재사용(Single)되므로, 이전 사용에서 꺼둔 확인 버튼을 기본 활성으로 되돌린다.
            SetOkButtonInteractable(true);

            int token = ContentToken;
            supersededCancel?.Invoke();
            return token;
        }

        // 확인 외의 방법(취소/X/딤/뒤로가기, 다른 내용으로 덮어씀, 파괴)으로 끝났을 때의 콜백. Set 이후에 호출할 것.
        public void SetOnCancel(System.Action onCancel)
        {
            _onCancel = onCancel;
        }

        // 뒤로가기: 버튼 없는 모달은 닫지 않고 입력만 소비, 그 외는 취소와 동일하게 닫는다
        public override bool HandleBackKey()
        {
            if (!IsButtonless)
                Close();
            return true;
        }

        public override void OnClose()
        {
            base.OnClose();
            FireCancel();
        }

        protected override void OnDestroy()
        {
            // CloseAll/CloseAllInLayer 처럼 OnClose 없이 파괴된 경우에도 대기 중인 호출부가 멈추지 않게
            FireCancel();
            base.OnDestroy();
        }

        private void FireCancel()
        {
            System.Action cancel = _onCancel;
            _onCancel = null;
            _onClickOk = null;
            cancel?.Invoke();
        }

        // 확인 버튼 활성/비활성 (예: 하루 광고 시청 횟수 소진 시 비활성).
        // 비활성이어도 취소/닫기/딤 배경으로 팝업은 닫을 수 있다.
        public void SetOkButtonInteractable(bool interactable)
        {
            if (okButton != null)
                okButton.interactable = interactable;
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
                Sprite sprite = SpriteCache.Get(CoinIconPath);
                if (sprite != null)
                    coinIcon.sprite = sprite;
            }

            coinIcon.gameObject.SetActive(active);
        }

        // OK 버튼: 콜백 실행 후 팝업을 닫는다.
        // 콜백이 같은 팝업에 다음 단계 내용을 다시 Set 했다면(2단계 확인 등) 그 새 내용은 닫지 않는다.
        private void OnClickOk()
        {
            int token = ContentToken;
            System.Action ok = _onClickOk;
            _onClickOk = null;
            _onCancel = null;   // 확인으로 끝났으므로 취소 콜백은 버린다
            ok?.Invoke();

            if (this != null && ContentToken == token)
                Close();
        }
    }
}
