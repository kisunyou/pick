using System;

namespace FunRabbit
{
    // Closing releases the presentation, not the independently delivered reward.
    public sealed class RewardedAdRequest
    {
        Action _reward;
        Action _failed;
        Action _closed;
        public bool IsClosed { get; private set; }
        public bool IsRewarded { get; private set; }
        public bool IsFailed { get; private set; }

        public RewardedAdRequest(Action reward, Action failed, Action closed)
        {
            _reward = reward; _failed = failed; _closed = closed;
        }

        public void Reward()
        {
            if (IsRewarded || IsFailed) return;
            IsRewarded = true;
            try { _reward?.Invoke(); }
            catch { IsRewarded = false; throw; }
            _reward = null;
            _failed = null;
        }

        public void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            _closed?.Invoke();
            _closed = null;
        }

        public void Fail()
        {
            if (IsFailed || IsRewarded) return;
            IsFailed = true;
            try { _failed?.Invoke(); }
            finally { _failed = null; _reward = null; Close(); }
        }
    }
}
