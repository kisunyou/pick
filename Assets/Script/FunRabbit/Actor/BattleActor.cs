using UnityEngine;

namespace FunRabbit
{
    // 보스 배틀 필드(ActorBattleSystem)에 스폰되는 전투용 액터의 공통 베이스.
    // hp/공격 스탯(BattleActorContext), 물리 고정, 애니메이션 재생 래퍼, 사거리 기즈모 표시를 제공한다.
    // 실제 역할(ally의 접근/공방, 보스의 대기)은 AllyBattleActor/BossBattleActor가 각각 구현한다.
    public class BattleActor : Actor
    {
        // BattleActor 전용 데이터 컨텍스트. hp/attackPower/attackSpeed/attackRange는 ally/보스가 공통으로
        // 갖는 전투 스탯이라 여기 둔다 (CollectionActor 등은 전투 스탯이 없어 쓰지 않는다).
        public class BattleActorContext : ActorContext
        {
            private int _hp;
            private int _maxHp;
            private int _attackPower;
            private float _attackSpeed;
            private float _attackRange;
            private string _animalKey;

            public BattleActorContext(Actor actor) : base(actor) { }

            // ActorBattleSystem.bottomFloor 참조. SetupFloor로 채워지며, 이동할 때마다(OnMoveUpdate)
            // bottomSocket을 이 높이에 다시 맞춘다 (모델마다 피벗~바닥 거리가 달라 필요).
            public Transform FloorTransform { get; set; }

            public int Hp { get => _hp; set => _hp = value; }
            public int MaxHp => _maxHp;
            public int AttackPowerValue => _attackPower;
            public float AttackSpeedValue => _attackSpeed;
            public string AnimalKey => _animalKey;

            // 실제 스케일(inGameScale 등) 반영, 절반 (인형마다 크기가 달라 사거리도 그만큼 커/작아야 함)
            public float AttackRange => _attackRange * _actor.transform.lossyScale.x / 2;

            // hp/attackPower/attackSpeed는 같은 animalKey라도 ally로 쓰일 때와 보스로 쓰일 때 값이 다르다
            // (actor.json의 ally*/boss* 필드). 기본은 ally 값이고, BossBattleActorContext가 보스 값을 반환하도록 오버라이드한다.
            protected virtual int GetHpValue(string animalKey) => GameActorData.GetAllyHp(animalKey);
            protected virtual int GetAttackPowerValue(string animalKey) => GameActorData.GetAllyAttackPower(animalKey);
            protected virtual float GetAttackSpeedValue(string animalKey) => GameActorData.GetAllyAttackSpeed(animalKey);

            // hp/attackPower/attackSpeed/attackRange를 animalKey 테이블 기준으로 채운다.
            // 변형(_g/_r) 행은 actor.json에 스탯이 이미 2/3배로 기입돼 있어 별도 배수 없이 그대로 쓴다.
            public void SetStats(ActorData actorData)
            {
                _animalKey = actorData.animalKey;
                _hp = GetHpValue(actorData.animalKey);
                _maxHp = _hp;
                _attackPower = GetAttackPowerValue(actorData.animalKey);
                _attackSpeed = GetAttackSpeedValue(actorData.animalKey);
                _attackRange = GameActorData.GetAttackRange(actorData.animalKey);
            }
        }

        public const float MOVE_SPEED = 2f; // 보스를 향해 이동하는 속도 (units/s)

        // 실제 hp 차감(양방향)을 적용한다. true였을 때는 이동/사거리 판정/공격 트리거 로직만 확인하는 용도였음.
        public const bool DISABLE_HP_DAMAGE_FOR_TESTING = false;

        // 테스팅용: true면 ally가 보스를 때려도 GameQuestManager.BossHp(에너지)가 줄지 않는다.
        // (보스가 ally를 죽이는 쪽 테스트를 스테이지 클리어 걱정 없이 반복하기 위함)
        public const bool DISABLE_BOSS_DAMAGE_FOR_TESTING = false;

        // 공격 스윙 시작~실제 타격 사이의 지연(초). hitFx/데미지 텍스트/실제 데미지 적용은 이 시간만큼
        // 늦춰 공격 애니메이션의 타격 타이밍과 맞춘다 (attackFx는 스윙 시작 즉시 재생).
        public const float HIT_DELAY = 0.4f;

        // base(Actor)의 _context와 같은 인스턴스를 BattleActorContext 타입으로 노출한다 (new로 가림).
        public new BattleActorContext Context => (BattleActorContext)base.Context;

