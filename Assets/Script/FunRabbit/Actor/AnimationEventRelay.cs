using UnityEngine;

namespace FunRabbit
{
    // Animator가 있는 자식 오브젝트에 붙어 애니메이션 클립에 임베드된 AnimationEvent를 받는다.
    // 리시버가 없으면 "AnimationEvent ... has no receiver!" 경고가 뜨므로, 별도 로직 없이 조용히 받기만 한다.
    // (Actor.CacheAnimator가 Animator를 찾을 때 자동으로 붙여준다)
    public class AnimationEventRelay : MonoBehaviour
    {
        // attack0 클립 끝에 걸린 AnimationEvent.
        public void OnEndAnim()
        {
        }
    }
}
