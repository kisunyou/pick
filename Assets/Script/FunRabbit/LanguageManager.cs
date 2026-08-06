using System;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public enum LanguageType
    {
        KOR,
        ENG,
        JPN,
        THA,
    }

    [Serializable]
    public class StringRow
    {
        public string key;
        public string kor;
        public string eng;
        public string jpn;
        public string tha;
    }

    [Serializable]
    public class StringDataList
    {
        public List<StringRow> stringData;
    }

    // 다국어 문자열 매니저. Table/stringData.json을 로드해 key로 조회한다.
    // 앱 최초 실행 시 저장된 언어가 없으면 OS 시스템 언어를 기본값으로 사용한다.
    public class LanguageManager : Singleton<LanguageManager>
    {
        private const string PLAYER_PREFS_KEY = "LanguageType";
        private const string STRING_DATA_PATH = "Table/stringData";

        private readonly Dictionary<string, StringRow> _table = new Dictionary<string, StringRow>();
        private bool _isInit;

        public LanguageType CurrentLanguage { get; private set; } = LanguageType.ENG;

        // 언어가 바뀔 때 UI가 텍스트를 다시 그릴 수 있도록 알리는 이벤트 (LocalizedText가 구독)
        public event Action OnLanguageChanged;

        protected override void Awake()
        {
            base.Awake();
            Init();
        }

        // 문자열 테이블 로드 + 저장된 언어(없으면 시스템 언어)로 초기화. 중복 호출해도 한 번만 실행된다.
        public void Init()
        {
            if (_isInit)
                return;
            _isInit = true;

            LoadStringData();

            LanguageType initialLanguage = LoadSavedLanguage() ?? SystemLanguageToLanguageType(Application.systemLanguage);
            SetLanguage(initialLanguage, save: false);
        }

        private void LoadStringData()
        {
            TextAsset json = Resources.Load<TextAsset>(STRING_DATA_PATH);
            if (json == null)
            {
                Debug.LogError("[LanguageManager] stringData.json not found in Resources/Table.");
                return;
            }

            StringDataList dataList = JsonUtility.FromJson<StringDataList>(json.text);
            if (dataList?.stringData == null)
            {
                Debug.LogError("[LanguageManager] stringData.json parse failed.");
                return;
            }

            _table.Clear();
            foreach (StringRow row in dataList.stringData)
                _table[row.key] = row;

            Debug.Log($"[LanguageManager] Loaded {_table.Count} string rows.");
        }

        // PlayerPrefs에 저장된 언어. 저장된 적 없으면 null.
        private static LanguageType? LoadSavedLanguage()
        {
            if (!PlayerPrefs.HasKey(PLAYER_PREFS_KEY))
                return null;

            string saved = PlayerPrefs.GetString(PLAYER_PREFS_KEY);
            return Enum.TryParse(saved, out LanguageType parsed) ? parsed : (LanguageType?)null;
        }

        // Unity의 OS 시스템 언어 → 게임이 지원하는 LanguageType. 지원하지 않는 언어는 영어로 폴백.
        private static LanguageType SystemLanguageToLanguageType(SystemLanguage systemLanguage)
        {
            switch (systemLanguage)
            {
                case SystemLanguage.Korean: return LanguageType.KOR;
                case SystemLanguage.Japanese: return LanguageType.JPN;
                case SystemLanguage.Thai: return LanguageType.THA;
                default: return LanguageType.ENG;
            }
        }

        // 언어 변경 (설정 메뉴 등에서 사용자가 직접 바꿀 때 save=true로 호출)
        public void SetLanguage(LanguageType language, bool save = true)
        {
            CurrentLanguage = language;
            if (save)
                PlayerPrefs.SetString(PLAYER_PREFS_KEY, language.ToString());

            OnLanguageChanged?.Invoke();
        }

        // key에 해당하는 현재 언어 문자열. 테이블에 없으면 "[key]"를 반환(누락 표시),
        // 현재 언어 값이 비어있으면 영어로 폴백한다.
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            if (!_table.TryGetValue(key, out StringRow row))
                return $"[{key}]";

            string value = CurrentLanguage switch
            {
                LanguageType.KOR => row.kor,
                LanguageType.JPN => row.jpn,
                LanguageType.THA => row.tha,
                _ => row.eng,
            };

            return string.IsNullOrEmpty(value) ? row.eng : value;
        }

        // string.Format 파라미터를 적용한 조회 (예: "코인 {0}개를 받았습니다!")
        public string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
