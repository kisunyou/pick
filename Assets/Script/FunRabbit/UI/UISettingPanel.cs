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
