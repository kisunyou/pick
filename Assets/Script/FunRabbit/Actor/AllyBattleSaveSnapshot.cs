using System;

namespace FunRabbit
{
    // Read the complete saved army before spawning can trigger any writes.
    public sealed class AllyBattleSaveSnapshot
    {
        public const int MissingHp = int.MaxValue;
        public readonly string[] AnimalKeys;
        public readonly int[] HitPoints;
        public readonly string[] PendingAnimalKeys;

        private AllyBattleSaveSnapshot(string[] keys, int[] hp, string[] pending)
        {
            AnimalKeys = keys;
            HitPoints = hp;
            PendingAnimalKeys = pending;
        }

        public static AllyBattleSaveSnapshot Read(int slotCount,
            Func<string, string, string> getString, Func<string, int, int> getInt)
        {
            var keys = new string[slotCount];
            var hp = new int[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                keys[i] = getString("AllySlotAnimalKey" + i, string.Empty);
                hp[i] = getInt("AllySlotHp" + i, MissingHp);
            }
            string queue = getString("AllyPendingQueue", string.Empty);
            return new AllyBattleSaveSnapshot(keys, hp,
                queue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
