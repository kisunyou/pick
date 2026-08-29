using DG.Tweening;
using UnityEngine;

namespace FunRabbit
{
    // attackSpeed 간격으로 보스에게 attackPower만큼 데미지를 입힌다.
    // 보스 쪽의 반격(카운터)은 이제 보스 자신의 상태(BossAttackState)가 스스로 타겟을 찾아 담당한다.
    public class ActorAttackState : BattleActorState
    {
        // AttackPowerUp 버프 1개 소비 시 공격력 배수(15% 증가) / 데미지 텍스트 크기 배수(50% 증가)
        private const float ATTACK_POWER_UP_MULTIPLIER = 1.15f;
        private const float ATTACK_POWER_UP_TEXT_SCALE = 1.5f;

        private float _attackTimer;

        public ActorAttackState(int key, AllyBattleActor actor) : base(key, actor) { }

        public override void EnterState()
        {
            base.EnterState();
            _attackTimer = 0f;
            _battleActor.PlayAttack();
        }

        public override void UpdateState(float deltaTime)
        {
            // Attack 상태에서도 이동 상태와 동일하게 매 프레임 보스 쪽을 계속 바라본다.
            Transform bossTransform = _battleActor.BossTransform;
            if (bossTransform != null)
                SetRotate(bossTransform.position);

            base.UpdateState(deltaTime);

            _attackTimer += deltaTime;
            if (_attackTimer < _battleActor.AttackSpeedValue)
                return;

            _attackTimer = 0f;

            // 반복 공격마다 애니메이션/공격 이펙트를 재생한다 (hp 차감 테스트 여부와 무관하게 항상 재생)
            _battleActor.PlayAttack();

            if (bossTransform != null)
                BattleActor.PlayAttackFx(_battleActor.AnimalKey, _battleActor.transform.position, bossTransform.position);

            int baseDamage = _battleActor.AttackPowerValue;
            int damage = baseDamage;
            float damageTextScale = 1f;

            // AttackPowerUp 보유 시 공격마다 1개씩 소비하며 공격력을 15% 올리고 데미지 텍스트도 50% 키운다.
            if (UIHud.Instance != null && UIHud.Instance.BuffManager != null
                && UIHud.Instance.BuffManager.TryConsumeBuff(BuffType.AttackPowerUp))
            {
                damage = Mathf.RoundToInt(baseDamage * ATTACK_POWER_UP_MULTIPLIER);
                damageTextScale = ATTACK_POWER_UP_TEXT_SCALE;
            }

            string animalKey = _battleActor.AnimalKey;

            // 스윙 시점의 보스 세대. HIT_DELAY 사이에 보스가 죽어 교체됐으면(세대 변경) 이 타격은 버린다 -
            // 이전 보스에게 날린 마지막 타격이 새 보스의 hp 를 깎지 않도록.
            int bossGeneration = ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystemAtSwing)
                ? battleSystemAtSwing.BossGeneration : -1;

            // hitFx/데미지 텍스트/실제 데미지 적용은 타격 타이밍(HIT_DELAY)에 맞춰 함께 늦춘다.
            DOVirtual.DelayedCall(BattleActor.HIT_DELAY, () =>
            {
                if (bossTransform == null)
                    return;

                if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem)
                    || !battleSystem.HasBoss || battleSystem.BossGeneration != bossGeneration)
                    return;

                BattleActor.PlayHitFx(animalKey, bossTransform.position);
                BattleActorDamageControl.Instance.ShowDamage(bossTransform.position, damage, damageTextScale, damage - baseDamage);

                if (!BattleActor.DISABLE_BOSS_DAMAGE_FOR_TESTING && GameQuestManager.IsCheckInstance())
                    GameQuestManager.Instance.DamageBoss(damage);
            });
        }
    }
}
