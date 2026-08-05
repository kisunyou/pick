namespace FunRabbit
{
    // 인형뽑기 기기에 섞여 나오는 "랜덤박스"(doll_random_prefab) 인형.
    // 물리 동작은 DollBoxActor와 동일하고, Basket이 타입으로 구분해 ally 합류 대신
    // PlayerContext.RandomBoxCount를 올리는 별도 흐름(+전용 트레일 연출)으로 처리하도록 표시만 한다.
    public class RandomBoxDollActor : DollBoxActor
    {
    }
}
