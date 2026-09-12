using System;

namespace FunRabbit
{
    public static class BossHpBalance
    {
        // Last table before bossHpMax was saved. Never replace these with a later balance table.
        private static readonly int[] LegacyMaxHp =
        {
            410, 610, 1800, 2200, 2750, 3250, 4100, 4850, 6200, 6850, 8200, 9950,
            820, 1220, 3600, 4400, 5500, 6500, 8200, 9700, 12400, 13700, 16400, 19900,
            1230, 1830, 5400, 6600, 8250, 9750, 12300, 14550, 18600, 20550, 24600, 29850
        };

        public static int ResolvePreviousMax(int stage, int savedMax, int currentMax)
        {
            if (savedMax > 0) return savedMax;
            return stage >= 1 && stage <= LegacyMaxHp.Length ? LegacyMaxHp[stage - 1] : currentMax;
        }

        public static int RescaleRemaining(int remaining, int previousMax, int currentMax)
        {
            if (remaining <= 0 || currentMax <= 0) return 0;
            if (previousMax <= 0) return Math.Min(remaining, currentMax);
            long clamped = Math.Min(remaining, previousMax);
            // Round up so a living boss never becomes a free clear after a downscale.
            return (int)((clamped * currentMax + previousMax - 1L) / previousMax);
        }
    }
}
