using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace FunRabbit
{
    // Balance and processed transactions are one durable record. Never write a
    // transaction marker separately from the balance it protects.
    [Serializable]
    public sealed class CoinWallet
    {
        public const string PrefsKey = "CoinWalletV2";
        public int version = 2;
        public long balance;
        public List<string> transactions = new List<string>();
        public string adRewardDate = "";
        public int adRewardCount;
        public List<string> adRewardIds = new List<string>();

        public static CoinWallet Load()
        {
            CoinWallet wallet = PlayerPrefs.HasKey(PrefsKey) ? Parse(PlayerPrefs.GetString(PrefsKey)) :
                new CoinWallet { balance = PlayerPrefs.GetInt("ItemAmount_1", 2000) };
            if (string.IsNullOrEmpty(wallet.adRewardDate))
            {
                wallet.adRewardDate = PlayerPrefs.GetString("WatchAdDate", "");
                wallet.adRewardCount = Mathf.Max(0, PlayerPrefs.GetInt("WatchAdCount", 0));
            }
            return wallet;
        }

        public static CoinWallet Parse(string json)
        {
            // JsonUtility treats a JSON null list as an empty list on some Unity
            // versions. Reject missing/null history instead of accepting it as
            // a fresh wallet and replaying already delivered purchases.
            JObject data = JObject.Parse(json);
            if (data["version"]?.Type != JTokenType.Integer || data["balance"]?.Type != JTokenType.Integer ||
                data["transactions"]?.Type != JTokenType.Array)
                throw new InvalidOperationException("Incomplete coin wallet.");
            var wallet = data.ToObject<CoinWallet>();
            if (wallet == null || wallet.version != 2 || wallet.balance < 0 || wallet.transactions == null)
                throw new InvalidOperationException("Invalid coin wallet; refusing to reset purchase history.");
            foreach (string transaction in wallet.transactions)
                if (string.IsNullOrWhiteSpace(transaction)) throw new InvalidOperationException("Invalid transaction history.");
            if (wallet.adRewardIds == null || wallet.adRewardCount < 0 || wallet.adRewardDate == null)
                throw new InvalidOperationException("Invalid ad reward history.");
            foreach (string rewardId in wallet.adRewardIds)
                if (string.IsNullOrWhiteSpace(rewardId)) throw new InvalidOperationException("Invalid ad reward ID.");
            return wallet;
        }

        public bool Grant(string transactionId, long amount)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || amount <= 0)
                throw new ArgumentException("A stable transaction ID and positive reward are required.");
            if (transactions.Contains(transactionId)) return false;
            long next = checked(balance + amount);
            transactions.Add(transactionId);
            balance = next;
            return true;
        }

        // 광고 보상 지급. dailyLimit > 0 이면 지급 시점에도 하루 한도를 검사한다
        // (한도를 넘은 영수증은 처리 완료로만 기록하고 코인은 지급하지 않는다 - 재처리 방지).
        // today: 지급 시점의 기기 날짜(yyyyMMdd, 선택). 저장 날짜보다 과거 날짜의 영수증은
        //  - 기기 날짜도 저장 날짜보다 과거면(날짜를 앞당겼다 되돌림 의심) 저장 날짜의 횟수로 센다 - 리셋 우회 차단
        //  - 그 외(지연 수령된 옛 영수증)는 오늘 횟수를 늘리지 않는다 (기존 규칙 유지)
        public bool GrantAdReward(string requestId, long amount, string date, int dailyLimit = 0, string today = null)
        {
            if (string.IsNullOrWhiteSpace(requestId) || amount <= 0 || !TryParseAdDate(date, out _))
                throw new ArgumentException("Invalid ad reward receipt.");
            if (adRewardIds.Contains(requestId)) return false;

            // 저장 날짜가 영수증 날짜보다 (허용 범위 안의) 미래면 저장 날짜를 유지한다
            string effectiveDate = ResolveAdRewardDate(adRewardDate, date);
            bool olderReceipt = effectiveDate != date;
            bool clockBehindStored = olderReceipt && TryParseAdDate(today, out _) &&
                                     ResolveAdRewardDate(adRewardDate, today) == adRewardDate && today != adRewardDate;
            int currentCount = effectiveDate == adRewardDate ? adRewardCount : 0;
            if (dailyLimit > 0 && currentCount >= dailyLimit)
            {
                adRewardIds.Add(requestId);
                return false;
            }
            long next = checked(balance + amount);
            if (!olderReceipt || clockBehindStored)
            {
                adRewardDate = effectiveDate;
                adRewardCount = checked(currentCount + 1);
            }
            adRewardIds.Add(requestId);
            balance = next;
            return true;
        }

        // 저장된 광고 집계 날짜가 기준 날짜(오늘/영수증 날짜)보다 미래면 저장 날짜를 유지하고(리셋 안 함),
        // 과거·무효면 기준 날짜를 쓴다. 단 MaxFutureAdDays 를 넘는 먼 미래 값은 무효로 본다
        // (구버전이 현지 달력(예: 태국 불기 2569년)으로 기록한 날짜가 영구 미래로 잠기는 것을 방지).
        public const int MaxFutureAdDays = 366;

        public static string ResolveAdRewardDate(string storedDate, string referenceDate)
        {
            if (!TryParseAdDate(referenceDate, out DateTime reference)) return storedDate ?? "";
            if (!TryParseAdDate(storedDate, out DateTime stored) || stored <= reference) return referenceDate;
            return (stored - reference).TotalDays <= MaxFutureAdDays ? storedDate : referenceDate;
        }

        public static bool TryParseAdDate(string date, out DateTime value)
        {
            return DateTime.TryParseExact(date, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out value);
        }

        // flush=false 면 PlayerPrefs 에 쓰기만 하고 디스크 반영(PlayerPrefs.Save)은 호출자에게 맡긴다.
        // (PlayerContext.RunAtomic 이 다른 진행 상태와 한 번에 반영할 때 사용)
        public void Save(bool flush = true)
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            // Legacy mirror only. V2 always reads the combined wallet.
            PlayerPrefs.SetInt("ItemAmount_1", (int)Math.Min(balance, int.MaxValue));
            PlayerPrefs.SetString("WatchAdDate", adRewardDate);
            PlayerPrefs.SetInt("WatchAdCount", adRewardCount);
            if (flush) PlayerPrefs.Save();
        }
    }
}
