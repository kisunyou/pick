namespace FunRabbit
{
    // ally 전투 상태(Attack/Dead)의 중간 베이스.
    // ActorState._actor(Actor)를 AllyBattleActor로 캐스팅해 전투 관련 멤버(BossTransform, Hp 등)에 접근한다.
    // Idle/Move처럼 CollectionActor와 공용인 상태는 ActorState를 직접 상속한다.
    // (Attack/Dead는 보스가 아니라 ally에서만 쓰이므로 AllyBattleActor로 좁혀 받는다)
    public abstract class BattleActorState : ActorState
    {
        protected readonly AllyBattleActor _battleActor;

        protected BattleActorState(int key, AllyBattleActor actor) : base(key, actor)
        {
            _battleActor = actor;
        }
    }
}
