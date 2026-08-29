using UnityEditor;
using UnityEngine;

namespace FunRabbit.EditorTools
{
    // 테스트용 언어 전환 메뉴 (에디터 전용). LanguageManager 와 같은 PlayerPrefs 키("LanguageType", enum 이름 문자열)를 쓴다.
    // - 편집 모드: 저장값만 바꿈 → 다음 플레이부터 적용
    // - 플레이 모드: LanguageManager.SetLanguage 로 즉시 전환(LocalizedText / 상점 텍스트 등 구독자 갱신) + 저장
    // - "시스템 언어 따르기": 저장값 삭제 → 다음 실행 시 OS 언어 감지(비지원 언어는 영어)
    // 현재 선택된 언어는 메뉴에 체크 표시된다. LanguageManager.useTestLanguage 가 켜져 있으면 그 강제값이 우선한다.
    public static class DebugLanguageMenu
    {
        const string PrefsKey = "LanguageType";   // LanguageManager.PLAYER_PREFS_KEY 와 동일
        const string MenuRoot = "FunRabbit/Debug/언어/";

        [MenuItem(MenuRoot + "한국어 (KOR)", false, 100)] static void SetKor() => Set(LanguageType.KOR);
        [MenuItem(MenuRoot + "English (ENG)", false, 101)] static void SetEng() => Set(LanguageType.ENG);
        [MenuItem(MenuRoot + "日本語 (JPN)", false, 102)] static void SetJpn() => Set(LanguageType.JPN);
        [MenuItem(MenuRoot + "ไทย (THA)", false, 103)] static void SetTha() => Set(LanguageType.THA);

        [MenuItem(MenuRoot + "시스템 언어 따르기 (저장값 삭제)", false, 200)]
        static void UseSystemLanguage()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();

            if (Application.isPlaying && LanguageManager.IsCheckInstance())
                LanguageManager.Instance.SetLanguage(SystemLanguageToLanguageType(Application.systemLanguage), save: false);

            Debug.Log($"[DebugLanguage] 저장된 언어 삭제 - 다음 실행부터 시스템 언어({Application.systemLanguage}) 사용");
        }

        [MenuItem(MenuRoot + "현재 언어 로그", false, 300)]
        static void LogCurrent()
        {
            string saved = PlayerPrefs.GetString(PrefsKey, "(없음 → 시스템 언어)");
            string live = Application.isPlaying && LanguageManager.IsCheckInstance()
                ? LanguageManager.Instance.CurrentLanguage.ToString() : "(플레이 중 아님)";
            Debug.Log($"[DebugLanguage] 저장값={saved}, 현재 적용={live}, useTestLanguage={LanguageManager.useTestLanguage}({LanguageManager.testLanguage})");
        }

        // ── 체크 표시 (Validate) ──
        [MenuItem(MenuRoot + "한국어 (KOR)", true)] static bool ValidateKor() => Check(LanguageType.KOR);
        [MenuItem(MenuRoot + "English (ENG)", true)] static bool ValidateEng() => Check(LanguageType.ENG);
        [MenuItem(MenuRoot + "日本語 (JPN)", true)] static bool ValidateJpn() => Check(LanguageType.JPN);
        [MenuItem(MenuRoot + "ไทย (THA)", true)] static bool ValidateTha() => Check(LanguageType.THA);

        static bool Check(LanguageType language)
        {
            string menuPath = MenuRoot + MenuLabel(language);
            Menu.SetChecked(menuPath, GetSelected() == language);
            return true;
        }

        static string MenuLabel(LanguageType language)
        {
            switch (language)
            {
                case LanguageType.KOR: return "한국어 (KOR)";
                case LanguageType.ENG: return "English (ENG)";
                case LanguageType.JPN: return "日本語 (JPN)";
                case LanguageType.THA: return "ไทย (THA)";
                default: return language.ToString();
            }
        }

        static void Set(LanguageType language)
        {
            if (LanguageManager.useTestLanguage)
                Debug.LogWarning($"[DebugLanguage] LanguageManager.useTestLanguage=true 라 강제 언어({LanguageManager.testLanguage})가 우선합니다.");

            if (Application.isPlaying && LanguageManager.IsCheckInstance())
            {
                LanguageManager.Instance.SetLanguage(language, save: true);   // 즉시 전환 + 저장
                Debug.Log($"[DebugLanguage] 즉시 전환 + 저장: {language}");
            }
            else
            {
                PlayerPrefs.SetString(PrefsKey, language.ToString());
                PlayerPrefs.Save();
                Debug.Log($"[DebugLanguage] 저장: {language} (다음 플레이부터 적용)");
            }
        }

        // 체크 표시 기준: 플레이 중이면 실제 적용 언어, 아니면 저장값(없으면 null)
        static LanguageType? GetSelected()
        {
            if (Application.isPlaying && LanguageManager.IsCheckInstance())
                return LanguageManager.Instance.CurrentLanguage;

            string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
            return System.Enum.TryParse(saved, out LanguageType parsed) ? parsed : (LanguageType?)null;
        }

        // LanguageManager.SystemLanguageToLanguageType 과 동일한 매핑 (private 이라 복제)
        static LanguageType SystemLanguageToLanguageType(SystemLanguage systemLanguage)
        {
            switch (systemLanguage)
            {
                case SystemLanguage.Korean: return LanguageType.KOR;
                case SystemLanguage.Japanese: return LanguageType.JPN;
                case SystemLanguage.Thai: return LanguageType.THA;
                default: return LanguageType.ENG;
            }
        }
    }
}
