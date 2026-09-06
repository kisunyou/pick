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

        // ===== 랜덤박스 아군 액터 보상 (지급 대기) =====
        // 랜덤박스에서 뽑은 아군 액터 아이템은 보상 팝업 확인 시점에 여기 쌓이고,
        // 랜덤박스 패널이 닫힐 때 트레일 연출과 함께 지급된다. 패널이 열린 채 앱이 종료되면
        // 재시작 시 ActorBattleSystem이 연출 없이 바로 지급한다.

        public struct PendingAllyReward
        {
            public string animalKey;
            public int count;
        }

        private const string KEY_PENDING_ALLY_REWARDS = "PendingAllyRewards";
        private const char PENDING_ALLY_ENTRY_DELIMITER = ',';
        private const char PENDING_ALLY_FIELD_DELIMITER = ':';

        public static void AddPendingAllyReward(string animalKey, int count)
        {
            if (string.IsNullOrEmpty(animalKey) || count <= 0)
                return;

            string saved = PlayerPrefs.GetString(KEY_PENDING_ALLY_REWARDS, string.Empty);
            string entry = $"{animalKey}{PENDING_ALLY_FIELD_DELIMITER}{count}";
            saved = string.IsNullOrEmpty(saved) ? entry : saved + PENDING_ALLY_ENTRY_DELIMITER + entry;

            PlayerPrefs.SetString(KEY_PENDING_ALLY_REWARDS, saved);
            PlayerPrefs.Save();
        }

        public static List<PendingAllyReward> GetPendingAllyRewards()
        {
            List<PendingAllyReward> rewards = new List<PendingAllyReward>();

            string saved = PlayerPrefs.GetString(KEY_PENDING_ALLY_REWARDS, string.Empty);
            if (string.IsNullOrEmpty(saved))
                return rewards;

            foreach (string entry in saved.Split(PENDING_ALLY_ENTRY_DELIMITER))
            {
                string[] fields = entry.Split(PENDING_ALLY_FIELD_DELIMITER);
                if (fields.Length != 2 || !int.TryParse(fields[1], out int count))
                    continue;

                rewards.Add(new PendingAllyReward { animalKey = fields[0], count = count });
            }

            return rewards;
        }

        // 지급이 끝난 항목을 목록에서 제거한다 (같은 내용이 여러 개면 첫 항목만).
        public static void RemovePendingAllyReward(string animalKey, int count)
        {
            List<PendingAllyReward> rewards = GetPendingAllyRewards();
            int index = rewards.FindIndex(r => r.animalKey == animalKey && r.count == count);
            if (index < 0)
                return;

            rewards.RemoveAt(index);

            List<string> entries = new List<string>(rewards.Count);
            foreach (PendingAllyReward reward in rewards)
                entries.Add($"{reward.animalKey}{PENDING_ALLY_FIELD_DELIMITER}{reward.count}");

            PlayerPrefs.SetString(KEY_PENDING_ALLY_REWARDS, string.Join(PENDING_ALLY_ENTRY_DELIMITER.ToString(), entries));
            PlayerPrefs.Save();
        }

        // ===== 뽑기 미션 (MissionSystem) =====
        // 진행 중 미션 하나의 상태만 저장한다. 키가 0이면 진행 중 미션 없음 → MissionSystem이 새로 추첨.

        private const string KEY_MISSION_KEY = "MissionKey";
        private const string KEY_MISSION_ANIMAL = "MissionAnimalKey";
        private const string KEY_MISSION_PROGRESS = "MissionProgress";

        public static int GetMissionKey() => PlayerPrefs.GetInt(KEY_MISSION_KEY, 0);
        public static string GetMissionAnimalKey() => PlayerPrefs.GetString(KEY_MISSION_ANIMAL, string.Empty);
        public static int GetMissionProgress() => PlayerPrefs.GetInt(KEY_MISSION_PROGRESS, 0);

        // 새 미션 시작 (진행도 0으로 리셋). animalKey는 actor 미션의 대상 동물 (randombox 미션은 빈 값)
        public static void SetMission(int missionKey, string animalKey)
        {
            PlayerPrefs.SetInt(KEY_MISSION_KEY, missionKey);
            PlayerPrefs.SetString(KEY_MISSION_ANIMAL, animalKey ?? string.Empty);
            PlayerPrefs.SetInt(KEY_MISSION_PROGRESS, 0);
            PlayerPrefs.Save();
        }

        public static void SetMissionProgress(int progress)
        {
            PlayerPrefs.SetInt(KEY_MISSION_PROGRESS, progress);
            PlayerPrefs.Save();
        }

        // ===== 하루 광고 시청 횟수 제한 =====
        // 날짜(로컬 자정 기준)가 바뀌면 자동으로 0부터 다시 시작한다.
        // 차감은 보상 지급 시점(광고 끝까지 시청)에만 한다 - 중도 이탈/로드 실패는 소모 안 됨.

        public const int WATCH_AD_DAILY_LIMIT = 10;

        private const string KEY_WATCH_AD_DATE = "WatchAdDate";
        private const string KEY_WATCH_AD_COUNT = "WatchAdCount";

        private static string TodayString() => DateTime.Now.ToString("yyyyMMdd");

        // 오늘 시청한 광고 횟수. 저장된 날짜가 오늘이 아니면 0 (자동 리셋).
        public static int GetTodayWatchAdCount()
        {
            if (PlayerPrefs.GetString(KEY_WATCH_AD_DATE, string.Empty) != TodayString())
                return 0;

            return PlayerPrefs.GetInt(KEY_WATCH_AD_COUNT, 0);
        }

        // 오늘 남은 광고 시청 가능 횟수 (0 이상)
        public static int GetRemainingWatchAdCount()
        {
            return Mathf.Max(0, WATCH_AD_DAILY_LIMIT - GetTodayWatchAdCount());
        }

        // 광고 시청 1회 기록 (보상 지급 시점에 호출)
        public static void AddWatchAdCount()
        {
            int count = GetTodayWatchAdCount() + 1; // 날짜가 바뀌었으면 0+1부터
            PlayerPrefs.SetString(KEY_WATCH_AD_DATE, TodayString());
            PlayerPrefs.SetInt(KEY_WATCH_AD_COUNT, count);
            PlayerPrefs.Save();
        }
    }
}
