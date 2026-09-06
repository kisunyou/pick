namespace FunRabbit
{
    // 인형뽑기 기기에 낮은 확률(랜덤박스와 동일한 1~2개 규칙)로 섞여 나오는 "황금 인형".
    // 물리 동작은 DollBoxActor와 동일하고, 스폰 시 머티리얼이 황금색으로 물들며(GameDollCreator),
    // Basket이 타입으로 구분해 뽑으면 아군이 3마리 합류하는 보상 흐름으로 처리하도록 표시만 한다.
    public class GoldenDollBoxActor : DollBoxActor
    {
    }
}
