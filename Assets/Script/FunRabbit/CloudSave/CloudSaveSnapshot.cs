using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // PlayerPrefs에 산재한 게임 진행 데이터를 하나의 스냅샷으로 묶는 직렬화 계층.
    // - Capture(): 현재 PlayerPrefs → 스냅샷
    // - ApplyToPlayerPrefs(): 스냅샷 → PlayerPrefs (관리 대상 키를 전부 지운 뒤 기록)
    // 기기별 설정(볼륨/진동/언어)과 분석/알림 북키핑 키는 클라우드 동기화 대상에서 제외한다.
    [System.Serializable]
    public class CloudSaveSnapshot
    {
        [System.Serializable] public class IntEntry { public string k; public int v; }
        [System.Serializable] public class FloatEntry { public string k; public float v; }
        [System.Serializable] public class StringEntry { public string k; public string v; }

        public int version = 1;
        public List<IntEntry> ints = new List<IntEntry>();
        public List<FloatEntry> floats = new List<FloatEntry>();
        public List<StringEntry> strings = new List<StringEntry>();

        // ===== 관리 대상 키 목록 =====
        // 고정 int 키: 스테이지/보스HP(GameQuestManager), 아이템 수량(PlayerContext),
        //              게이지/랜덤박스, 일일 광고 시청 횟수
        static readonly string[] IntKeys =
        {
            "currentStage", "bossHp", "maxClearedStage",
            "ItemAmount_1", "ItemAmount_8", "ItemAmount_9", "ItemAmount_10",
            "DollCountGage", "RandomBoxCount",
            "WatchAdCount",
            "MissionKey", "MissionProgress",
        };

        static readonly string[] FloatKeys =
        {
            "RandomBoxProgress",
        };

        // 고정 string 키: 지급 대기 아군 보상, 아군 대기열, 광고 시청 날짜, 코인 타이머 종료 시각, 미션 대상 동물
        static readonly string[] StringKeys =
        {
            "PendingAllyRewards",
            "AllyPendingQueue",
            "WatchAdDate",
            "CoinTimerEndTimeUtc",
            "MissionAnimalKey",
        };

        // 동적 키: 아군 슬롯(ActorBattleSystem), 스테이지별 인형 배치(StageManager)
        const string AllySlotAnimalKeyPrefix = "AllySlotAnimalKey"; // + index (string)
        const string AllySlotHpPrefix = "AllySlotHp";               // + index (int)
        const int MaxAllySlots = 16;

        const string StageDataPrefix = "StageData_";                // + stage (json string)
        const int MaxStageScan = 200;                               // 스테이지 수 상한 (여유값)

        // 현재 PlayerPrefs 상태를 스냅샷으로 캡처한다 (존재하는 키만 담는다).
        public static CloudSaveSnapshot Capture()
        {
            var snapshot = new CloudSaveSnapshot();

            foreach (string key in IntKeys)
            {
                if (PlayerPrefs.HasKey(key))
                    snapshot.ints.Add(new IntEntry { k = key, v = PlayerPrefs.GetInt(key) });
            }

            foreach (string key in FloatKeys)
            {
                if (PlayerPrefs.HasKey(key))
                    snapshot.floats.Add(new FloatEntry { k = key, v = PlayerPrefs.GetFloat(key) });
            }

            foreach (string key in StringKeys)
            {
                if (PlayerPrefs.HasKey(key))
                    snapshot.strings.Add(new StringEntry { k = key, v = PlayerPrefs.GetString(key) });
            }

            for (int i = 0; i < MaxAllySlots; i++)
            {
                string animalKey = AllySlotAnimalKeyPrefix + i;
                if (PlayerPrefs.HasKey(animalKey))
                    snapshot.strings.Add(new StringEntry { k = animalKey, v = PlayerPrefs.GetString(animalKey) });

                string hpKey = AllySlotHpPrefix + i;
                if (PlayerPrefs.HasKey(hpKey))
                    snapshot.ints.Add(new IntEntry { k = hpKey, v = PlayerPrefs.GetInt(hpKey) });
            }

            int stageScan = Mathf.Min(Mathf.Max(GameQuestManager.TotalPlayableStageCount, 1) + 1, MaxStageScan);
            for (int stage = 1; stage <= stageScan; stage++)
            {
                string key = StageDataPrefix + stage;
                if (PlayerPrefs.HasKey(key))
                    snapshot.strings.Add(new StringEntry { k = key, v = PlayerPrefs.GetString(key) });
            }

            return snapshot;
        }

        // 스냅샷을 PlayerPrefs에 적용한다. 관리 대상 키를 전부 지운 뒤 기록해
        // 스냅샷에 없는 키(빈 아군 슬롯 등)가 로컬 잔존값으로 남지 않게 한다.
        public void ApplyToPlayerPrefs()
        {
            ClearManagedKeys();

            foreach (IntEntry e in ints)
                PlayerPrefs.SetInt(e.k, e.v);
            foreach (FloatEntry e in floats)
                PlayerPrefs.SetFloat(e.k, e.v);
            foreach (StringEntry e in strings)
                PlayerPrefs.SetString(e.k, e.v);

            PlayerPrefs.Save();
        }

        private static void ClearManagedKeys()
        {
            foreach (string key in IntKeys)
                PlayerPrefs.DeleteKey(key);
            foreach (string key in FloatKeys)
                PlayerPrefs.DeleteKey(key);
            foreach (string key in StringKeys)
                PlayerPrefs.DeleteKey(key);

            for (int i = 0; i < MaxAllySlots; i++)
            {
                PlayerPrefs.DeleteKey(AllySlotAnimalKeyPrefix + i);
                PlayerPrefs.DeleteKey(AllySlotHpPrefix + i);
            }

            for (int stage = 1; stage <= MaxStageScan; stage++)
                PlayerPrefs.DeleteKey(StageDataPrefix + stage);
        }

        // ===== 조회 헬퍼 =====

        public bool TryGetInt(string key, out int value)
        {
            foreach (IntEntry e in ints)
            {
                if (e.k == key)
                {
                    value = e.v;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        public int GetInt(string key, int defaultValue)
        {
            return TryGetInt(key, out int value) ? value : defaultValue;
        }
    }
}
