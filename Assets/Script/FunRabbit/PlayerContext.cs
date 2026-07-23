using UnityEngine;

namespace FunRabbit
{
    public class PlayerContext
    {
        public static DataObserver<long> CoinAmount = new DataObserver<long>();
        public static DataObserver<int> DollCountGage = new DataObserver<int>();
        // 미션과 무관한 인형을 받아 쌓이는 랜덤박스 카운트
        public static DataObserver<int> RandomBoxCount = new DataObserver<int>();
        // 랜덤박스 진행 게이지 (0~1). 1을 넘으면 RandomBoxCount가 증가한다.
        public static DataObserver<float> RandomBoxProgressValue = new DataObserver<float>();
        public static System.Action OnFullDollCountGage { get; set; }
        // 보유한 리셋 아이템 개수
        public static DataObserver<int> ResetItemCount = new DataObserver<int>();


        public static void Initialize()
        {
            // 프로그램이 종료됐다 다시 켜져도 값이 유지되도록 PlayerPrefs에서 불러온다.
            // (키가 없는 첫 실행 시에는 기본값 사용)
            CoinAmount.Value = PlayerPrefs.GetInt("CoinAmount", 9000);
            DollCountGage.Value = PlayerPrefs.GetInt("DollCountGage", 0);
            RandomBoxCount.Value = PlayerPrefs.GetInt("RandomBoxCount", 0);
            RandomBoxProgressValue.Value = PlayerPrefs.GetFloat("RandomBoxProgress", 0f);
            ResetItemCount.Value = PlayerPrefs.GetInt("ResetItemCount", 0);
        }

        public static void SetCoinAmount(long amount)
        {
            CoinAmount.Value = amount;
            PlayerPrefs.SetInt("CoinAmount", (int)amount);
            PlayerPrefs.Save();
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

        // ===== 랜덤박스 =====

        public static void SetRandomBoxCount(int count)
        {
            RandomBoxCount.Value = count;
            PlayerPrefs.SetInt("RandomBoxCount", count);
            PlayerPrefs.Save();
        }

        // 랜덤박스 카운트 증가 (미션과 무관한 인형을 받았을 때)
        public static void AddRandomBox(int amount = 1)
        {
            SetRandomBoxCount(RandomBoxCount.Value + amount);
        }

        // 랜덤박스 소비 (열기 시). 보유량이 부족하면 false를 반환한다.
        public static bool SpendRandomBox(int amount = 1)
        {
            if (RandomBoxCount.Value < amount)
                return false;

            SetRandomBoxCount(RandomBoxCount.Value - amount);
            return true;
        }

        public static void SetRandomBoxProgressValue(float value)
        {
            RandomBoxProgressValue.Value = value;
            PlayerPrefs.SetFloat("RandomBoxProgress", value);
            PlayerPrefs.Save();
        }

        // 진행 게이지를 누적한다. 1.0을 넘을 때마다 RandomBoxCount가 1씩 증가하고
        // 남은 소수부만 게이지에 유지한다. (예: +1.3 → 박스 1개 + 게이지 0.3)
        public static void AddRandomBoxProgressValue(float value)
        {
            if (value <= 0f)
                return;

            float progress = RandomBoxProgressValue.Value + value;

            int gained = Mathf.FloorToInt(progress);
            if (gained > 0)
            {
                progress -= gained;   // 정수부만큼 박스로 전환, 소수부 유지
                AddRandomBox(gained);
            }

            SetRandomBoxProgressValue(progress);
        }

        public static void SetDollCountGage(int value)
        {
            DollCountGage.Value = value;
            PlayerPrefs.SetInt("DollCountGage", value);
            PlayerPrefs.Save();
        }

        public static void AddDollCountGage()
        {
            SetDollCountGage(DollCountGage.Value + 1);

            if (DollCountGage.Value >= 10)
            {
                OnFullDollCountGage?.Invoke();
                SetDollCountGage(0);
            }
        }

        // ===== 리셋 아이템 =====

        public static void SetResetItemCount(int count)
        {
            ResetItemCount.Value = count;
            PlayerPrefs.SetInt("ResetItemCount", count);
            PlayerPrefs.Save();
        }

        public static void AddResetItemCount(int amount = 1)
        {
            SetResetItemCount(ResetItemCount.Value + amount);
        }
    }
}
