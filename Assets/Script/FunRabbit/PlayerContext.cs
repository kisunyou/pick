using UnityEngine;

namespace FunRabbit
{
    public class PlayerContext
    {
        public static DataObserver<long> CoinAmount = new DataObserver<long>();
        public static DataObserver<int> DollCountGage = new DataObserver<int>();
        public static System.Action OnFullDollCountGage { get; set; }


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

        public static void AddCoinAmount(long amount)
        {
            SetCoinAmount(CoinAmount.Value + amount);
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

        public static void AddDollCountGage()
        {
            DollCountGage.Value++;

            if(DollCountGage.Value >= 10)
            {
                OnFullDollCountGage?.Invoke();
                DollCountGage.Value = 0;
            }
        }
    }
}
