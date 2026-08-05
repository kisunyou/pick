using DG.Tweening;
using UnityEngine;

namespace FunRabbit
{
    // 타겟(ally)이 있을 때의 보스 상태. 한 번 잡은 타겟만 계속 공격한다 - 타겟이 죽거나 사라지기
    // 전까지는 다른 ally가 있어도 바꾸지 않는다. 평소엔 회전하지 않고 있다가, 공격 간격(보스의
    // AttackSpeedValue - actor.json bossAttackSpeed, 스테이지별로 다르다)마다 실제로 공격을 시도하는
    // 순간에만 타겟 쪽으로 회전을 시작한다.
    // 타겟이 아직 사거리 밖이면 타이머를 리셋하지 않고 그대로 둔다 - 리셋해버리면 ally가 도착한
    // 직후에도 최대 공격 간격만큼 불필요하게 더 기다리게 되어 "늦게 공격"하는 것처럼
    // 보인다. 실제로 공격(타이머 리셋)은 사거리 안에 들어온 게 확인된 뒤에만 일어난다.
    // 킬(hp 0) 순간에도 바로 Idle로 끊지 않는다 - 공격 애니메이션이 끝까지 재생되고 회전도 계속
    // 진행되도록 KILL_FINISH_DELAY만큼 이 상태에 머문 뒤에야 Idle로 돌아가 새 타겟을 검색한다.
    public class BossAttackState : ActorState
    {
        // DefensePowerUp 버프 1개 소비 시 받는 데미지 배수 / 데미지 텍스트 크기 배수 (둘 다 30% 감소)
        private const float DEFENSE_UP_MULTIPLIER = 0.7f;
        private const float DEFENSE_UP_TEXT_SCALE = 0.7f;

        private readonly BossBattleActor _boss;
        private float _attackTimer;

        // 타겟을 처치한 뒤 Idle로 돌아가기 전 대기 시간 (공격 애니메이션/회전 재생을 보장)
        private const float KILL_FINISH_DELAY = 1f;
        private bool _isFinishingKill;
        private float _killFinishTimer;

        public BossAttackState(int key, BossBattleActor actor) : base(key, actor)
        {
            _boss = actor;
        }

        public override void EnterState()
        {
            base.EnterState();
            _attackTimer = 0f;
            _isFinishingKill = false;
        }

        public override void UpdateState(float deltaTime)
        {
            // 킬 직후: 회전(SetRotate로 설정된 방향)만 계속 진행시키며 애니메이션이 끝날 시간을 번다.
            if (_isFinishingKill)
            {
                base.UpdateState(deltaTime);

                _killFinishTimer += deltaTime;
                if (_killFinishTimer >= KILL_FINISH_DELAY)
                {
                    _isFinishingKill = false;
                    _boss.StateMachine.ChangeState(ActorStateType.Idle);
                }

                return;
            }

            AllyBattleActor target = _boss.CurrentTarget;
            if (target == null)
            {
                _boss.StateMachine.ChangeState(ActorStateType.Idle);
                return;
            }

            base.UpdateState(deltaTime);

            _attackTimer += deltaTime;
            if (_attackTimer < _boss.AttackSpeedValue)
                return;

            // ally는 (보스 사거리 + 자신의 사거리) 지점까지 접근하되 ARRIVE_THRESHOLD만큼 못 미쳐서
            // 멈출 수 있어, 같은 허용치를 더해 판정한다. 아직 사거리 밖이면 타이머를 그대로 둔 채
            // 리턴한다 - 사거리 안에 들어오는 즉시(다음 프레임) 바로 공격할 수 있도록.
            float distance = HorizontalDistance(_boss.transform.position, target.transform.position);
            float combinedRange = _boss.AttackRange + target.AttackRange;
            if (distance > combinedRange + ARRIVE_THRESHOLD)
                return;

            _attackTimer = 0f;

            // 공격하는 이 순간에만 타겟 쪽으로 회전을 시작한다 (평소엔 정지 상태 유지).
            SetRotate(target.transform.position);

            // 공격 애니메이션/공격 이펙트는 hp 차감 테스트 여부와 무관하게 항상 재생한다.
            _boss.PlayAttack();
            BattleActor.PlayAttackFx(_boss.AnimalKey, _boss.transform.position, target.transform.position);

            int baseDamage = _boss.AttackPowerValue;
            int damage = baseDamage;
            float damageTextScale = 1f;

            // DefensePowerUp 보유 시 피격마다 1개씩 소비하며 받는 데미지를 30% 줄이고 데미지 텍스트도 30% 줄인다.
            if (UIHud.Instance != null && UIHud.Instance.BuffManager != null
                && UIHud.Instance.BuffManager.TryConsumeBuff(BuffType.DefensePowerUp))
            {
                damage = Mathf.RoundToInt(baseDamage * DEFENSE_UP_MULTIPLIER);
                damageTextScale = DEFENSE_UP_TEXT_SCALE;
            }

            // hitFx/데미지 텍스트/실제 데미지 적용(+킬 판정)은 타격 타이밍(HIT_DELAY)에 맞춰 함께 늦춘다.
            DOVirtual.DelayedCall(BattleActor.HIT_DELAY, () =>
            {
                if (_boss == null || target == null)
                    return;

                BattleActor.PlayHitFx(_boss.AnimalKey, target.transform.position);
                BattleActorDamageControl.Instance.ShowDamage(target.transform.position, damage, damageTextScale, damage - baseDamage);

                if (BattleActor.DISABLE_HP_DAMAGE_FOR_TESTING)
                    return;

                target.Hp -= damage;
                if (target.Hp <= 0)
                {
                    _boss.ClearTarget();
                    target.StateMachine.ChangeState(ActorStateType.Dead);

                    // 바로 Idle로 전환하지 않고, 공격 애니메이션/회전이 끝날 시간을 준 뒤 전환한다.
                    _isFinishingKill = true;
                    _killFinishTimer = 0f;
                }
            });
        }
    }
}
