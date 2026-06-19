using UnityEngine;

namespace FunRabbit
{
    public class PlayerContext
    {
        public static DataObserver<long> CoinAmount = new DataObserver<long>();

        public static void Initialize()
        {
            // 초기화 시 PlayerPrefs에서 코인 수량을 불러옴
            long coinAmount = PlayerPrefs.GetInt("CoinAmount", 9000);
            CoinAmount.Value = coinAmount;
        }

        public static void SetCoinAmount(long amount)
        {
            CoinAmount.Value = amount;
            PlayerPrefs.SetInt("CoinAmount", (int)amount);
        }

        public static bool TrySpendCoin(long amount)
        {
            if (CoinAmount.Value >= amount)
            {
                SetCoinAmount(CoinAmount.Value - amount);
                return true;
            }
            return false;
        }
    }
}
