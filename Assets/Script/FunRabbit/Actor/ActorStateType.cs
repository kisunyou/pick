namespace FunRabbit
{
    // Actor 상태 머신의 상태 키. dev 작업 폴더(TeenyWorld)의 TeenyActorStateType 규칙(정수 키)을 따른다.
    // Idle/Move는 BattleActor/CollectionActor가 공용으로, Attack/Dead는 BattleActor 전용으로 사용한다.
    public static class ActorStateType
    {
        public const int Idle = 0;
        public const int Move = 1;
        public const int Attack = 2;
        public const int Dead = 3;
    }
}
