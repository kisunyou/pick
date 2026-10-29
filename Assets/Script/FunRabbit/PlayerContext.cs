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
            SetItemAmount(COIN_ITEM_KEY, CoinWallet.Load().balance);
            DollCountGage.Value = PlayerPrefs.GetInt("DollCountGage", 0);
            RandomBoxCount.Value = PlayerPrefs.GetInt("RandomBoxCount", 0);
            RandomBoxProgressValue.Value = PlayerPrefs.GetFloat("RandomBoxProgress", 0f);
            SetItemAmount(RESET_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(RESET_ITEM_KEY), 0));
            SetItemAmount(ATTACK_POWER_UP_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(ATTACK_POWER_UP_ITEM_KEY), 0));
            SetItemAmount(DEFENSE_UP_ITEM_KEY, PlayerPrefs.GetInt(ItemAmountKey(DEFENSE_UP_ITEM_KEY), 0));
        }

        private static string ItemAmountKey(int itemKey) => $"{KEY_ITEM_AMOUNT_PREFIX}{itemKey}";

        // ===== 저장 묶음 (보상 지급 + 진행 상태 변경을 한 번의 PlayerPrefs.Save 로 반영) =====
        // RunAtomic 안에서 호출된 PlayerContext 의 쓰기는 디스크 반영을 미뤘다가 마지막에 한 번만 Save 한다.
        // "진행 상태는 저장됐는데 보상은 연출 도착 후 지급" 같은 중간 상태가 남지 않게 하는 용도.
        // (중첩 호출 가능. 바깥 RunAtomic 이 끝날 때 한 번 반영된다)
        private static int _saveBatchDepth;

        public static bool IsInSaveBatch => _saveBatchDepth > 0;

        public static void RunAtomic(Action action)
        {
            _saveBatchDepth++;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _saveBatchDepth--;
                if (_saveBatchDepth == 0)
                    PlayerPrefs.Save();
            }
        }

        // 묶음 저장 중이 아니면 즉시 디스크에 반영한다.
        private static void FlushPrefs()
        {
            if (_saveBatchDepth == 0)
                PlayerPrefs.Save();
        }

        // itemKey의 보유 개수를 반환한다 (딕셔너리에 없으면 0).
        public static long GetItemAmount(int itemKey)
        {
            return _itemAmounts.TryGetValue(itemKey, out long amount) ? amount : 0;
        }

        // itemKey의 보유 개수를 딕셔너리/PlayerPrefs에 반영하고, 그 itemKey를 구독 중인 콜백들을 호출한다.
        public static void SetItemAmount(int itemKey, long amount)
        {
            if (itemKey == COIN_ITEM_KEY)
            {
                if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                CoinWallet wallet = CoinWallet.Load();
                wallet.balance = amount;
                wallet.Save(flush: !IsInSaveBatch);
            }
            else
            {
                PlayerPrefs.SetInt(ItemAmountKey(itemKey), checked((int)amount));
                FlushPrefs();
            }
            _itemAmounts[itemKey] = amount;

            if (_itemAmountChanged.TryGetValue(itemKey, out Action<long> callback))
                callback?.Invoke(amount);
        }

        public static bool GrantPurchaseOnce(string transactionId, long amount)
        {
            CoinWallet wallet = CoinWallet.Load();
            bool granted = wallet.Grant(transactionId, amount);
            wallet.Save(flush: !IsInSaveBatch);
            _itemAmounts[COIN_ITEM_KEY] = wallet.balance;
            if (_itemAmountChanged.TryGetValue(COIN_ITEM_KEY, out Action<long> callback))
                callback?.Invoke(wallet.balance);
            if (granted) GameplayAnalytics.CoinsChanged(amount, wallet.balance, "purchase_" + transactionId);
            return granted;
        }

        public static void AddItemAmount(int itemKey, long amount)
        {
            long balance = checked(GetItemAmount(itemKey) + amount);
            SetItemAmount(itemKey, balance);
            if (itemKey == COIN_ITEM_KEY) GameplayAnalytics.CoinsChanged(amount, balance);
        }

        public static bool GrantAdRewardOnce(string requestId, long amount, string date)
        {
            CoinWallet wallet = CoinWallet.Load();
            // 지급 시점에도 하루 한도를 검사한다 (팝업 시점 검사만으로는 날짜 조작·지연 수령을 막지 못함)
            bool granted = wallet.GrantAdReward(requestId, amount, date, WATCH_AD_DAILY_LIMIT, TodayString());
            wallet.Save(flush: !IsInSaveBatch);
            if (!granted)
                Debug.LogWarning($"[PlayerContext] 광고 보상 미지급(중복 또는 일일 한도 초과): {requestId}");
            _itemAmounts[COIN_ITEM_KEY] = wallet.balance;
            if (_itemAmountChanged.TryGetValue(COIN_ITEM_KEY, out Action<long> callback)) callback?.Invoke(wallet.balance);
            if (granted) GameplayAnalytics.CoinsChanged(amount, wallet.balance, "ad_" + requestId);
            return granted;
        }

        // 보유량이 충분하면 차감 후 true, 부족하면 false.
        public static bool TrySpendItemAmount(int itemKey, long amount)
        {
            if (SessionOperation.IsBusy || amount < 0) return false;
            long current = GetItemAmount(itemKey);
            if (current < amount)
                return false;

            SetItemAmount(itemKey, current - amount);
            if (itemKey == COIN_ITEM_KEY) GameplayAnalytics.CoinsChanged(-amount, current - amount);
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
            FlushPrefs();
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
            FlushPrefs();
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
            FlushPrefs();
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

        // ===== 아군 액터 보상 (지급 대기) =====
        // 랜덤박스·뽑기 미션·인형 획득으로 얻은 아군은 "얻는 순간" 여기 먼저 저장(지급 확정)하고,
        // 트레일 연출이 도착하면 TryClaimPendingAllyReward 로 목록에서 빼면서 실제로 합류시킨다.
        // 연출 도중 앱이 종료되면 재시작 시 ActorBattleSystem 이 남은 항목을 연출 없이 지급한다.
        // 저장 형식: "animalKey:count:id" 를 ',' 로 연결 (id 없는 구버전 "animalKey:count" 도 읽는다).

        public struct PendingAllyReward
        {
            public string animalKey;
            public int count;
            public string id; // 항목 고유 id (구버전 항목은 빈 값)
        }

        private const string KEY_PENDING_ALLY_REWARDS = "PendingAllyRewards";
        private const char PENDING_ALLY_ENTRY_DELIMITER = ',';
        private const char PENDING_ALLY_FIELD_DELIMITER = ':';

        // 트레일 연출이 이미 맡아 날아가고 있는 항목 id (메모리 전용). 다른 경로(랜덤박스 패널 닫힘)가
        // 같은 항목으로 트레일을 한 번 더 띄우지 않도록 걸러낸다. 지급 중복은 TryClaim 의 제거 성공 여부로 막는다.
        private static readonly HashSet<string> _inFlightAllyRewardIds = new HashSet<string>();

        // 지급 대기 항목을 추가하고 그 항목을 반환한다 (잘못된 입력이면 id 가 null).
        public static PendingAllyReward AddPendingAllyReward(string animalKey, int count)
        {
            PendingAllyReward reward = new PendingAllyReward { animalKey = animalKey, count = count };
            if (string.IsNullOrEmpty(animalKey) || count <= 0)
                return reward;

            reward.id = Guid.NewGuid().ToString("N");
            List<PendingAllyReward> rewards = GetPendingAllyRewards();
            rewards.Add(reward);
            SavePendingAllyRewards(rewards);
            return reward;
        }

        // includeInFlight=false 면 트레일이 이미 맡은 항목은 제외한다.
        public static List<PendingAllyReward> GetPendingAllyRewards(bool includeInFlight = true)
        {
            List<PendingAllyReward> rewards = new List<PendingAllyReward>();

            string saved = PlayerPrefs.GetString(KEY_PENDING_ALLY_REWARDS, string.Empty);
            if (string.IsNullOrEmpty(saved))
                return rewards;

            foreach (string entry in saved.Split(PENDING_ALLY_ENTRY_DELIMITER))
            {
                string[] fields = entry.Split(PENDING_ALLY_FIELD_DELIMITER);
                if ((fields.Length != 2 && fields.Length != 3) || !int.TryParse(fields[1], out int count))
                    continue;

                string id = fields.Length == 3 ? fields[2] : string.Empty;
                if (!includeInFlight && !string.IsNullOrEmpty(id) && _inFlightAllyRewardIds.Contains(id))
                    continue;

                rewards.Add(new PendingAllyReward { animalKey = fields[0], count = count, id = id });
            }

            return rewards;
        }

        // 트레일 연출이 이 항목을 맡았음을 표시한다 (메모리 전용)
        public static void MarkPendingAllyRewardInFlight(string id)
        {
            if (!string.IsNullOrEmpty(id))
                _inFlightAllyRewardIds.Add(id);
        }

        // 트레일이 지급하지 못하고 사라질 때 표시를 풀어 다른 경로가 지급할 수 있게 한다.
        public static void UnmarkPendingAllyRewardInFlight(string id)
        {
            if (!string.IsNullOrEmpty(id))
                _inFlightAllyRewardIds.Remove(id);
        }

        // 지급 대기 항목을 꺼낸다(제거). 실제로 제거했을 때만 true - 호출자는 true 일 때만 아군을 합류시킨다.
        // (같은 항목을 두 경로가 지급하려 해도 한 번만 성공한다)
        public static bool TryClaimPendingAllyReward(PendingAllyReward reward)
        {
            UnmarkPendingAllyRewardInFlight(reward.id);

            List<PendingAllyReward> rewards = GetPendingAllyRewards();
            int index = string.IsNullOrEmpty(reward.id)
                ? rewards.FindIndex(r => r.animalKey == reward.animalKey && r.count == reward.count)
                : rewards.FindIndex(r => r.id == reward.id);
            if (index < 0)
                return false;

            rewards.RemoveAt(index);
            SavePendingAllyRewards(rewards);
            return true;
        }

        // 지급 대기 항목을 꺼내 전투(ActorBattleSystem)에 합류시킨다 (트레일 도착 시 호출).
        // 전투 시스템이 없으면 항목을 그대로 남겨 재시작 지급 경로에 맡기고 false.
        // 이미 다른 경로가 지급한 항목이면 아무것도 하지 않고 false (중복 지급 방지).
        public static bool DeliverPendingAllyReward(PendingAllyReward reward)
        {
            if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
            {
                UnmarkPendingAllyRewardInFlight(reward.id);
                return false;
            }

            if (!TryClaimPendingAllyReward(reward))
                return false;

            battleSystem.AddAllyActors(reward.animalKey, reward.count);
            return true;
        }

        // 지급이 끝난 항목을 목록에서 제거한다 (같은 내용이 여러 개면 첫 항목만). 제거했으면 true.
        // (재시작 복원 경로 ActorBattleSystem.GrantPendingAllyRewards 가 사용)
        public static bool RemovePendingAllyReward(string animalKey, int count)
        {
            List<PendingAllyReward> rewards = GetPendingAllyRewards();
            int index = rewards.FindIndex(r => r.animalKey == animalKey && r.count == count);
            if (index < 0)
                return false;

            UnmarkPendingAllyRewardInFlight(rewards[index].id);
            rewards.RemoveAt(index);
            SavePendingAllyRewards(rewards);
            return true;
        }

        private static void SavePendingAllyRewards(List<PendingAllyReward> rewards)
        {
            List<string> entries = new List<string>(rewards.Count);
            foreach (PendingAllyReward reward in rewards)
            {
                string entry = $"{reward.animalKey}{PENDING_ALLY_FIELD_DELIMITER}{reward.count}";
                if (!string.IsNullOrEmpty(reward.id))
                    entry += $"{PENDING_ALLY_FIELD_DELIMITER}{reward.id}";
                entries.Add(entry);
            }

            PlayerPrefs.SetString(KEY_PENDING_ALLY_REWARDS, string.Join(PENDING_ALLY_ENTRY_DELIMITER.ToString(), entries));
            FlushPrefs();
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
            FlushPrefs();
        }

        public static void SetMissionProgress(int progress)
        {
            PlayerPrefs.SetInt(KEY_MISSION_PROGRESS, progress);
            FlushPrefs();
        }

        // ===== 하루 광고 시청 횟수 제한 =====
        // 날짜(로컬 자정 기준)가 바뀌면 자동으로 0부터 다시 시작한다.
        // 차감은 보상 지급 시점(광고 끝까지 시청)에만 한다 - 중도 이탈/로드 실패는 소모 안 됨.
        // 기기 날짜 조작 방어: 저장 날짜가 오늘보다 미래면(날짜를 앞당겼다 되돌림) 오늘로 간주해 리셋하지 않는다.
        // (CoinWallet.ResolveAdRewardDate - 서버 시간 없이 가능한 범위)

        public const int WATCH_AD_DAILY_LIMIT = 10;

        // 날짜 문자열은 반드시 InvariantCulture (태국어 등 현지 달력(불기)이면 연도가 달라져 비교가 깨진다)
        private static string TodayString() =>
            DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        // 오늘 시청한 광고 횟수. 저장된 날짜가 과거면 0 (자동 리셋), 미래면 오늘로 간주해 저장값 유지.
        public static int GetTodayWatchAdCount()
        {
            CoinWallet wallet = CoinWallet.Load();
            string effective = CoinWallet.ResolveAdRewardDate(wallet.adRewardDate, TodayString());
            return effective == wallet.adRewardDate ? wallet.adRewardCount : 0;
        }

        // 오늘 남은 광고 시청 가능 횟수 (0 이상)
        public static int GetRemainingWatchAdCount()
        {
            return Mathf.Max(0, WATCH_AD_DAILY_LIMIT - GetTodayWatchAdCount());
        }

        // 광고 시청 1회 기록 (보상 지급 시점에 호출)
        public static void AddWatchAdCount()
        {
            CoinWallet wallet = CoinWallet.Load();
            int count = GetTodayWatchAdCount();
            wallet.adRewardDate = CoinWallet.ResolveAdRewardDate(wallet.adRewardDate, TodayString());
            wallet.adRewardCount = count + 1;
            wallet.Save(flush: !IsInSaveBatch);
        }
    }
}
