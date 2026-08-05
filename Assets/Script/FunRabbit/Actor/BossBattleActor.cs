using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 보스 액터. 타겟(ally)이 없으면 정면(스폰 시 방향)을 보며 대기하다가(BossIdleState) 살아있는
    // ally를 찾으면 공격 상태(BossAttackState)로 전환해 그 하나만 공격한다. 타겟이 죽으면 다시
    // Idle로 돌아가 다른 ally를 검색한다.
    public class BossBattleActor : BattleActor
    {
        // 보스 전용 데이터 컨텍스트. actor.json의 boss* 필드(bossHp/bossAttackPower/bossAttackSpeed)를 사용해
        // 같은 animalKey라도 ally보다 훨씬 강하게 만든다. GameQuestManager.MaxBossHp(에너지)도 같은 bossHp 필드를 참조한다.
        public class BossBattleActorContext : BattleActorContext
        {
            public BossBattleActorContext(Actor actor) : base(actor) { }

            protected override int GetHpValue(string animalKey) => GameActorData.GetBossHp(animalKey);
            protected override int GetAttackPowerValue(string animalKey) => GameActorData.GetBossAttackPower(animalKey);
            protected override float GetAttackSpeedValue(string animalKey) => GameActorData.GetBossAttackSpeed(animalKey);
        }

        private const float REST_FACING_DISTANCE = 10f;

        // base(BattleActor)의 Context와 같은 인스턴스를 BossBattleActorContext 타입으로 노출한다 (new로 가림).
        public new BossBattleActorContext Context => (BossBattleActorContext)base.Context;

        // 현재 공격 중인 타겟 (없으면 null - BossIdleState가 검색한다)
        public AllyBattleActor CurrentTarget { get; private set; }

        // 타겟이 없을 때 바라볼 정면 지점 (스폰 시점의 바라보는 방향으로 Setup에서 캡처)
        public Vector3 RestFacingPoint { get; private set; }

        protected override ActorContext CreateContext() => new BossBattleActorContext(this);

        protected override void Awake()
        {
            base.Awake();

            StateMachine.CreateState(
                new BossIdleState(ActorStateType.Idle, this),
                new BossAttackState(ActorStateType.Attack, this)
            );
        }

        // 스폰 직후(ActorBattleSystem이) 호출한다.
        public void Setup(ActorData actorData)
        {
            Context.SetStats(actorData);
            RestFacingPoint = transform.position + transform.forward * REST_FACING_DISTANCE;
            StateMachine.ChangeState(ActorStateType.Idle);
        }

        public void SetTarget(AllyBattleActor target) => CurrentTarget = target;
        public void ClearTarget() => CurrentTarget = null;

        // 살아있는(hp > 0) ally 중 가장 가까운 하나를 찾는다. 씬 전체를 검색(FindObjectsByType)하지
        // 않고 ActorBattleSystem이 이미 슬롯별로 추적 중인 목록을 재사용한다.
        public AllyBattleActor FindLivingAllyTarget()
        {
            if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
                return null;

            IReadOnlyList<AllyBattleActor> allies = battleSystem.GetAllySlotActors();

            AllyBattleActor closest = null;
            float closestDistanceSqr = float.MaxValue;

            for (int i = 0; i < allies.Count; i++)
            {
                AllyBattleActor ally = allies[i];
                if (ally == null || ally.Hp <= 0)
                    continue;

                float distanceSqr = HorizontalDistanceSqr(transform.position, ally.transform.position);
                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closest = ally;
                }
            }

            return closest;
        }

        // y(높이)를 제외한 수평(XZ) 거리의 제곱. 비교(최솟값 찾기) 용도라 sqrt 없이 제곱값끼리 비교한다.
        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
