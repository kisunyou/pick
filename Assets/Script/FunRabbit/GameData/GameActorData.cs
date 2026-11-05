using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 액터 속성 (JSON table/actor 한 행 = 스테이지 하나의 액터).
    // 연속 28스테이지(2026-11-05 8종 제외, 28판 후 1판부터 반복): 원본 12종과 색 변형. 제작을 마친 변형 행부터 신규 동물로 교체한다.
    // 교체된 행의 구 세이브 키는 legacyKey 로 신규 동물에 연결된다.
    // 스테이지 목록(GameQuestData)도 이 테이블에서 구성된다 - actor.json이 스테이지 구성의 단일 정본.
    [System.Serializable]
    public class ActorData
    {
        public int stage;                    // 이 액터가 보스로 등장하는 스테이지 번호 (1~28)
        public string animalKey;             // 고유 키 (동물 이름, 예: bear / sheep / deer)
        public string nameKey;               // 표시 이름의 stringData 키 (변형 행도 원본 이름 키를 기입, 예: doll_name_bear)
        public string model;                 // 모델 프리팹 경로 (Resources 기준). 비어있으면 기존 이름 규칙 사용
        public string texture;               // 교체 텍스처 경로 (Resources 기준). 비어있으면 모델 원본 텍스처 유지
        public string sound;                 // 획득 시 울음소리 (Resources/Sound 기준 경로)
        public string legacyKey;             // 이 행이 대체한 예전 키 (예: cow_b, cow_r). 구 세이브의 키를 이 행으로 연결
        public float inGameScale = 1f;       // 인게임(뽑기 기계) 인형 스케일
        public float collectionScale = 1.5f; // 컬렉션(도감) 배회 인형 스케일

        // ally(전투 지원 인형)로 스폰될 때 쓰는 스탯
        public int allyHp = 1000;
        public int allyAttackPower = 10;
        public float allyAttackSpeed = 1f;

        // boss(스테이지 보스 몬스터)로 스폰될 때 쓰는 스탯 (같은 animalKey라도 ally보다 훨씬 강함)
        public int bossHp = 1000;
        public int bossAttackPower = 10;
        public float bossAttackSpeed = 1f;
        public int clearCoinReward;

        public float attackRange = 1f;       // 공격 사거리 (반경, ally/보스 공용)

        // 공격 시 공격자-타겟 사이에 재생할 이펙트 / 피격 시 타겟 위치에 재생할 이펙트 (Resources 경로, ally/보스 공용)
        public string attackFx = "";
        public string hitFx = "";
    }

    [System.Serializable]
    public class ActorDataList
    {
        public List<ActorData> actors;
    }

    public class GameActorData
    {
        // 테이블에 항목이 없을 때의 기본값
        const float DEFAULT_INGAME_SCALE = 1f;
        const float DEFAULT_COLLECTION_SCALE = 1.5f;
        const int DEFAULT_ALLY_HP = 200;
        const int DEFAULT_ALLY_ATTACK_POWER = 8;
        const float DEFAULT_ALLY_ATTACK_SPEED = 1f;
        const int DEFAULT_BOSS_HP = 1200;
        const int DEFAULT_BOSS_ATTACK_POWER = 30;
        const float DEFAULT_BOSS_ATTACK_SPEED = 1.6f;
        const float DEFAULT_ATTACK_RANGE = 1f;

        private static ActorDataList _dataList;

        public static void Load()
        {
            TextAsset json = Resources.Load<TextAsset>("table/actor");
            if (json == null)
            {
                Debug.LogError("[GameActorData] actor.json not found in Resources/");
                return;
            }

            _dataList = JsonUtility.FromJson<ActorDataList>(json.text);
            Debug.Log($"[GameActorData] Loaded {_dataList.actors.Count} actors.");
        }

        // 2026-11-05 스테이지에서 뺀 동물(햄스터·토끼·악어, 펭귄·거북이·부엉이, 오징어·해파리)과 그 자리의 구 변형 키 -> 같은 계열 원본 동물 (구 세이브의 아군·배치·미션 보존)
        static readonly Dictionary<string, string> RetiredKeys = new Dictionary<string, string>
        {
            { "hamster", "koala" }, { "rabbit", "koala" }, { "crocodile", "frog" },
            { "koala_b", "koala" }, { "koala_r", "koala" }, { "frog_r", "frog" },
            { "penguin", "duck" }, { "owl", "duck" }, { "turtle", "frog" },
            { "duck_b", "duck" }, { "duck_r", "duck" }, { "frog_b", "frog" },
            { "squid", "octopus" }, { "jellyfish", "octopus" }, { "octopus_b", "octopus" }, { "octopus_r", "octopus" },
        };

        // Older local/cloud saves still contain the retired green-variant keys.
        public static string ResolveAnimalKey(string animalKey)
        {
            if (!string.IsNullOrEmpty(animalKey) && animalKey.EndsWith("_g", System.StringComparison.Ordinal))
                animalKey = animalKey.Substring(0, animalKey.Length - 2) + "_b";
            if (!string.IsNullOrEmpty(animalKey) && RetiredKeys.TryGetValue(animalKey, out string retiredTo))
                return retiredTo;
            // 구 변형 키(cow_b / cow_r 등) -> 그 자리를 대체한 신규 동물 키
            if (!string.IsNullOrEmpty(animalKey) && (animalKey.EndsWith("_b", System.StringComparison.Ordinal) || animalKey.EndsWith("_r", System.StringComparison.Ordinal)))
            {
                if (_dataList == null)
                    Load();
                var row = _dataList?.actors.Find(a => a.legacyKey == animalKey);
                if (row != null)
                    return row.animalKey;
            }
            return animalKey;
        }

        public static ActorData Get(string animalKey)
        {
            if (_dataList == null)
                Load();

            animalKey = ResolveAnimalKey(animalKey);
            return _dataList?.actors.Find(a => a.animalKey == animalKey);
        }

        // 전체 액터 행 (스테이지 구성용 - GameQuestData가 이 목록으로 스테이지를 만든다)
        public static List<ActorData> Actors
        {
            get
            {
                if (_dataList == null)
                    Load();

                return _dataList?.actors;
            }
        }

        // 해당 스테이지의 액터 행 (stage 필드 기준)
        public static ActorData GetByStage(int stage)
        {
            if (_dataList == null)
                Load();

            return _dataList?.actors.Find(a => a.stage == stage);
        }

        // animalKey에 해당하는 획득 울음소리 경로(Resources/Sound 기준)를 반환한다. 없으면 null.
        public static string GetSound(string animalKey)
        {
            return Get(animalKey)?.sound;
        }

        // 인게임(뽑기 기계) 인형 스케일. 테이블에 없으면 기본값.
        public static float GetInGameScale(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.inGameScale : DEFAULT_INGAME_SCALE;
        }

        // 컬렉션(도감) 배회 인형 스케일. 테이블에 없으면 기본값.
        public static float GetCollectionScale(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.collectionScale : DEFAULT_COLLECTION_SCALE;
        }

        // ally 체력. 테이블에 없으면 기본값.
        public static int GetAllyHp(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.allyHp : DEFAULT_ALLY_HP;
        }

        // ally 공격력. 테이블에 없으면 기본값.
        public static int GetAllyAttackPower(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.allyAttackPower : DEFAULT_ALLY_ATTACK_POWER;
        }

        // ally 공격 속도. 테이블에 없으면 기본값.
        public static float GetAllyAttackSpeed(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.allyAttackSpeed : DEFAULT_ALLY_ATTACK_SPEED;
        }

        // 누락된 보상 수치는 기존 500코인을 유지한다.
        public static int GetClearCoinReward(string animalKey)
        {
            int reward = Get(animalKey)?.clearCoinReward ?? 0;
            return reward > 0 ? reward : 500;
        }

        // 보스 체력(에너지). 테이블에 없으면 기본값.
        public static int GetBossHp(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.bossHp : DEFAULT_BOSS_HP;
        }

        // 보스 공격력. 테이블에 없으면 기본값.
        public static int GetBossAttackPower(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.bossAttackPower : DEFAULT_BOSS_ATTACK_POWER;
        }

        // 보스 공격 속도. 테이블에 없으면 기본값.
        public static float GetBossAttackSpeed(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.bossAttackSpeed : DEFAULT_BOSS_ATTACK_SPEED;
        }

        // 공격 사거리(반경). 테이블에 없으면 기본값.
        public static float GetAttackRange(string animalKey)
        {
            ActorData data = Get(animalKey);
            return data != null ? data.attackRange : DEFAULT_ATTACK_RANGE;
        }

        // 공격 이펙트 경로 (Resources 기준). 테이블에 없거나 비어있으면 null.
        public static string GetAttackFx(string animalKey)
        {
            string fx = Get(animalKey)?.attackFx;
            return string.IsNullOrEmpty(fx) ? null : fx;
        }

        // 피격 이펙트 경로 (Resources 기준). 테이블에 없거나 비어있으면 null.
        public static string GetHitFx(string animalKey)
        {
            string fx = Get(animalKey)?.hitFx;
            return string.IsNullOrEmpty(fx) ? null : fx;
        }
    }
}
