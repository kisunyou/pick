using UnityEngine;

namespace FunRabbit
{
    // Actor의 애니메이션 재생을 전담하는 클래스. Actor가 인스턴스를 소유하고 위임(delegate)한다.
    public class ActorAnimation
    {
        // 공용 애니메이션 스테이트 (model_base_anim_ctrl 오버라이드 기준)
        public static readonly int IdleStateHash = Animator.StringToHash("Idle0");
        public static readonly int MoveStateHash = Animator.StringToHash("Run");
        public static readonly int AttackStateHash = Animator.StringToHash("Attack0");

        public Animator Animator { get; private set; }

        // 마지막으로 재생 지시한 애니메이터 스테이트 (중복 전환 방지용 - PlayAnimation 참고)
        public int CurrentStateHash { get; set; }

        // Animator를 owner의 자식에서 찾아 캐시한다. (모델 프리팹은 루트가 아니라 자식에 Animator가 있음)
        // 일부 클립(attack0 등)에는 AnimationEvent가 임베드돼 있어, 리시버가 없으면
        // "AnimationEvent ... has no receiver!" 경고가 뜬다 - AnimationEventRelay를 함께 붙여 받아준다.
        public void CacheAnimator(GameObject owner)
        {
            if (Animator != null)
                return;

            Animator = owner.GetComponentInChildren<Animator>();
            if (Animator != null && Animator.GetComponent<AnimationEventRelay>() == null)
                Animator.gameObject.AddComponent<AnimationEventRelay>();
        }

        // 지정 스테이트로 크로스페이드한다. forceRestart가 아니면 같은 스테이트로의 중복 전환은 무시한다.
        // CrossFade(normalized)는 블렌드 길이가 클립 길이에 비례해 모델마다 달라지므로,
        // 고정 시간(초) 버전을 사용해 전 인형이 일정한 전환 속도를 갖게 한다.
        public void PlayAnimation(int stateHash, float crossFadeDuration = 0.15f, bool forceRestart = false)
        {
            if (Animator == null || stateHash == 0)
                return;

            if (!forceRestart && stateHash == CurrentStateHash)
                return;

            CurrentStateHash = stateHash;
            Animator.CrossFadeInFixedTime(stateHash, crossFadeDuration);
        }

        public void PlayIdleAnimation() => PlayAnimation(IdleStateHash);
        public void PlayMoveAnimation() => PlayAnimation(MoveStateHash);
        // 공격은 반복될 때마다(같은 스테이트라도) 매번 처음부터 다시 재생해야 하므로 forceRestart로 재생한다.
        public void PlayAttackAnimation() => PlayAnimation(AttackStateHash, forceRestart: true);
    }
}
