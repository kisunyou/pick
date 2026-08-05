namespace FunRabbit
{
    // 대기(idle) 상태. actor.GetIdleDuration()만큼 기다린 뒤 actor.OnIdleComplete()를 호출한다.
    // OnIdleComplete()가 false를 반환하면(예: 유효한 다음 목표를 못 찾음) 새 대기시간을 굴려 idle을 유지한다.
    // (BattleActor 보스: 기본값(무한대)이라 사실상 계속 idle 유지 / CollectionActor: 랜덤 대기 후 배회 목적지를 찾아 Move 전환)
    public class ActorIdleState : ActorState
    {
        private float _idleTimer;
        private float _idleDuration;

        public ActorIdleState(int key, Actor actor) : base(key, actor) { }

        public override void EnterState()
        {
            base.EnterState();
            _actor.Animation.PlayIdleAnimation();
            ResetTimer();
        }

        public override void UpdateState(float deltaTime)
        {
            base.UpdateState(deltaTime);

            _idleTimer += deltaTime;
            if (_idleTimer < _idleDuration)
                return;

            if (!_actor.OnIdleComplete())
                ResetTimer();
        }

        private void ResetTimer()
        {
            _idleTimer = 0f;
            _idleDuration = _actor.GetIdleDuration();
        }
    }
}
