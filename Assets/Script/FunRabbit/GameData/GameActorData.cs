using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 동물(액터) 공통 속성 (JSON table/actor 한 행 = 동물 한 종).
    // 스테이지 진행 규칙은 GameQuestData(quest.json)가, 동물 자체 속성은 여기가 담당한다.
    [System.Serializable]
    public class ActorData
    {
        public string animalKey;
        public string sound;                 // 획득 시 울음소리 (Resources/Sound 기준 경로)
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

        public static ActorData Get(string animalKey)
        {
            if (_dataList == null)
                Load();

            return _dataList?.actors.Find(a => a.animalKey == animalKey);
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
