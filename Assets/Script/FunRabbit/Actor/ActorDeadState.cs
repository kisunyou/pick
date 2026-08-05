namespace FunRabbit
{
    // hp가 0 이하가 되어 죽었을 때의 상태. 진입 즉시 자신을 파괴한다.
    public class ActorDeadState : BattleActorState
    {
        public ActorDeadState(int key, AllyBattleActor actor) : base(key, actor) { }

        public override void EnterState()
        {
            base.EnterState();
            _actor.DestroySelf();
        }
    }
}
