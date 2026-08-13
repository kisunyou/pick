using UnityEngine;

namespace FunRabbit
{
    // 진동(햅틱) 설정과 재생을 담당하는 정적 유틸리티.
    // 설정은 PlayerPrefs에 저장되고, 실제 진동은 모바일 기기에서만 울린다.
    public static class VibrationManager
    {
        const string VibrationPrefsKey = "VibrationEnabled";

        static bool? _enabled;

        // 진동 사용 여부 (기본 on). 변경 즉시 PlayerPrefs에 저장된다.
        public static bool Enabled
        {
            get
            {
                if (!_enabled.HasValue)
                    _enabled = PlayerPrefs.GetInt(VibrationPrefsKey, 1) == 1;
                return _enabled.Value;
            }
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(VibrationPrefsKey, value ? 1 : 0);
            }
        }

        // 진동 1회 재생. 설정이 꺼져 있거나 모바일 기기가 아니면 아무것도 하지 않는다.
        public static void Play()
        {
            if (!Enabled)
                return;

#if UNITY_ANDROID || UNITY_IOS
            if (Application.isMobilePlatform)
                Handheld.Vibrate();
#endif
        }
    }
}
