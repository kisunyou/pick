using UnityEngine;

namespace FunRabbit
{
    // 인형 액터의 공통 베이스. 인형뽑기 기기(크레인) 모드는 DollBoxActor, 도감 배회 모드는 CollectionActor,
    // 보스 배틀 필드(ActorBattleSystem)의 보스/ally 표시용은 BattleActor가 담당한다.
    // Idle/Move 상태 머신(ActorStateMachine)을 공용으로 소유하며, 각 서브클래스는 아래 virtual 훅으로
    // 자신만의 이동/대기 동작을 결정한다 (BattleActor: 보스 접근/공격 대기, CollectionActor: 랜덤 배회).
    public class Actor : MonoBehaviour
    {
        // Actor가 공용으로 갖는 데이터 컨텍스트. hp/공격 스탯은 전투 관련 전용이라 여기 두지 않고
        // BattleActor.BattleActorContext로 옮겼다 (CollectionActor 등은 전투 스탯을 쓰지 않으므로).
        // CollectionActor/BattleActor는 이를 상속한 전용 Context(CollectionActorContext/BattleActorContext)를 사용한다.
        public class ActorContext
        {
            protected readonly Actor _actor;

            public DollData Data { get; set; }

            public ActorContext(Actor actor)
            {
                _actor = actor;
            }
        }

        [SerializeField] Rigidbody rigidbody;
        [SerializeField] Collider[] colliders;
        [SerializeField] Transform bottomSocket;
        [SerializeField] Transform headSocket;

        // 컴포넌트 스왑(DestroyImmediate + AddComponent<T>) 시 위 SerializeField 값이 유실되므로,
        // 스왑 직전에 읽고(RigidbodyComponent/CollidersArray/BottomSocket/HeadSocket) 직후 SetSwappedReferences로 복원한다.
        public Rigidbody RigidbodyComponent => rigidbody;
        public Collider[] CollidersArray => colliders;
        public Transform BottomSocket => bottomSocket;
        public Transform HeadSocket => headSocket;

        public void SetSwappedReferences(Rigidbody rigidbody, Collider[] colliders, Transform bottomSocket, Transform headSocket)
        {
            this.rigidbody = rigidbody;
            this.colliders = colliders;
            this.bottomSocket = bottomSocket;
            this.headSocket = headSocket;
        }

        // rigidbody/colliders를 물리 시뮬레이션에서 제외한다. (BattleActor: transform으로 직접 이동/배치하므로 물리 불필요)
        public void DisablePhysics()
        {
            if (rigidbody != null)
            {
                // kinematic 바디는 Discrete/ContinuousSpeculative만 지원 - 프리팹 기본값이 ContinuousDynamic이라
                // isKinematic보다 먼저 모드를 내리지 않으면 'Kinematic body only supports Speculative...' 경고가 뜬다
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }

            if (colliders != null)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider != null)
                        collider.enabled = false;
                }
            }
        }

        // bottomSocket의 월드 y가 floorY와 같아지도록 transform.position.y를 보정한다.
        // (모델마다 피벗~바닥 거리가 달라 루트 위치만으로는 접지가 맞지 않아 bottomSocket 기준으로 보정한다)
        protected void AlignBottomSocketToHeight(float floorY)
        {
            if (bottomSocket == null)
                return;

            float delta = floorY - bottomSocket.position.y;
            transform.position += new Vector3(0f, delta, 0f);
        }

        public const float TURN_SPEED = 10f; // 타겟을 바라보는 회전 속도 (공용, 기존 100f의 1/10)

        // 필드 초기화식은 this를 참조할 수 없어(CS0027) Awake에서 생성한다.
        private ActorContext _context;
        public ActorContext Context => _context;

        // 서브클래스가 자신만의 Context(예: BattleActor.BattleActorContext)를 쓰고 싶으면 오버라이드한다.
        protected virtual ActorContext CreateContext() => new ActorContext(this);

        protected virtual void Awake()
        {
            _context = CreateContext();
        }

        // 컬렉션(도감) 배회 인형인지 (터치 판정 등에서 기계 인형과 구분용). CollectionActor가 true로 오버라이드.
        public virtual bool IsCollectionMode => false;

        // Actor의 애니메이션 재생 전담 클래스. CollectionActor/BattleActor가 Animation 프로퍼티로 직접 호출한다.
        protected readonly ActorAnimation _actorAnimation = new ActorAnimation();

        public ActorAnimation Animation { get { return _actorAnimation; } }

        // 이름이 UnityEngine.Animator 타입명과 겹치지 않도록 필드였을 때와 동일하게 _animator로 노출한다.
        // (프로퍼티명을 Animator로 하면 "Animator.StringToHash(...)" 같은 정적 호출부에서 타입명과 충돌 위험)
        protected Animator _animator => _actorAnimation.Animator;
        protected int _currentStateHash
        {
            get => _actorAnimation.CurrentStateHash;
            set => _actorAnimation.CurrentStateHash = value;
        }

        // Idle/Move/Attack/Dead 상태 머신. 서브클래스가 Awake 등에서 CreateState로 상태를 등록한다.
        public ActorStateMachine StateMachine { get; } = new ActorStateMachine();

        // ── Move 상태(ActorMoveState) 공용 훅 ─────────────────────────────
        public Vector3 MoveTargetPosition { get; protected set; }
        public virtual float MoveSpeed => 2f;

        // Move 상태 진입 시 호출된다. MoveTargetPosition을 계산해 저장한다. (기본은 아무 것도 하지 않음 -
        // 이미 MoveTargetPosition이 설정돼 있다고 가정. CollectionActor는 Idle 단계에서 미리 정해둔다)
        public virtual void PrepareMoveTarget() { }

        // Move 상태에서 목표 지점 도착 시 호출된다. 다음 상태 전환은 서브클래스가 결정한다.
        public virtual void OnMoveArrived() { }

        // Move 상태에서 위치가 갱신될 때마다(이동 중) 호출된다. 기본은 아무 것도 하지 않음.
        public virtual void OnMoveUpdate() { }

        // ── Idle 상태(ActorIdleState) 공용 훅 ─────────────────────────────
        // 대기 시간(초). 기본은 무한대 - 별도 전환 없이 계속 idle을 유지한다.
        public virtual float GetIdleDuration() => float.PositiveInfinity;

        // 대기 시간이 끝났을 때 호출된다. true = 서브클래스가 다음 상태로 전환했다는 뜻.
        // false = ActorIdleState가 새 대기시간을 굴려 계속 idle을 유지한다 (예: 유효한 목표를 못 찾은 경우 재시도).
        public virtual bool OnIdleComplete() => false;

        public void DestroySelf() => Destroy(gameObject);

        // 지정 위치를 수평 방향으로 부드럽게 바라보도록 회전한다. (ActorState.SetRotate가 사용)
        public void FaceTarget(Vector3 targetPosition)
        {
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(toTarget.normalized), Time.deltaTime * TURN_SPEED);
        }

        private void Update()
        {
            StateMachine.UpdateState(Time.deltaTime);
        }

        private void OnDestroy()
        {
            StageManager.RemoveActor(this);
        }
    }
}
