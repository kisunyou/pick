using System;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // Write-ahead receipts survive process termination and late callbacks after account switching.
    public static class AdRewardInbox
    {
        [Serializable] public sealed class Receipt { public string id; public long amount; public string date; }
        [Serializable] sealed class Inbox { public List<Receipt> receipts = new List<Receipt>(); }
        const string Prefix = "AdRewardInbox_";

        public static string CurrentOwner => FireBaseAuthManager.IsCheckInstance() &&
            FireBaseAuthManager.Instance.IsLoggedIn ? FireBaseAuthManager.Instance.UserId : "local";

        public static void Enqueue(string owner, string id, long amount)
        {
            string key = Prefix + owner;
            var inbox = PlayerPrefs.HasKey(key) ? JsonUtility.FromJson<Inbox>(PlayerPrefs.GetString(key)) : new Inbox();
            if (inbox == null || inbox.receipts == null) throw new InvalidOperationException("Invalid ad reward inbox.");
            if (inbox.receipts.Exists(r => r.id == id)) return;
            inbox.receipts.Add(new Receipt { id = id, amount = amount,
                date = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) });
            PlayerPrefs.SetString(key, JsonUtility.ToJson(inbox));
            PlayerPrefs.Save();
        }

        public static long ClaimCurrent()
        {
            if (SessionOperation.IsBusy) return 0;
            string key = Prefix + CurrentOwner;
            if (!PlayerPrefs.HasKey(key)) return 0;
            var inbox = JsonUtility.FromJson<Inbox>(PlayerPrefs.GetString(key));
            if (inbox == null || inbox.receipts == null) throw new InvalidOperationException("Invalid ad reward inbox.");
            long total = 0;
            foreach (var receipt in inbox.receipts)
                if (PlayerContext.GrantAdRewardOnce(receipt.id, receipt.amount, receipt.date))
                    total = checked(total + receipt.amount);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return total;
        }
    }
}
