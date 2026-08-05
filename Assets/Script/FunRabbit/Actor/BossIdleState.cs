namespace FunRabbit
{
    // 타겟이 없을 때의 보스 상태. 스폰 시점의 정면(RestFacingPoint)을 바라보며,
    // 주기적으로 살아있는 ally를 검색해 찾으면 Attack으로 전환한다.
    public class BossIdleState : ActorState
    {
        private const float SEARCH_INTERVAL = 0.3f;

        private readonly BossBattleActor _boss;
        private float _searchTimer;

        public BossIdleState(int key, BossBattleActor actor) : base(key, actor)
        {
            _boss = actor;
        }

        public override void EnterState()
        {
            base.EnterState();
            _boss.PlayIdle();
            _searchTimer = 0f;
        }

        public override void UpdateState(float deltaTime)
        {
            SetRotate(_boss.RestFacingPoint);
            base.UpdateState(deltaTime);

            _searchTimer += deltaTime;
            if (_searchTimer < SEARCH_INTERVAL)
                return;

            _searchTimer = 0f;

            AllyBattleActor target = _boss.FindLivingAllyTarget();
            if (target == null)
                return;

            _boss.SetTarget(target);
            _boss.StateMachine.ChangeState(ActorStateType.Attack);
        }
    }
}
