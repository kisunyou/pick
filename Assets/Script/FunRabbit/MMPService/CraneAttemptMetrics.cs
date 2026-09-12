using System;
using System.Collections.Generic;

namespace FunRabbit
{
    // Pure attempt state, independent of Unity, Firebase and player storage.
    public sealed class CraneAttemptMetrics
    {
        public sealed class Snapshot
        {
            public string id;
            public int stage;
            public long coinsBefore, coinsAfter;
            public int collected;
            public double playSeconds;
            public string result;
        }

        readonly HashSet<int> _collectedActors = new HashSet<int>();
        Snapshot _current;
        public bool Active => _current != null;
        public string AttemptId => _current?.id ?? "";

        public Snapshot Begin(string id, int stage, long before, long after)
        {
            if (Active) return null;
            if (string.IsNullOrEmpty(id) || stage < 1 || before < 0 || after < 0)
                throw new ArgumentException("Invalid attempt.");
            _collectedActors.Clear();
            _current = new Snapshot { id = id, stage = stage, coinsBefore = before, coinsAfter = after, result = "started" };
            return Copy();
        }

        public void Tick(double seconds, bool paused)
        {
            if (Active && !paused && seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                _current.playSeconds += seconds;
        }

        public bool Collect(int actorId)
        {
            if (!Active || !_collectedActors.Add(actorId)) return false;
            _current.collected++;
            return true;
        }

        public Snapshot Copy()
        {
            if (!Active) return null;
            return new Snapshot { id = _current.id, stage = _current.stage, coinsBefore = _current.coinsBefore,
                coinsAfter = _current.coinsAfter, collected = _current.collected, playSeconds = _current.playSeconds,
                result = _current.result };
        }

        public Snapshot End(long balance, bool interrupted)
        {
            if (!Active) return null;
            var result = Copy();
            result.coinsAfter = balance;
            result.result = interrupted ? "interrupted" : result.collected > 0 ? "success" : "failure";
            _current = null;
            _collectedActors.Clear();
            return result;
        }
    }
}
