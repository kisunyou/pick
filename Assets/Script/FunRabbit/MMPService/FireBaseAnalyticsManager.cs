using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
using UnityEngine;

namespace FunRabbit
{
    // Firebase Analytics 매니저.
    // - Firebase 의존성 체크/초기화 + 기본 Analytics 함수(이벤트 로깅/유저 속성/화면뷰/수집 on-off)를 담당한다.
    // - GameMain.Start()에서 MakeInstance()로 깨워진다 (AudioManager/LevelPlayAds와 동일 패턴).
    public class FireBaseAnalyticsManager : Singleton<FireBaseAnalyticsManager>
    {
        public bool IsInitialized { get; private set; }

        void Start()
        {
            // Firebase SDK 내부 로그(이벤트 전송 시도 등)까지 콘솔에 출력 - 에디터 테스트용
            FirebaseApp.LogLevel = LogLevel.Debug;

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                DependencyStatus status = task.Result;
                if (status == DependencyStatus.Available)
                {
                    IsInitialized = true;
                    Debug.Log("[FireBaseAnalyticsManager] 초기화 성공");
                }
                else
                {
                    Debug.LogError($"[FireBaseAnalyticsManager] 초기화 실패: {status}");
                }
            });
        }

        // ===== 이벤트 로깅 =====

        public void LogEvent(string eventName)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogEvent: {eventName}");
            FirebaseAnalytics.LogEvent(eventName);
        }

        const string LoggedOnceKeyPrefix = "FireBaseAnalytics_LoggedOnce_";

        // 이 기기(설치)에서 한 번도 기록된 적 없는 이벤트만 기록한다 (PlayerPrefs로 영구 저장, 앱 재실행해도 다시 안 찍힘).
        public void LogEventOnce(string eventName)
        {
            string key = LoggedOnceKeyPrefix + eventName;
            if (PlayerPrefs.GetInt(key, 0) == 1)
                return;

            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();

            LogEvent(eventName);
        }

        public void LogEvent(string eventName, string parameterName, string parameterValue)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogEvent: {eventName} ({parameterName}={parameterValue})");
            FirebaseAnalytics.LogEvent(eventName, parameterName, parameterValue);
        }

        public void LogEvent(string eventName, string parameterName, double parameterValue)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogEvent: {eventName} ({parameterName}={parameterValue})");
            FirebaseAnalytics.LogEvent(eventName, parameterName, parameterValue);
        }

        public void LogEvent(string eventName, string parameterName, long parameterValue)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogEvent: {eventName} ({parameterName}={parameterValue})");
            FirebaseAnalytics.LogEvent(eventName, parameterName, parameterValue);
        }

        public void LogEvent(string eventName, params Parameter[] parameters)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogEvent: {eventName} (params={parameters.Length})");
            FirebaseAnalytics.LogEvent(eventName, parameters);
        }

        // 화면 전환 로깅 (screenClass 생략 시 screenName과 동일하게 기록)
        public void LogScreenView(string screenName, string screenClass = null)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] LogScreenView: {screenName} ({screenClass ?? screenName})");
            FirebaseAnalytics.LogEvent(
                FirebaseAnalytics.EventScreenView,
                new Parameter(FirebaseAnalytics.ParameterScreenName, screenName),
                new Parameter(FirebaseAnalytics.ParameterScreenClass, screenClass ?? screenName));
        }

        // ===== 유저 속성 =====

        public void SetUserId(string userId)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] SetUserId: {userId}");
            FirebaseAnalytics.SetUserId(userId);
        }

        public void SetUserProperty(string name, string value)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] SetUserProperty: {name}={value}");
            FirebaseAnalytics.SetUserProperty(name, value);
        }

        // ===== 수집 제어 =====

        // 수집 on/off (예: GDPR 동의 여부에 따라)
        public void SetAnalyticsCollectionEnabled(bool enabled)
        {
            if (!CheckReady())
                return;

            Debug.Log($"[FireBaseAnalyticsManager] SetAnalyticsCollectionEnabled: {enabled}");
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled);
        }

        // 이 기기의 애널리틱스 데이터(앱 인스턴스 ID 포함)를 초기화한다.
        public void ResetAnalyticsData()
        {
            if (!CheckReady())
                return;

            Debug.Log("[FireBaseAnalyticsManager] ResetAnalyticsData");
            FirebaseAnalytics.ResetAnalyticsData();
        }

        private bool CheckReady()
        {
            if (IsInitialized)
                return true;

            Debug.LogWarning("[FireBaseAnalyticsManager] 아직 초기화되지 않았습니다.");
            return false;
        }
    }
}
