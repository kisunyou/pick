using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UISettingPanel",
        Layer = UILayer.Popup,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UISettingPanel : BaseUIView<UISettingPanel>
    {
        [SerializeField] Slider soundFxSlider;
        [SerializeField] Slider musicSlider;
        [SerializeField] Toggle vibrationToggle;
        [SerializeField] Button closeButton;
        [SerializeField] Button dimedButton;
        [SerializeField] Button googleLoginButton;
        [SerializeField] Button privacyPolicyButton;
        // 계정 삭제 버튼. 프리팹에 배선되지 않았으면 개인정보 처리방침 링크를 복제해 같은 줄 오른쪽에 만든다.
        [SerializeField] Button deleteAccountButton;

        // 개인정보 처리방침 페이지 (스토어 등록 URL 과 동일)
        private const string PrivacyPolicyUrl = "https://funrabbit-games.github.io/privacy/";

        // 런타임 생성 시 링크 한 줄 배치: [개인정보 처리방침] [계정 삭제] (각 폭 460, 중심 간격 480 - 패널 폭 약 1027 안쪽)
        private const float LinkRowHalfGap = 240f;
        private const float LinkWidth = 460f;
        private static readonly Color DeleteLinkColor = new Color(1f, 0.62f, 0.62f, 1f);

        private TMP_Text _deleteAccountLabel;   // 런타임 생성 라벨 (LocalizedText 대신 직접 다국어 갱신)

        void Start()
        {
            // 저장된 볼륨으로 슬라이더 초기화 (SetValueWithoutNotify로 초기화 시 콜백 발동 방지)
            if (soundFxSlider != null)
            {
                soundFxSlider.SetValueWithoutNotify(AudioManager.Instance.SfxVolume);
                soundFxSlider.onValueChanged.AddListener(OnSoundFxSliderChanged);
            }

            if (musicSlider != null)
            {
                musicSlider.SetValueWithoutNotify(AudioManager.Instance.BgmVolume);
                musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
            }

            // 저장된 진동 설정으로 토글 초기화
            if (vibrationToggle != null)
            {
                vibrationToggle.SetIsOnWithoutNotify(VibrationManager.Enabled);
                vibrationToggle.onValueChanged.AddListener(OnVibrationToggleChanged);
            }

            // 닫기 / 딤 배경 버튼은 모두 패널을 닫는다.
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (dimedButton != null)
                dimedButton.onClick.AddListener(Close);

            // 구글 로그인 전환 버튼: 게스트 상태에서만 누를 수 있다.
            // 이미 구글 로그인 상태(또는 미로그인)면 비활성(interactable=false)으로 표시만 한다.
            if (googleLoginButton != null)
            {
                bool isGuest = FireBaseAuthManager.IsCheckInstance() && FireBaseAuthManager.Instance.IsAnonymousUser;
                googleLoginButton.interactable = isGuest;
                googleLoginButton.onClick.AddListener(OnClickGoogleLogin);
            }

            // 계정 삭제 버튼이 프리팹에 없으면 개인정보 처리방침 링크 옆에 만든다 (링크 위치 조정 포함)
            if (deleteAccountButton == null)
                deleteAccountButton = CreateDeleteAccountButton();

            // 개인정보 처리방침: 외부 브라우저로 연다.
            if (privacyPolicyButton != null)
                privacyPolicyButton.onClick.AddListener(() => Application.OpenURL(PrivacyPolicyUrl));

            // 계정 삭제 (게스트/구글 모두 가능)
            if (deleteAccountButton != null)
                deleteAccountButton.onClick.AddListener(OnClickDeleteAccount);
        }

        protected override void OnDestroy()
        {
            if (_deleteAccountLabel != null && LanguageManager.IsCheckInstance())
                LanguageManager.Instance.OnLanguageChanged -= RefreshDeleteAccountLabel;
            base.OnDestroy();
        }

        // 개인정보 처리방침 링크(밑줄 텍스트 버튼)를 복제해 같은 줄 오른쪽에 "계정 삭제" 링크를 만든다
        private Button CreateDeleteAccountButton()
        {
            if (privacyPolicyButton == null)
                return null;

            RectTransform privacyRect = privacyPolicyButton.transform as RectTransform;
            GameObject go = Instantiate(privacyPolicyButton.gameObject, privacyRect.parent);
            go.name = "deleteAccountButton";
            RectTransform rect = go.transform as RectTransform;

            float baseX = privacyRect.anchoredPosition.x;
            privacyRect.anchoredPosition = new Vector2(baseX - LinkRowHalfGap, privacyRect.anchoredPosition.y);
            privacyRect.sizeDelta = new Vector2(LinkWidth, privacyRect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(baseX + LinkRowHalfGap, privacyRect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(LinkWidth, privacyRect.sizeDelta.y);

            // 복제된 라벨의 LocalizedText(개인정보 처리방침 키)는 제거하고 직접 문자열을 넣는다
            foreach (LocalizedText localized in go.GetComponentsInChildren<LocalizedText>(true))
                DestroyImmediate(localized);

            // 긴 번역(태국어 등)이 폭을 넘지 않도록 두 라벨 모두 자동 크기(최대 = 기존 크기)를 건다
            FitLinkLabel(privacyPolicyButton.GetComponentInChildren<TMP_Text>(true));
            _deleteAccountLabel = go.GetComponentInChildren<TMP_Text>(true);
            if (_deleteAccountLabel != null)
            {
                FitLinkLabel(_deleteAccountLabel);
                _deleteAccountLabel.color = DeleteLinkColor;
                RefreshDeleteAccountLabel();
                LanguageManager.Instance.OnLanguageChanged += RefreshDeleteAccountLabel;
            }

            return go.GetComponent<Button>();
        }

        private static void FitLinkLabel(TMP_Text label)
        {
            if (label == null)
                return;

            float size = label.fontSize;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Min(24f, size);
        }

        private void RefreshDeleteAccountLabel()
        {
            if (_deleteAccountLabel != null)
                _deleteAccountLabel.text = LanguageManager.Instance.Get("settings_delete_account");
        }

        // 계정 삭제 1단계: 되돌릴 수 없다는 경고 → 2단계: 최종 확인 → CloudSaveManager 가 삭제 + 재시작
        // (UIPopup 확인 콜백 안에서 같은 팝업에 다음 내용을 Set 하면 닫히지 않고 이어진다)
        private void OnClickDeleteAccount()
        {
            if (!CloudSaveManager.Instance.CanDeleteAccount)
            {
                UITopMessage.ShowMessage(LanguageManager.Instance.Get("message_account_busy"));
                return;
            }

            UIPopup popup = UIPopup.CreateOrGet();
            if (popup == null)
                return;

            popup.Set(
                LanguageManager.Instance.Get("popup_delete_account_title"),
                LanguageManager.Instance.Get("popup_delete_account_body"),
                () => popup.Set(
                    LanguageManager.Instance.Get("popup_delete_account_confirm_title"),
                    LanguageManager.Instance.Get("popup_delete_account_confirm_body"),
                    StartDeleteAccount));
        }

        private static void StartDeleteAccount()
        {
            CloudSaveManager.Instance.DeleteAccountAndRestart(() =>
                UITopMessage.ShowMessage(LanguageManager.Instance.Get("message_delete_account_fail")));
        }

        // 게스트 → 구글 계정 전환 (성공 시 버튼 비활성 + 토스트 / 실패 시 재시도 가능)
        private void OnClickGoogleLogin()
        {
            googleLoginButton.interactable = false;

            FireBaseAuthManager.Instance.UpgradeGuestToGoogle(success =>
            {
                if (this == null || googleLoginButton == null)
                    return;

                if (success)
                {
                    UITopMessage.ShowMessage(LanguageManager.Instance.Get("login_message_google"));
                    // 전환 완료 - 이미 구글 상태이므로 비활성 유지
                }
                else
                {
                    googleLoginButton.interactable = true;
                }
            });
        }

        private void OnSoundFxSliderChanged(float value)
        {
            AudioManager.Instance.SfxVolume = value;
        }

        private void OnMusicSliderChanged(float value)
        {
            AudioManager.Instance.BgmVolume = value;
        }

        private void OnVibrationToggleChanged(bool isOn)
        {
            VibrationManager.Enabled = isOn;

            // 켤 때 짧게 1회 울려 진동 세기를 바로 체감할 수 있게 한다
            if (isOn)
                VibrationManager.Play();
        }
    }
}
