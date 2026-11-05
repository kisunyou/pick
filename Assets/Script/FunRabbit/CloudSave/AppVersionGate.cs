using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace FunRabbit
{
    // 강제 업데이트 판정 (2026-11-05, 1.0.6 / versionCode 16 부터 탑재)
    // Firestore config/app 문서의 min_version_code 보다 이 앱의 빌드 번호(versionCode)가 낮으면 업데이트를 강제한다.
    // - 로그인 전에 읽으므로 인증 없이 읽는다 (보안 규칙: match /config/{doc} { allow read: if true; })
    // - 읽기 실패·시간 초과·문서/필드 없음은 통과 (서버 장애로 모두가 게임을 못 하는 사고 방지)
    // - 운영: 새 버전이 스토어에 100% 출시된 뒤 콘솔에서 min_version_code 를 그 빌드 번호로 올린다
    public static class AppVersionGate
    {
        public enum Status { NotStarted, Checking, Pass, UpdateRequired }

        const string ConfigUrl = "https://firestore.googleapis.com/v1/projects/pick-ddf42/databases/(default)/documents/config/app";

        // Firestore 요청 한도(초) - 넘으면 통과
        const int RequestTimeoutSeconds = 5;

        public static Status State { get; private set; } = Status.NotStarted;
        public static int MinVersionCode { get; private set; }
        public static int CurrentVersionCode { get; private set; } = -1;

        public static bool IsDone => State == Status.Pass || State == Status.UpdateRequired;
        public static bool IsUpdateRequired => State == Status.UpdateRequired;

#if UNITY_EDITOR
        // 에디터 테스트용 빌드 번호 대체값 (-1 = PlayerSettings 값 사용). 플레이 진입 도메인 리로드에도 남도록 SessionState 에 둔다
        public const string EditorVersionCodeOverrideKey = "pick.AppVersionGate.VersionCodeOverride";
        public static int EditorVersionCodeOverride
        {
            get => UnityEditor.SessionState.GetInt(EditorVersionCodeOverrideKey, -1);
            set => UnityEditor.SessionState.SetInt(EditorVersionCodeOverrideKey, value);
        }

        // 에디터 테스트용 판정 초기화 (도메인 리로드 없이 플레이를 반복할 때)
        public static void ResetForTest()
        {
            State = Status.NotStarted;
            MinVersionCode = 0;
            CurrentVersionCode = -1;
        }
#endif

        // 판정한다. 이미 진행 중이거나 끝났으면 결과가 나올 때까지만 기다린다 (여러 곳에서 불러도 요청은 1회)
        public static IEnumerator Check()
        {
            if (State != Status.NotStarted)
            {
                while (!IsDone)
                    yield return null;
                yield break;
            }

            State = Status.Checking;
            CurrentVersionCode = GetCurrentVersionCode();
            if (CurrentVersionCode < 0)
            {
                Pass("빌드 번호를 알 수 없음 (PC/iOS 등은 검사하지 않음)");
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(ConfigUrl))
            {
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Pass($"설정 읽기 실패 ({request.responseCode} {request.error})");
                    yield break;
                }

                MinVersionCode = ParseMinVersionCode(request.downloadHandler.text);
            }

            if (MinVersionCode > CurrentVersionCode)
            {
                State = Status.UpdateRequired;
                Debug.LogWarning($"[AppVersionGate] 업데이트 필요: 현재 {CurrentVersionCode} < 최소 {MinVersionCode}");
            }
            else
            {
                Pass($"현재 {CurrentVersionCode} >= 최소 {MinVersionCode}");
            }
        }

        // 스토어의 이 앱 페이지를 연다 (Android = Play 스토어 앱, 그 외 = 웹)
        public static void OpenStore()
        {
            string packageName = Application.identifier;
#if UNITY_ANDROID && !UNITY_EDITOR
            Application.OpenURL("market://details?id=" + packageName);
#else
            Application.OpenURL("https://play.google.com/store/apps/details?id=" + packageName);
#endif
        }

        static void Pass(string reason)
        {
            State = Status.Pass;
            Debug.Log("[AppVersionGate] 통과: " + reason);
        }

        // 빌드 번호 (Android versionCode). 알 수 없으면 -1
        static int GetCurrentVersionCode()
        {
#if UNITY_EDITOR
            return EditorVersionCodeOverride >= 0 ? EditorVersionCodeOverride : UnityEditor.PlayerSettings.Android.bundleVersionCode;
#elif UNITY_ANDROID
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (AndroidJavaObject packageInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
                    return packageInfo.Get<int>("versionCode");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AppVersionGate] versionCode 조회 실패: " + e.Message);
                return -1;
            }
#else
            return -1;
#endif
        }

        // {"fields":{"min_version_code":{"integerValue":"16"}}} - 필드가 없거나 숫자가 아니면 0 (= 통과)
        static int ParseMinVersionCode(string json)
        {
            try
            {
                ConfigDoc doc = JsonUtility.FromJson<ConfigDoc>(json);
                string value = doc?.fields?.min_version_code?.integerValue;
                return int.TryParse(value, out int code) ? code : 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AppVersionGate] 설정 해석 실패: " + e.Message);
                return 0;
            }
        }

        [Serializable] class ConfigDoc { public ConfigFields fields; }
        [Serializable] class ConfigFields { public FsInt min_version_code; }
        [Serializable] class FsInt { public string integerValue; }
    }
}
