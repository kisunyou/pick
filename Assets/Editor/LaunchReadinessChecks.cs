using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Firebase.Analytics;
using FunRabbit;
using Unity.Services.LevelPlay;
using UnityEditor;
using UnityEngine;

public static class LaunchReadinessChecks
{
    static int _passed;
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Test(string name, Action action) { action(); _passed++; Debug.Log("[LaunchReadiness] " + name); }
    static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }

    [MenuItem("Tools/Release Safety/Run launch readiness checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _passed = 0;
        Test("Reward after close is delivered", () =>
        {
            int reward=0, failed=0,closed=0;
            var r=new RewardedAdRequest(()=>reward++,()=>failed++,()=>closed++);
            r.Close(); r.Reward();
            Check(reward==1&&failed==0&&closed==1);
        });
        Test("Reward before close and duplicated SDK events deliver once", () =>
        {
            int reward=0,closed=0;
            var r=new RewardedAdRequest(()=>reward++,()=>{throw new Exception();},()=>closed++);
            r.Reward();r.Reward();r.Close();r.Close();
            Check(reward==1&&closed==1);
        });
        Test("Explicit display failure cannot reward", () =>
        {
            int failed=0,reward=0,closed=0;
            var r=new RewardedAdRequest(()=>reward++,()=>failed++,()=>closed++);
            r.Fail();r.Fail();r.Reward();r.Close();
            Check(failed==1&&reward==0&&closed==1);
        });
        Test("Reentrant reward cannot duplicate delivery", () =>
        {
            int count=0;RewardedAdRequest r=null;
            r=new RewardedAdRequest(()=>{count++;r.Reward();},null,null);
            r.Reward();Check(count==1);
        });
        Test("Failed persistence leaves reward retryable", () =>
        {
            int calls=0;var r=new RewardedAdRequest(()=>{if(++calls==1)throw new Exception();},null,null);
            Reject(r.Reward);Check(!r.IsRewarded);r.Reward();Check(r.IsRewarded&&calls==2);
        });
        Test("Late callback is routed to old request, not active newer ad", CheckRequestRouting);
        Test("Bounded exponential retry delay", () =>
        {
            Check(LevelPlayAds.RetryDelay(1)==2&&LevelPlayAds.RetryDelay(2)==4&&LevelPlayAds.RetryDelay(20)==60);
        });
        Test("Ad balance and daily count survive one-record roundtrip", () =>
        {
            var w=new CoinWallet{balance=100};
            Check(w.GrantAdReward("ad-1",500,"20260912"));
            var restored=CoinWallet.Parse(JsonUtility.ToJson(w));
            Check(restored.balance==600&&restored.adRewardCount==1&&restored.adRewardDate=="20260912");
        });
        Test("Replayed write-ahead receipt does not grant again", () =>
        {
            var w=new CoinWallet{balance=100};
            w.GrantAdReward("ad-1",500,"20260912");w.balance-=100;
            var restored=CoinWallet.Parse(JsonUtility.ToJson(w));
            Check(!restored.GrantAdReward("ad-1",500,"20260912"));
            Check(restored.balance==500&&restored.adRewardCount==1);
        });
        Test("New day resets count without erasing receipt history", () =>
        {
            var w=new CoinWallet();w.GrantAdReward("a",500,"20260911");w.GrantAdReward("b",500,"20260912");
            Check(w.balance==1000&&w.adRewardCount==1&&w.adRewardIds.Count==2);
        });
        Test("Delayed older-day reward cannot reset today's count", () =>
        {
            var w=new CoinWallet();w.GrantAdReward("a",500,"20260912");w.GrantAdReward("b",500,"20260911");
            Check(w.balance==1000&&w.adRewardCount==1&&w.adRewardDate=="20260912");
        });
        Test("Ad overflow leaves balance, count and ledger unchanged", () =>
        {
            var w=new CoinWallet{balance=long.MaxValue};
            Reject(()=>w.GrantAdReward("ad",500,"20260912"));
            Check(w.balance==long.MaxValue&&w.adRewardCount==0&&w.adRewardIds.Count==0);
        });
        Test("Old wallets without ad fields remain readable", () =>
        {
            var w=CoinWallet.Parse("{\"version\":2,\"balance\":100,\"transactions\":[]}");
            Check(w.balance==100&&w.adRewardCount==0&&w.adRewardIds!=null);
        });
        Test("Purchase recovery grants missing order exactly once on server wallet", () =>
        {
            var remote=new CoinWallet{balance=700};
            Check(remote.Grant("order",10000));remote=CoinWallet.Parse(JsonUtility.ToJson(remote));
            Check(!remote.Grant("order",10000)&&remote.balance==10700);
        });
        Test("Purchase recovery preserves server wallet if order already exists", () =>
        {
            var remote=new CoinWallet{balance=700};remote.Grant("order",10000);remote.balance-=300;
            Check(!remote.Grant("order",10000)&&remote.balance==10400);
        });
        Test("Once marker remains unset until event send succeeds", () =>
        {
            var sent=new HashSet<string>();int calls=0;
            var q=new AnalyticsEventQueue(sent.Contains,n=>sent.Add(n),(n,p)=>calls++);
            q.Enqueue("first",null,true);q.Enqueue("first",null,true);
            Check(calls==0&&sent.Count==0&&q.Count==1);q.Flush();
            Check(calls==1&&sent.Contains("first"));q.Enqueue("first",null,true);q.Flush();Check(calls==1);
        });
        Test("Failed SDK handoff remains queued without marking once", () =>
        {
            var sent=new HashSet<string>();bool fail=true;
            var q=new AnalyticsEventQueue(sent.Contains,n=>sent.Add(n),(n,p)=>{if(fail)throw new Exception();});
            q.Enqueue("first",null,true);Reject(q.Flush);Check(sent.Count==0&&q.Count==1);
            fail=false;q.Flush();Check(sent.Contains("first")&&q.Count==0);
        });
        Test("Full queue does not reserve or mark rejected once event", () =>
        {
            var sent=new HashSet<string>();
            var q=new AnalyticsEventQueue(sent.Contains,n=>sent.Add(n),(n,p)=>{},1);
            q.Enqueue("ordinary",null);Check(!q.Enqueue("first",null,true));q.Flush();
            Check(q.Enqueue("first",null,true));q.Flush();Check(sent.Contains("first"));
        });
        Test("Ordinary events remain repeatable", () =>
        {
            int calls=0;var q=new AnalyticsEventQueue(n=>false,n=>{},(n,p)=>calls++);
            q.Enqueue("play",null);q.Enqueue("play",null);q.Flush();Check(calls==2);
        });
        Test("Stage number localizations format actual stage", () =>
        {
            var table=JsonUtility.FromJson<StringDataList>(File.ReadAllText("Assets/Resources/Table/stringData.json"));
            var row=table.stringData.Single(r=>r.key=="hud_stage_number");
            foreach(var text in new[]{row.kor,row.eng,row.jpn,row.tha})
                Check(string.Format(text,18).Contains("18"));
        });
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/launch_readiness_result.txt","PASS: "+_passed+" checks; no real ads, purchases, Firebase requests or player-save writes.\n");
        Debug.Log("LAUNCH_READINESS_CHECKS_PASSED="+_passed);
    }

    static void CheckRequestRouting()
    {
        var go=new GameObject("Ad routing regression");
        try
        {
            var ads=go.AddComponent<LevelPlayAds>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var requests=(Dictionary<string,RewardedAdRequest>)typeof(LevelPlayAds).GetField("_requests",flags).GetValue(ads);
            int a=0,b=0;
            var old=new RewardedAdRequest(()=>a++,null,null);old.Close();
            var current=new RewardedAdRequest(()=>b++,null,null);
            requests.Add("auction-a",old);requests.Add("auction-b",current);
            typeof(LevelPlayAds).GetField("_activeRequest",flags).SetValue(ads,current);
            var ctor=typeof(LevelPlayAdInfo).GetConstructor(flags,null,new[]{typeof(string)},null);
            var find=typeof(LevelPlayAds).GetMethod("FindRequest",flags);
            var info=ctor.Invoke(new object[]{"{\"auctionId\":\"auction-a\"}"});
            ((RewardedAdRequest)find.Invoke(ads,new[]{info})).Reward();
            Check(a==1&&b==0);
            var unknown=ctor.Invoke(new object[]{"{\"auctionId\":\"unknown\"}"});
            Check(find.Invoke(ads,new[]{unknown})==null);
        }
        finally { UnityEngine.Object.DestroyImmediate(go);LevelPlayAds.ClearVariablesSingleton(); }
    }
}
