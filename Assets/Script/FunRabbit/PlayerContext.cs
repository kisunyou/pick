using System;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class PlayerContext
    {
        // item.json 기준 아이템 키 (코인/리셋 아이템은 itemKey로 구분되는 보유 개수 아이템이다).
        // 새 보유 개수형 아이템(예: apUp=9, deffenseUp=10)이 추가돼도 여기 상수 하나만 늘면 되고,
        // PlayerContext에 필드/메서드를 새로 만들 필요 없이 GetItemAmount/AddItemAmount/AttachItemAmount를 그대로 쓴다.
        public const int COIN_ITEM_KEY = 1;
        public const int RESET_ITEM_KEY = 8;
        public const int ATTACK_POWER_UP_ITEM_KEY = 9;
        public const int DEFENSE_UP_ITEM_KEY = 10;

        private const string KEY_ITEM_AMOUNT_PREFIX = "ItemAmount_";

        // itemKey -> 보유 개수 (보유 개수형 아이템의 유일한 저장소)
        private static readonly Dictionary<int, long> _itemAmounts = new Dictionary<int, long>();
        // itemKey -> 그 itemKey의 보유 개수가 바뀔 때마다 호출되는 콜백들
        private static readonly Dictionary<int, Action<long>> _itemAmountChanged = new Dictionary<int, Action<long>>();

        public static DataObserver<int> DollCountGage = new DataObserver<int>();
        // 미션과 무관한 인형을 받아 쌓이는 랜덤박스 카운트
        public static DataObserver<int> RandomBoxCount = new DataObserver<int>();
        // 랜덤박스 진행 게이지 (0~1). 1을 넘으면 RandomBoxCount가 증가한다.
        public static DataObserver<float> RandomBoxProgressValue = new DataObserver<float>();
        public static System.Action OnFullDollCountGage { get; set; }


        public static void Initialize()
        {
            // 프로그램이 종료됐다 다시 켜져도 값이 유지되도록 PlayerPrefs에서 불러온다.
            // (키가 없는 첫 실행 시에는 기본값 사용)
            // 시작 코인: 스테이지 1은 여유 있게 깨되(크레인 1회 100코인, 20회분), 그 이후부터는
            // 코인이 조금씩 부족해지도록 낮게 잡았다 (기존 9000 → 2000).
            SetItemAmount(COIN_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(COIN_ITEM_KEY), 2000));
            DollCountGage.Value = PlayerPrefs.GetInt("DollCountGage", 0);
            RandomBoxCount.Value = PlayerPrefs.GetInt("RandomBoxCount", 0);
            RandomBoxProgressValue.Value = PlayerPrefs.GetFloat("RandomBoxProgress", 0f);
            SetItemAmount(RESET_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(RESET_ITEM_KEY), 0));
            SetItemAmount(ATTACK_POWER_UP_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(ATTACK_POWER_UP_ITEM_KEY), 0));
            SetItemAmount(DEFENSE_UP_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(DEFENSE_UP_ITEM_KEY), 0));
        }

        private static string ItemAmountKey(int itemKey) => $"{KEY_ITEM_AMOUNT_PREFIX}{itemKey}";

        // itemKey의 보유 개수를 반환한다 (딕셔너리에 없으면 0).
        public static long GetItemAmount(int itemKey)
        {
            return _itemAmounts.TryGetValue(itemKey, out long amount) ? amount : 0;
        }

        // itemKey의 보유 개수를 딕셔너리/PlayerPrefs에 반영하고, 그 itemKey를 구독 중인 콜백들을 호출한다.
        public static void SetItemAmount(int itemKey, long amount)
        {
            _itemAmounts[itemKey] = amount;
            PlayerPrefs.SetInt(ItemAmountKey(itemKey), (int)amount);
            PlayerPrefs.Save();

            if (_itemAmountChanged.TryGetValue(itemKey, out Action<long> callback))
                callback?.Invoke(amount);
        }

        public static void AddItemAmount(int itemKey, long amount)
        {
            SetItemAmount(itemKey, GetItemAmount(itemKey) + amount);
        }

        // 보유량이 충분하면 차감 후 true, 부족하면 false.
        public static bool TrySpendItemAmount(int itemKey, long amount)
        {
            long current = GetItemAmount(itemKey);
            if (current < amount)
                return false;

            SetItemAmount(itemKey, current - amount);
            return true;
        }

        // itemKey의 보유 개수가 바뀔 때마다 callback을 호출하도록 구독한다.
        // DataObserver.Attach와 동일하게, 구독 즉시 현재 값으로 1회 콜백된다.
        public static void AttachItemAmount(int itemKey, Action<long> callback)
        {
            if (callback == null)
                return;

            _itemAmountChanged.TryGetValue(itemKey, out Action<long> existing);
            _itemAmountChanged[itemKey] = existing + callback;
            callback.Invoke(GetItemAmount(itemKey));
        }

        public static void DetachItemAmount(int itemKey, Action<long> callback)
        {
            if (callback == null || !_itemAmountChanged.TryGetValue(itemKey, out Action<long> existing))
                return;

            _itemAmountChanged[itemKey] = existing - callback;
        }

        public static void SetCoinAmount(long amount)
        {
            SetItemAmount(COIN_ITEM_KEY, amount);
        }

        public static void AddCoinAmount(long amount)
        {
            AddItemAmount(COIN_ITEM_KEY, amount);
        }

        public static bool TrySpendCoin(long amount)
        {
            return TrySpendItemAmount(COIN_ITEM_KEY, amount);
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
            SetItemAmount(RESET_ITEM_KEY, count);
        }

        public static void AddResetItemCount(int amount = 1)
        {
            AddItemAmount(RESET_ITEM_KEY, amount);
        }
    }
}
