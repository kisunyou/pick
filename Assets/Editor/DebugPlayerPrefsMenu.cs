using UnityEditor;
using UnityEngine;

namespace FunRabbit.EditorTools
{
    // 테스트용 PlayerPrefs 조작 메뉴 (에디터 전용). 코인 키는 PlayerContext.ItemAmountKey(COIN_ITEM_KEY) = "ItemAmount_1".
    // 플레이 모드가 아닐 때 실행할 것 - 플레이 중에는 PlayerContext 의 메모리 값이 우선이라 다음 실행부터 반영된다.
    public static class DebugPlayerPrefsMenu
    {
        const string CoinKey = "ItemAmount_1";

        [MenuItem("FunRabbit/Debug/코인 0으로 설정")]
        static void SetCoinZero() => SetCoin(0);

        [MenuItem("FunRabbit/Debug/코인 100,000으로 설정")]
        static void SetCoinRich() => SetCoin(100000);

        [MenuItem("FunRabbit/Debug/현재 코인 로그")]
        static void LogCoin()
        {
            Debug.Log($"[DebugPlayerPrefs] {CoinKey} = {PlayerPrefs.GetInt(CoinKey, -1)} (-1 = 키 없음, 기본 2000)");
        }

        // Firebase 로그아웃 + 로그인 이력 플래그 제거 - 다음 실행 시 로그인 버튼부터 시작.
        // ⚠️ 데스크톱 SDK는 저장된 유저를 비동기로 복원하므로, 복원 전에 SignOut하면 no-op이 되어
        // 재시작 시 다시 로그인된다. 반드시 복원 완료(CurrentUser != null)를 기다렸다가 로그아웃한다.
        [MenuItem("FunRabbit/Debug/로그인 초기화 (로그아웃)")]
        static void ResetLogin()
        {
            // 플레이 중이고 매니저가 살아있으면 매니저 경유 (플래그 제거까지 한 번에 처리)
            if (Application.isPlaying && FireBaseAuthManager.IsCheckInstance() && FireBaseAuthManager.Instance.IsInitialized)
            {
                FireBaseAuthManager.Instance.SignOut();
                return;
            }

            // 비플레이 상태 - 에디터에서 직접 로그아웃 (복원 완료를 기다렸다가 실행)
            try
            {
                var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;

                if (auth.CurrentUser != null)
                {
                    SignOutAndClearFlag(auth);
                    return;
                }

                // 복원이 아직 안 됐으면 EditorApplication.update로 최대 5초 대기 후 로그아웃
                double deadline = EditorApplication.timeSinceStartup + 5.0;
                EditorApplication.CallbackFunction tick = null;
                tick = () =>
                {
                    if (auth.CurrentUser != null)
                    {
                        EditorApplication.update -= tick;
                        SignOutAndClearFlag(auth);
                    }
                    else if (EditorApplication.timeSinceStartup > deadline)
                    {
                        EditorApplication.update -= tick;
                        ClearLoginFlag();
                        Debug.Log("[DebugPlayerPrefs] 저장된 로그인 유저 없음 - 이미 로그아웃 상태입니다.");
                    }
                };
                EditorApplication.update += tick;
                Debug.Log("[DebugPlayerPrefs] 저장된 유저 복원 대기 중... (최대 5초 - 완료 로그를 확인하세요)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DebugPlayerPrefs] 에디터 로그아웃 실패({e.Message}) - 플레이 모드에서 다시 실행하세요.");
            }
        }

        static void SignOutAndClearFlag(Firebase.Auth.FirebaseAuth auth)
        {
            auth.SignOut();
            ClearLoginFlag();
            Debug.Log("[DebugPlayerPrefs] 로그인 초기화 완료 - 다음 실행 시 로그인 버튼부터 시작합니다.");
        }

        static void ClearLoginFlag()
        {
            PlayerPrefs.DeleteKey(FireBaseAuthManager.HasEverLoggedInKey);
            PlayerPrefs.Save();
        }

        static void SetCoin(int amount)
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[DebugPlayerPrefs] 플레이 모드 중에는 PlayerContext 메모리 값이 우선입니다 - 플레이를 멈추고 실행하세요.");
                return;
            }

            PlayerPrefs.SetInt(CoinKey, amount);
            PlayerPrefs.Save();
            Debug.Log($"[DebugPlayerPrefs] {CoinKey} = {amount} 저장");
        }
    }
}
