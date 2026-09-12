using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FunRabbit;
using UnityEditor;
using UnityEngine;

public static class GameplayAnalyticsChecks
{
    static int _passed;
    static void Require(bool condition) { if (!condition) throw new Exception("Gameplay analytics assertion failed."); }
    static void Test(string name, Action action) { action(); _passed++; Debug.Log("[GameplayAnalyticsChecks] " + name); }

    [MenuItem("Tools/Release Safety/Run gameplay analytics checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _passed = 0;
        Test("Attempt ID and stage survive through successful result", () =>
        {
            var t = new CraneAttemptMetrics();
            var start = t.Begin("attempt-a",18,300,200);
            t.Tick(2,false);t.Collect(10);
            var end = t.End(250,false);
            Require(start.id==end.id&&end.stage==18&&end.result=="success"&&end.coinsBefore==300&&end.coinsAfter==250);
        });
        Test("No collection produces failure", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,100,0);
            Require(t.End(0,false).result=="failure");
        });
        Test("Duplicate starts cannot reset paid attempt state", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,200,100);t.Collect(1);
            Require(t.Begin("b",2,100,0)==null);
            var end=t.End(100,false);Require(end.id=="a"&&end.stage==1&&end.collected==1);
        });
        Test("Repeated end emits no second result", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,100,0);
            Require(t.End(0,false)!=null&&t.End(0,false)==null&&!t.Active);
        });
        Test("Duplicate actor callbacks count once", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,100,0);
            Require(t.Collect(15)&&!t.Collect(15)&&t.Collect(16));
            Require(t.End(0,false).collected==2);
        });
        Test("Collection outside paid attempt is not attributed", () =>
        {
            var t=new CraneAttemptMetrics();Require(!t.Collect(1));
            t.Begin("a",1,100,0);t.End(0,false);Require(!t.Collect(1));
        });
        Test("Background and invalid frame durations are excluded", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,100,0);
            t.Tick(2,false);t.Tick(120,true);t.Tick(3,false);
            t.Tick(double.NaN,false);t.Tick(double.PositiveInfinity,false);t.Tick(-1,false);
            Require(t.End(0,false).playSeconds==5);
        });
        Test("Interruption does not masquerade as completed success", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,100,0);t.Collect(1);
            Require(t.End(0,true).result=="interrupted");
        });
        Test("Next attempt clears collection IDs and duration", () =>
        {
            var t=new CraneAttemptMetrics();t.Begin("a",1,200,100);t.Collect(1);t.Tick(3,false);t.End(100,false);
            t.Begin("b",2,100,0);Require(t.Collect(1));var end=t.End(0,false);
            Require(end.id=="b"&&end.stage==2&&end.playSeconds==0&&end.collected==1);
        });
        Test("Start snapshot is immutable under subsequent progress", () =>
        {
            var t=new CraneAttemptMetrics();var start=t.Begin("a",1,100,0);
            t.Tick(2,false);t.Collect(1);Require(start.playSeconds==0&&start.collected==0&&start.result=="started");
        });
        Test("Distinct grants share event name but retain independent local deduplication", () =>
        {
            var marked=new HashSet<string>();var sent=new List<string>();
            var q=new AnalyticsEventQueue(marked.Contains,k=>marked.Add(k),(name,p)=>sent.Add(name));
            q.Enqueue("currency_earned",null,true,"private-grant-a");
            q.Enqueue("currency_earned",null,true,"private-grant-a");
            q.Enqueue("currency_earned",null,true,"private-grant-b");
            Require(q.Count==2);q.Flush();
            Require(sent.SequenceEqual(new[]{"currency_earned","currency_earned"}));
            Require(!q.Enqueue("currency_earned",null,true,"private-grant-a"));
            Require(marked.SetEquals(new[]{"private-grant-a","private-grant-b"}));
        });
        Test("Failed handoff retains local grant key without sending it", () =>
        {
            bool fail=true;var marks=new HashSet<string>();
            var q=new AnalyticsEventQueue(marks.Contains,k=>marks.Add(k),(name,p)=>{Require(name=="currency_earned");if(fail)throw new Exception();});
            q.Enqueue("currency_earned",null,true,"private");
            try{q.Flush();}catch{}
            Require(q.Count==1&&marks.Count==0);fail=false;q.Flush();Require(marks.Contains("private"));
        });
        Test("Only approved gameplay field names occur in outgoing payload factories", () =>
        {
            var allowed=new HashSet<string>{"attempt_id","stage","result","play_seconds","collected_count",
                "coins_before","coins_after","animal","coins","amount","balance"};
            string source=File.ReadAllText("Assets/Script/FunRabbit/MMPService/GameplayAnalytics.cs");
            var matches=Regex.Matches(source, @"new Parameter\(""([^""]+)""");
            Require(matches.Count>0);
            foreach(Match m in matches)Require(allowed.Contains(m.Groups[1].Value));
        });
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/gameplay_analytics_result.txt", "PASS: "+_passed+" checks; fake sinks only; no Firebase events or player-save writes.\n");
        Debug.Log("GAMEPLAY_ANALYTICS_CHECKS_PASSED="+_passed);
    }
}
