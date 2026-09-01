using System;
using System.Collections;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;

namespace FunRabbit
{
    // Firebase Auth 매니저.
    // - 게스트(익명) 로그인 / Google 로그인(모바일: FederatedOAuthProvider 웹 플로우)을 담당한다.
    // - GameMain.Start()에서 MakeInstance()로 깨워진다 (FireBaseAnalyticsManager와 동일 패턴).
    // - Firebase가 로그인 상태를 기기에 영구 저장하므로 앱 재실행 시 CurrentUser가 자동 복원된다.
    //   → 게스트/Google 어느 쪽이든 한 번 로그인하면 다음 실행부터 IsLoggedIn = true.
    public class FireBaseAuthManager : Singleton<FireBaseAuthManager>
    {
        public bool IsInitialized { get; private set; }

        // 초기화 시도가 끝났는지 (성공/실패 무관 - 로딩 UI가 무한 대기하지 않도록 별도 노출)
        public bool IsInitializeDone { get; private set; }

        // 게스트/Google 어느 쪽이든 로그인돼 있으면 true
        public bool IsLoggedIn => _auth != null && _auth.CurrentUser != null;

        // 현재 로그인 유저가 게스트(익명)인지 (미로그인이면 false)
        public bool IsAnonymousUser => IsLoggedIn && _auth.CurrentUser.IsAnonymous;

        // 현재 로그인 유저의 uid (미로그인이면 null) - 클라우드 세이브 문서 키로 사용
        public string UserId => IsLoggedIn ? _auth.CurrentUser.UserId : null;

        // 이 기기에서 로그인에 성공한 적이 있는지.
        // 저장된 유저 복원은 비동기(특히 에디터/PC)라, 이력이 있으면 복원을 잠시 기다리는 판단에 쓴다.
        public const string HasEverLoggedInKey = "FireBaseAuth_HasEverLoggedIn";
        public bool HasEverLoggedIn => PlayerPrefs.GetInt(HasEverLoggedInKey, 0) == 1;

        FirebaseAuth _auth;
        bool _isSigningIn;

        // Firebase 의존성 체크(FireBaseAnalyticsManager 담당) 완료 대기 한도(초)
        const float DependencyWaitTimeout = 15f;

        void Start()
        {
            StartCoroutine(InitializeCoroutine());
        }

        // ⚠️ FirebaseApp.CheckAndFixDependenciesAsync()를 여기서 직접 부르지 않는다.
        // FireBaseAnalyticsManager가 이미 같은 체크를 수행하는데, 이 함수를 같은 프레임에 두 번
        // 호출하면 두 번째 Task의 콜백이 영영 오지 않는다 (실기기/에뮬레이터 공통 재현 -
        // Auth 초기화 무한 대기 → 로그인 버튼 미노출의 원인이었음).
        // 먼저 깨어난 FireBaseAnalyticsManager의 체크 결과를 기다렸다가 Auth만 초기화한다.
        private IEnumerator InitializeCoroutine()
        {
            float deadline = Time.realtimeSinceStartup + DependencyWaitTimeout;
            bool dependencyReady = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (FireBaseAnalyticsManager.IsCheckInstance() && FireBaseAnalyticsManager.Instance.IsInitialized)
                {
                    dependencyReady = true;
                    break;
                }
                yield return null;
            }

            if (dependencyReady)
            {
                try
                {
                    _auth = FirebaseAuth.DefaultInstance;
                    _auth.StateChanged += OnAuthStateChanged;
                    IsInitialized = true;

                    FirebaseUser user = _auth.CurrentUser;
                    Debug.Log($"[FireBaseAuthManager] 초기화 성공 (복원된 유저: {(user != null ? user.UserId : "없음 - 비동기 복원 대기")})");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FireBaseAuthManager] FirebaseAuth 생성 실패: {e.Message}");
                }
            }
            else
            {
                Debug.LogError("[FireBaseAuthManager] Firebase 의존성 체크 대기 시간 초과 - 로그인 비활성 (FireBaseAnalyticsManager 초기화 실패/지연)");
            }