        public int Hp
        {
            get => Context.Hp;
            set
            {
                Context.Hp = value;
                OnHpChanged();
            }
        }

        public int MaxHp => Context.MaxHp;
        public int AttackPowerValue => Context.AttackPowerValue;
        public float AttackSpeedValue => Context.AttackSpeedValue;
        public float AttackRange => Context.AttackRange;
        public string AnimalKey => Context.AnimalKey;

        // hp가 바뀔 때마다 호출된다. (AllyBattleActor: 머리 위 UIActorHPGage 갱신)
        protected virtual void OnHpChanged() { }

        public override float MoveSpeed => MOVE_SPEED;

        // 서브클래스가 자신만의 Context(예: AllyBattleActor.AllyBattleActorContext)를 쓰고 싶으면 오버라이드한다.
        protected override ActorContext CreateContext() => new BattleActorContext(this);

        protected override void Awake()
        {
            base.Awake();

            Animation.CacheAnimator(gameObject);
        }

        public void PlayIdle() => Animation.PlayIdleAnimation();
        public void PlayMove() => Animation.PlayMoveAnimation();
        public void PlayAttack() => Animation.PlayAttackAnimation();

        // 공격/피격 이펙트 재생 배율 (원본 크기의 3배). 카메라 기준 항상 최상단 표시는 attackFx/hitFx
        // 프리팹이 위치한 TransparentFX 레이어를 bossCameraFxOverlay(Stage0.unity)가 전담 렌더링해 보장한다.
        private const float AttackHitFxScale = 3f;

        // 공격 스윙 시작 즉시(ActorAttackState/BossAttackState) 호출. animalKey(공격자)의 attackFx를
        // 공격자-타겟 중간 지점에 재생한다. actor.json에 값이 없으면(빈 문자열) 재생하지 않는다.
        // WorldFxPlayer가 프리팹 로드/재생 인스턴스를 풀링해 재생 비용을 최소화한다.
        public static void PlayAttackFx(string animalKey, Vector3 attackerPosition, Vector3 targetPosition)
        {
            return;
            string attackFx = GameActorData.GetAttackFx(animalKey);
            if (attackFx != null)
                WorldFxPlayer.Instance.Play(attackFx, Vector3.Lerp(attackerPosition, targetPosition, 0.5f), AttackHitFxScale);
        }

        // 실제 타격 시점(HIT_DELAY 후, 데미지 텍스트와 같은 타이밍)에 호출. animalKey(공격자)의 hitFx를
        // 타겟 위치에 재생한다. actor.json에 값이 없으면(빈 문자열) 재생하지 않는다.
        public static void PlayHitFx(string animalKey, Vector3 targetPosition)
        {
            string hitFx = GameActorData.GetHitFx(animalKey);
            if (hitFx != null)
                WorldFxPlayer.Instance.Play(hitFx, targetPosition, AttackHitFxScale);
        }

        // ActorBattleSystem이 스폰 직후(SetupBattleActor) 호출한다. bottomFloor를 저장하고 즉시 접지를 보정한다.
        public void SetupFloor(Transform floorTransform)
        {
            Context.FloorTransform = floorTransform;
            AlignToFloor();
        }

        private void AlignToFloor()
        {
            if (Context.FloorTransform != null)
                AlignBottomSocketToHeight(Context.FloorTransform.position.y);
        }

        // Move 상태에서 이동할 때마다(ActorMoveState) 호출된다 - 계속 접지를 보정한다.
        public override void OnMoveUpdate() => AlignToFloor();

        // 공격 사거리를 씬 뷰에 디버그 라인(수평 원)으로 표시한다. 색은 서브클래스가 결정한다
        // (AllyBattleActor: 노란색, BossBattleActor: 기본값인 빨간색).
        protected virtual Color GizmoColor => Color.red;

        private void OnDrawGizmos()
        {
            float scaledRange = AttackRange;
            if (scaledRange <= 0f)
                return;

            Gizmos.color = GizmoColor;
            DrawGizmoCircle(transform.position, scaledRange);
        }

        // 지정 위치를 중심으로 한 수평(XZ) 원을 라인으로 그린다.
        protected static void DrawGizmoCircle(Vector3 center, float radius, int segments = 32)
        {
            Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, nextPoint);
                prevPoint = nextPoint;
            }
        }
    }
}
