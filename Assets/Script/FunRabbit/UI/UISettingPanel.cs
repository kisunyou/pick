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
