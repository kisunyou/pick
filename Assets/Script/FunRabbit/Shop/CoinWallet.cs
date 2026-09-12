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

        public bool GrantAdReward(string requestId, long amount, string date)
        {
            if (string.IsNullOrWhiteSpace(requestId) || amount <= 0 ||
                !DateTime.TryParseExact(date, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                throw new ArgumentException("Invalid ad reward receipt.");
            if (adRewardIds.Contains(requestId)) return false;
            long next = checked(balance + amount);
            int nextCount = date == adRewardDate ? checked(adRewardCount + 1) : 1;
            if (string.CompareOrdinal(date, adRewardDate) >= 0)
            {
                adRewardDate = date;
                adRewardCount = nextCount;
            }
            adRewardIds.Add(requestId);
            balance = next;
            return true;
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            // Legacy mirror only. V2 always reads the combined wallet.
            PlayerPrefs.SetInt("ItemAmount_1", (int)Math.Min(balance, int.MaxValue));
            PlayerPrefs.SetString("WatchAdDate", adRewardDate);
            PlayerPrefs.SetInt("WatchAdCount", adRewardCount);
            PlayerPrefs.Save();
        }
    }
}
