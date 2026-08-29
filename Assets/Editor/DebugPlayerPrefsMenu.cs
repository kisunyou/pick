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
