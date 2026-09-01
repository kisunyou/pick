#if UNITY_ANDROID
using System;
using System.Threading;
using UnityEngine;

namespace FunRabbit
{
    // Android Credential Manager(androidx.credentials + googleid)로 Google ID 토큰을 받아오는 헬퍼.
    // 별도 자바 플러그인 없이 JNI(AndroidJavaObject/AndroidJavaProxy)만으로 호출한다.
    // gradle 의존성은 Assets/Editor/GoogleSignInDependencies.xml(EDM4U)이 포함시킨다.
    // Firebase 웹 플로우(SignInWithProviderAsync)가 Android에서 인증서 해시 오류로 실패해 도입 (2026-09-01).
    public static class GoogleCredentialHelper
    {
        const string GoogleIdTokenCredentialType =
            "com.google.android.libraries.identity.googleid.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL";

        // Google 계정 선택 UI를 띄우고 ID 토큰을 받아온다.
        // onResult(idToken, error): 성공 시 idToken != null / 실패 시 error에 사유.
        // 콜백은 Unity 메인 스레드에서 호출된다 (메인 스레드에서 호출할 것).
        public static void RequestIdToken(Action<string, string> onResult)
        {
            SynchronizationContext mainThread = SynchronizationContext.Current;
            Action<string, string> report = (token, error) =>
            {
                if (mainThread != null)
                    mainThread.Post(_ => onResult(token, error), null);
                else
                    onResult(token, error);
            };

            try
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");

                // google-services.xml이 생성한 default_web_client_id 리소스에서 웹 클라이언트 ID를 읽는다
                string webClientId = GetWebClientId(activity);
                if (string.IsNullOrEmpty(webClientId))
                {
                    report(null, "default_web_client_id 리소스를 찾을 수 없습니다 (google-services.json 재생성 필요)");
                    return;
                }

                var option = new AndroidJavaObject("com.google.android.libraries.identity.googleid.GetGoogleIdOption$Builder")
                    .Call<AndroidJavaObject>("setServerClientId", webClientId)
                    .Call<AndroidJavaObject>("setFilterByAuthorizedAccounts", false)
                    .Call<AndroidJavaObject>("build");

                var request = new AndroidJavaObject("androidx.credentials.GetCredentialRequest$Builder")
                    .Call<AndroidJavaObject>("addCredentialOption", option)
                    .Call<AndroidJavaObject>("build");

                var credentialManager = new AndroidJavaClass("androidx.credentials.CredentialManager")
                    .CallStatic<AndroidJavaObject>("create", activity);

                var cancellationSignal = new AndroidJavaObject("android.os.CancellationSignal");
                var executor = new AndroidJavaClass("java.util.concurrent.Executors")
                    .CallStatic<AndroidJavaObject>("newSingleThreadExecutor");
                var callback = new CredentialManagerCallbackProxy(report);

                credentialManager.Call("getCredentialAsync",
                    activity, request, cancellationSignal, executor, callback);
            }
            catch (Exception e)
            {
                report(null, e.Message);
            }
        }

        private static string GetWebClientId(AndroidJavaObject activity)
        {
            try
            {
                string packageName = activity.Call<string>("getPackageName");
                var resources = activity.Call<AndroidJavaObject>("getResources");
                int resId = resources.Call<int>("getIdentifier", "default_web_client_id", "string", packageName);
                if (resId == 0)
                    return null;

                return activity.Call<string>("getString", resId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // androidx.credentials.CredentialManagerCallback<GetCredentialResponse, GetCredentialException> 구현
        private class CredentialManagerCallbackProxy : AndroidJavaProxy
        {
            private readonly Action<string, string> _report;

            public CredentialManagerCallbackProxy(Action<string, string> report)
                : base("androidx.credentials.CredentialManagerCallback")
            {
                _report = report;
            }

            // 계정 선택 완료 - 응답에서 Google ID 토큰을 꺼낸다
            public void onResult(AndroidJavaObject response)
            {
                try
                {
                    var credential = response.Call<AndroidJavaObject>("getCredential");
                    string type = credential.Call<string>("getType");
                    if (type != GoogleIdTokenCredentialType)
                    {
                        _report(null, $"예상치 못한 credential 타입: {type}");
                        return;
                    }

                    var data = credential.Call<AndroidJavaObject>("getData");
                    var googleIdCredential = new AndroidJavaClass("com.google.android.libraries.identity.googleid.GoogleIdTokenCredential")
                        .CallStatic<AndroidJavaObject>("createFrom", data);
                    string idToken = googleIdCredential.Call<string>("getIdToken");

                    _report(idToken, null);
                }
                catch (Exception e)
                {
                    _report(null, e.Message);
                }
            }

            // 취소/실패 (사용자가 닫음, 기기에 계정 없음 등)
            public void onError(AndroidJavaObject exception)
            {
                string message;
                try { message = exception.Call<string>("getMessage"); }
                catch (Exception) { message = "알 수 없는 오류"; }

                _report(null, message);
            }
        }
    }
}
#endif
