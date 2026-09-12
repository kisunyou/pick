using System;
using System.Collections;
using Firebase;
using Firebase.Analytics;
using UnityEngine;

namespace FunRabbit
{
    public class FireBaseAnalyticsManager : Singleton<FireBaseAnalyticsManager>
    {
        const string LoggedOnceKeyPrefix = "FireBaseAnalytics_LoggedOnce_";
        AnalyticsEventQueue _events;
        readonly System.Collections.Generic.Queue<Action> _settings = new System.Collections.Generic.Queue<Action>();
        public bool IsInitialized { get; private set; }

        AnalyticsEventQueue Events => _events ?? (_events = new AnalyticsEventQueue(
            name => PlayerPrefs.GetInt(LoggedOnceKeyPrefix + name, 0) == 1,
            name => { PlayerPrefs.SetInt(LoggedOnceKeyPrefix + name, 1); PlayerPrefs.Save(); },
            (name, parameters) => FirebaseAnalytics.LogEvent(name, parameters)));

        IEnumerator Start()
        {
            FirebaseApp.LogLevel = Debug.isDebugBuild ? LogLevel.Debug : LogLevel.Warning;
            while (!IsInitialized)
            {
                var task = FirebaseApp.CheckAndFixDependenciesAsync();
                while (!task.IsCompleted) yield return null;
                if (!task.IsCanceled && !task.IsFaulted && task.Result == DependencyStatus.Available)
                {
                    IsInitialized = true;
                    while (_settings.Count > 0) _settings.Dequeue().Invoke();
                    Flush();
                    yield break;
                }
                Debug.LogWarning("[Analytics] Initialization failed; retrying in 10 seconds.");
                yield return new WaitForSecondsRealtime(10f);
            }
        }

        void OnApplicationPause(bool paused) { if (paused) Flush(); }

        void Flush()
        {
            if (!IsInitialized) return;
            try { Events.Flush(); }
            catch (Exception e) { Debug.LogWarning("[Analytics] Queued event retained: " + e.Message); }
        }

        public void LogEvent(string name) => LogEvent(name, Array.Empty<Parameter>());
        public void LogEvent(string name, string parameter, string value) => LogEvent(name, new Parameter(parameter, value));
        public void LogEvent(string name, string parameter, double value) => LogEvent(name, new Parameter(parameter, value));
        public void LogEvent(string name, string parameter, long value) => LogEvent(name, new Parameter(parameter, value));
        public void LogEvent(string name, params Parameter[] parameters)
        {
            Events.Enqueue(name, parameters);
            Flush();
        }
        public void LogEventOnce(string name) => LogEventOnce(name, Array.Empty<Parameter>());
        public void LogEventOnce(string name, params Parameter[] parameters)
        {
            Events.Enqueue(name, parameters, true);
            Flush();
        }
        public void LogScreenView(string screenName, string screenClass = null) =>
            LogEvent(FirebaseAnalytics.EventScreenView, new Parameter(FirebaseAnalytics.ParameterScreenName, screenName),
                new Parameter(FirebaseAnalytics.ParameterScreenClass, screenClass ?? screenName));

        // The deduplication key remains local; only name and approved parameters reach Firebase.
        public void LogEventOnceForKey(string name, string localOnceKey, params Parameter[] parameters)
        {
            Events.Enqueue(name, parameters, true, localOnceKey);
            Flush();
        }


        void ApplySetting(Action action)
        {
            if (IsInitialized) action();
            else if (_settings.Count < 32) _settings.Enqueue(action);
        }
        public void SetUserId(string userId) => ApplySetting(() => FirebaseAnalytics.SetUserId(userId));
        public void SetUserProperty(string name, string value) => ApplySetting(() => FirebaseAnalytics.SetUserProperty(name, value));
        public void SetAnalyticsCollectionEnabled(bool enabled) => ApplySetting(() => FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled));
        public void ResetAnalyticsData() => ApplySetting(FirebaseAnalytics.ResetAnalyticsData);
    }
}
