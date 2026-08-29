using UnityEngine;

namespace FunRabbit
{
    // ally 액터. 보스에게 접근(Move) -> 공방(Attack) -> hp 0 시 죽음(Dead) 순으로 상태가 전이한다.
    // 보스가 클리어돼 다음 보스를 기다리는 동안은 Idle(대기)로 멈춰 있다가(EnterWaiting), 새 보스가
    // 등장하면 다시 Move 로 돌아가 새 사거리에 맞춰 위치를 잡고 공격한다(ResumeBattle).
    public class AllyBattleActor : BattleActor
    {
        // ally 전용 데이터 컨텍스트. BattleActorContext(hp/공격 스탯)에 보스 타겟 참조를 더한다.
        public class AllyBattleActorContext : BattleActorContext
        {
            public Transform BossTransform { get; set; }

            public AllyBattleActorContext(Actor actor) : base(actor) { }
        }

        // base(BattleActor)의 Context와 같은 인스턴스를 AllyBattleActorContext 타입으로 노출한다 (new로 가림).
        public new AllyBattleActorContext Context => (AllyBattleActorContext)base.Context;

        public Transform BossTransform => Context.BossTransform;

        // 머리 위에 떠서 따라다니는 hp 게이지. 스폰 시엔 만들지 않고, 처음 공격받을 때(OnHpChanged)
        // 그제서야 생성해 보여준다 (headSocket이 없어지면 자동으로 닫힌다).
        private UIActorHPGage _hpGageView;

        protected override ActorContext CreateContext() => new AllyBattleActorContext(this);
        protected override Color GizmoColor => Color.yellow;

        protected override void Awake()
        {
            base.Awake();

            StateMachine.CreateState(
                new ActorIdleState(ActorStateType.Idle, this),   // 보스 교체 대기 (GetIdleDuration 기본 무한 → 계속 대기)
                new ActorMoveState(ActorStateType.Move, this),
                new ActorAttackState(ActorStateType.Attack, this),
                new ActorDeadState(ActorStateType.Dead, this)
            );
        }

        // 스폰 직후(ActorBattleSystem이) 호출한다. 보스를 향해 이동을 시작한다.
        public void Setup(ActorData actorData, Transform bossTransform)
        {
            Context.BossTransform = bossTransform;
            Context.SetStats(actorData);
            StateMachine.ChangeState(ActorStateType.Move);
        }

        // hp가 바뀔 때마다(BattleActor.Hp setter) 호출된다. SetStats는 Hp 프로퍼티를 거치지 않으므로
        // 실제로는 처음 공격받는 순간 처음 호출된다 - 그때 게이지를 만들어 비율을 반영한다.
        protected override void OnHpChanged()
        {
            if (_hpGageView == null)
            {
                _hpGageView = UIActorHPGage.CreateOrGet();
                if (_hpGageView != null)
                    _hpGageView.SetTarget(HeadSocket);
            }

            if (_hpGageView != null)
                _hpGageView.SetHp(MaxHp > 0 ? (float)Hp / MaxHp : 0f);
        }

        // Move 상태 진입 시(ActorMoveState.EnterState) 호출된다.
        // targetPosition = 보스 위치 - (보스 위치 - 아군 위치).normalized * (보스 사거리 + 아군 사거리)
        // (두 액터의 공격범위를 합친 거리만큼 보스에서 떨어진 지점 - 두 원이 맞닿는 지점까지 이동한다)
        public override void PrepareMoveTarget()
        {
            Transform bossTransform = BossTransform;
            if (bossTransform == null)
            {
                OnMoveArrived();
                return;
            }

            Vector3 direction = bossTransform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            direction.Normalize();

            float combinedRange = GetCurrentBossAttackRange() + AttackRange;
            MoveTargetPosition = bossTransform.position - direction * combinedRange;
        }

        // Move 상태에서 목표 지점 도착 시(ActorMoveState) 호출된다.
        public override void OnMoveArrived()
        {
            StateMachine.ChangeState(ActorStateType.Attack);
        }

        // 보스 클리어 직후(ActorBattleSystem.SetAlliesWaiting) 호출: 다음 보스가 등장할 때까지 제자리에서 대기한다.
        public void EnterWaiting()
        {
            if (Hp <= 0 || StateMachine.IsState(ActorStateType.Dead))
                return;

            StateMachine.ChangeState(ActorStateType.Idle);
        }

        // 새 보스 등장 시(ActorBattleSystem.ResumeAllies) 호출: Move 로 재진입해 새 보스 사거리 기준으로
        // 위치를 다시 잡고(PrepareMoveTarget) 도착하면 공격을 재개한다.
        public void ResumeBattle()
        {
            if (Hp <= 0 || StateMachine.IsState(ActorStateType.Dead))
                return;

            StateMachine.ChangeState(ActorStateType.Move);
        }

        // 현재 스테이지 보스(BossBattleActor) 인스턴스를 찾는다.
        public BossBattleActor FindBossBattleActor()
        {
            Transform bossTransform = BossTransform;
            return bossTransform != null ? bossTransform.GetComponentInChildren<BossBattleActor>() : null;
        }

        // 현재 보스의 attackRange. (실제 스케일 반영, 절반)
        public float GetCurrentBossAttackRange()
        {
            BossBattleActor boss = FindBossBattleActor();
            return boss != null ? boss.AttackRange : 0f;
        }
    }
}
