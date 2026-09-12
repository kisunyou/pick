using System;
using Firebase.Analytics;
using UnityEngine;

namespace FunRabbit
{
    public static class GameplayAnalytics
    {
        static readonly CraneAttemptMetrics Attempt = new CraneAttemptMetrics();
        static bool _paused;
        static bool _skipResumeFrame;
        static int _lastStage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Attempt.End(0, true);
            _paused = false;
            _skipResumeFrame = false;
            _lastStage = 0;
        }

        // Analytics failure must never undo or interrupt an already committed gameplay operation.
        static void Safely(Action<FireBaseAnalyticsManager> action)
        {
            if (!Application.isPlaying || FireBaseAnalyticsManager.IsQuitting || !FireBaseAnalyticsManager.IsCheckInstance()) return;
            try { action(FireBaseAnalyticsManager.Instance); }
            catch (Exception e) { Debug.LogWarning("[GameplayAnalytics] Event not recorded: " + e.Message); }
        }

        static int Stage => GameQuestManager.IsCheckInstance() ? GameQuestManager.Instance.CurrentStage : 1;

        public static Parameter[] AttemptParameters(CraneAttemptMetrics.Snapshot snapshot) => new[]
        {
            new Parameter("attempt_id", snapshot.id),
            new Parameter("stage", (long)snapshot.stage),
            new Parameter("result", snapshot.result),
            new Parameter("play_seconds", snapshot.playSeconds),
            new Parameter("collected_count", (long)snapshot.collected),
            new Parameter("coins_before", snapshot.coinsBefore),
            new Parameter("coins_after", snapshot.coinsAfter)
        };

        public static void BeginAttempt(int stage, long before, long after)
        {
            if (!Application.isPlaying) return;
            StageStarted(stage);
            var snapshot = Attempt.Begin(Guid.NewGuid().ToString("N"), stage, before, after);
            if (snapshot != null) Safely(a => a.LogEvent("crane_attempt_start", AttemptParameters(snapshot)));
        }

        public static void Tick(float seconds)
        {
            if (_skipResumeFrame) { _skipResumeFrame = false; return; }
            Attempt.Tick(seconds, _paused || Time.timeScale <= 0f);
        }
        public static void SetPaused(bool paused)
        {
            _paused = paused;
            if (!paused) _skipResumeFrame = true;
        }

        public static void Collected(int actorId, string animalKey, bool isRandomBox)
        {
            if (!Application.isPlaying || !Attempt.Collect(actorId)) return;
            var snapshot = Attempt.Copy();
            Safely(a =>
            {
                var parameters = new[]
                {
                    new Parameter("attempt_id", snapshot.id), new Parameter("stage", (long)snapshot.stage),
                    new Parameter("animal", isRandomBox ? "random_box" : animalKey ?? "unknown"),
                    new Parameter("play_seconds", snapshot.playSeconds),
                    new Parameter("coins", PlayerContext.GetItemAmount(PlayerContext.COIN_ITEM_KEY))
                };
                a.LogEvent("doll_collected", parameters);
                if (!isRandomBox && !string.IsNullOrEmpty(animalKey))
                    a.LogEventOnce("first_doll_collected", parameters);
            });
        }

        public static void EndAttempt(bool interrupted = false)
        {
            var snapshot = Attempt.End(PlayerContext.GetItemAmount(PlayerContext.COIN_ITEM_KEY), interrupted);
            if (snapshot != null) Safely(a => a.LogEvent("crane_attempt_end", AttemptParameters(snapshot)));
        }

        public static void StageStarted(int stage)
        {
            if (!Application.isPlaying || stage == _lastStage) return;
            EndAttempt(true);
            _lastStage = stage;
            Safely(a => a.LogEvent("stage_start", new Parameter("stage", (long)stage),
                new Parameter("coins", PlayerContext.GetItemAmount(PlayerContext.COIN_ITEM_KEY))));
        }

        public static void CoinsChanged(long delta, long balance, string localGrantId = null)
        {
            if (delta == 0) return;
            Safely(a =>
            {
                var parameters = new[]
                {
                    new Parameter("attempt_id", Attempt.AttemptId), new Parameter("stage", (long)Stage),
                    new Parameter("amount", delta > 0 ? delta : checked(-delta)),
                    new Parameter("balance", balance)
                };
                string name = delta > 0 ? "currency_earned" : "currency_spent";
                if (localGrantId == null) a.LogEvent(name, parameters);
                else a.LogEventOnceForKey(name, "coin_grant_" + localGrantId, parameters);
            });
        }
    }
}