            IsInitializeDone = true;
        }

        protected override void OnDestroy()
        {
            if (_auth != null)
                _auth.StateChanged -= OnAuthStateChanged;

            base.OnDestroy();
        }

        // 로그인/복원/로그아웃 등 인증 상태 변화 추적.
        // 에디터/PC는 저장된 유저가 초기화 직후가 아니라 여기서 뒤늦게 복원되는 경우가 있다.
        private void OnAuthStateChanged(object sender, EventArgs e)
        {
            FirebaseUser user = _auth != null ? _auth.CurrentUser : null;
            Debug.Log($"[FireBaseAuthManager] AuthStateChanged: {(user != null ? $"{user.UserId} (익명={user.IsAnonymous})" : "미로그인")}");

            if (user != null)
                MarkEverLoggedIn();
        }

        private static void MarkEverLoggedIn()
        {
            if (PlayerPrefs.GetInt(HasEverLoggedInKey, 0) == 1)
                return;

            PlayerPrefs.SetInt(HasEverLoggedInKey, 1);
            PlayerPrefs.Save();
        }

        // ===== 로그인 =====

        // 게스트(익명) 로그인
        public void SignInAnonymously(Action<bool> onComplete)
        {
            if (!CheckCanSignIn(onComplete))
                return;

            SignInInternal(_auth.SignInAnonymouslyAsync(), "guest", onComplete);
        }

        // Google 로그인.
        // - Android: Credential Manager로 Google ID 토큰을 받아 Firebase 자격증명 로그인.
        //   (Firebase 웹 플로우 SignInWithProviderAsync는 Android에서 "package certificate hash"
        //    오류로 실패 - 2026-09-01 S25 Ultra 실기기 확인. Firebase 공식 문서도 Google은 ID 토큰 방식 안내)
        // - iOS 등: Firebase 제공 웹 플로우(FederatedOAuthProvider)
        public void SignInWithGoogle(Action<bool> onComplete)
        {
            if (!CheckCanSignIn(onComplete))
                return;

#if UNITY_EDITOR || UNITY_STANDALONE
            // 데스크톱(에디터 포함)에서는 Google 로그인 미지원 - 게스트 로그인으로 대체해 흐름만 검증
            Debug.LogWarning("[FireBaseAuthManager] 에디터/PC에서는 Google 로그인 미지원 - 게스트 로그인으로 대체합니다.");
            SignInInternal(_auth.SignInAnonymouslyAsync(), "google_editor_fallback", onComplete);
#elif UNITY_ANDROID
            _isSigningIn = true;
            GoogleCredentialHelper.RequestIdToken((idToken, error) =>
            {
                _isSigningIn = false;

                if (string.IsNullOrEmpty(idToken))
                {
                    Debug.LogWarning($"[FireBaseAuthManager] google ID 토큰 획득 실패: {error}");
                    onComplete?.Invoke(false);
                    return;
                }

                Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
                SignInInternal(_auth.SignInAndRetrieveDataWithCredentialAsync(credential), "google", onComplete);
            });
#else
            var providerData = new FederatedOAuthProviderData();
            providerData.ProviderId = GoogleAuthProvider.ProviderId; // "google.com"

            var provider = new FederatedOAuthProvider();
            provider.SetProviderData(providerData);

            SignInInternal(_auth.SignInWithProviderAsync(provider), "google", onComplete);
#endif
        }

        // 로그아웃 (디버그/테스트용). 저장된 로그인 상태와 로그인 이력 플래그를 함께 지운다.
        // 다음 실행 시 로그인 게이트가 복원 대기 없이 로그인 버튼부터 노출한다.
        public void SignOut()
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[FireBaseAuthManager] 아직 초기화되지 않았습니다.");
                return;
            }

            // 복원 전에 SignOut하면 no-op이 되어 저장 상태가 남는다 - 복원 후 다시 시도해야 실제로 지워진다
            if (_auth.CurrentUser == null)
            {
                PlayerPrefs.DeleteKey(HasEverLoggedInKey);
                PlayerPrefs.Save();
                Debug.LogWarning("[FireBaseAuthManager] 로그인된 유저가 없습니다 - 복원 대기 중이었다면 잠시 후 다시 실행하세요.");
                return;
            }

            _auth.SignOut();

            PlayerPrefs.DeleteKey(HasEverLoggedInKey);
            PlayerPrefs.Save();

            Debug.Log("[FireBaseAuthManager] 로그아웃 완료 - 다음 실행 시 로그인 버튼부터 시작합니다.");
        }

        // 현재 유저의 Firebase ID 토큰을 콜백으로 전달한다 (REST API 인증용, 미로그인/실패 시 null).
        // SDK가 토큰을 캐시하고 만료 시 자동 갱신하므로 매번 호출해도 부담이 없다.
        public void GetIdToken(Action<string> onToken)
        {
            if (!IsLoggedIn)
            {
                onToken?.Invoke(null);
                return;
            }

            _auth.CurrentUser.TokenAsync(false).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogWarning($"[FireBaseAuthManager] ID 토큰 획득 실패: {task.Exception?.GetBaseException().Message}");
                    onToken?.Invoke(null);
                    return;
                }

                onToken?.Invoke(task.Result);
            });
        }

        // ===== 내부 =====

        private bool CheckCanSignIn(Action<bool> onComplete)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[FireBaseAuthManager] 아직 초기화되지 않았습니다.");
                onComplete?.Invoke(false);
                return false;
            }

            if (_isSigningIn)
            {
                Debug.LogWarning("[FireBaseAuthManager] 이미 로그인 진행 중입니다.");
                return false;
            }

            return true;
        }

        private void SignInInternal(Task<AuthResult> signInTask, string method, Action<bool> onComplete)
        {
            _isSigningIn = true;

            signInTask.ContinueWithOnMainThread(task =>
            {
                _isSigningIn = false;

                if (task.IsCanceled || task.IsFaulted)
                {
                    string reason = task.Exception != null ? task.Exception.GetBaseException().Message : "취소됨";
                    Debug.LogWarning($"[FireBaseAuthManager] {method} 로그인 실패: {reason}");
                    onComplete?.Invoke(false);
                    return;
                }

                FirebaseUser user = task.Result.User;
                Debug.Log($"[FireBaseAuthManager] {method} 로그인 성공: {user.UserId} (익명={user.IsAnonymous})");

                FireBaseAnalyticsManager.Instance.LogEvent("login", "method", method);

                onComplete?.Invoke(true);
            });
        }
    }
}
