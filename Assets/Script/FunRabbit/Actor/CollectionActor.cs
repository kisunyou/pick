using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 컬렉션(도감) 배회 모드 인형. 물리 없이 transform으로 직접 이동하며 배회한다.
    // Idle(대기) <-> Move(배회 이동) 전이는 Actor 공용 상태 머신(ActorIdleState/ActorMoveState)이 담당한다.
    public class CollectionActor : Actor
    {
        // CollectionActor 전용 데이터 컨텍스트. 현재는 추가 데이터 없이 ActorContext(hp/공격 스탯)만 그대로 쓴다.
        public class CollectionActorContext : ActorContext
        {
            public CollectionActorContext(Actor actor) : base(actor) { }
        }

        // base(Actor)의 _context와 같은 인스턴스를 CollectionActorContext 타입으로 노출한다 (new로 가림).
        public new CollectionActorContext Context => (CollectionActorContext)base.Context;

        const string CollectionDollLayerName = "collection_doll";

        // 이동 애니메이션 스테이트 후보. 앞에서부터 존재하는 첫 스테이트를 사용한다.
        // (현재 공유 컨트롤러 model_base_anim_ctrl에는 jump가 없어 Run이 사용된다.
        //  컨트롤러에 jump 스테이트를 추가하면 코드 수정 없이 jump가 우선 적용된다)
        static readonly string[] MoveStateCandidates = { "jump", "Jump", "Run" };

        const float ROAM_MOVE_SPEED = 1.5f;   // 배회 이동 속도 (units/s)
        const float ROAM_IDLE_MIN = 1.5f;     // idle 최소 대기(초)
        const float ROAM_IDLE_MAX = 4f;       // idle 최대 대기(초)
        const float ROAM_HOP_MIN = 2f;        // 한 번에 이동하는 최소 거리
        const float ROAM_HOP_MAX = 5f;        // 한 번에 이동하는 최대 거리
        const float ROAM_AREA_MARGIN = 0.8f;  // 플레인 가장자리 여유 (밖으로 삐져나가지 않게)

        public override bool IsCollectionMode => true;

        private IReadOnlyList<Bounds> _roamAreas; // 배회 가능 영역 (CollectionManager가 전달, 공유 리스트)
        private int _idleStateHash;
        private int _moveStateHash;

        public override float MoveSpeed => ROAM_MOVE_SPEED;

        protected override ActorContext CreateContext() => new CollectionActorContext(this);

        protected override void Awake()
        {
            base.Awake();

            StateMachine.CreateState(
                new ActorIdleState(ActorStateType.Idle, this),
                new ActorMoveState(ActorStateType.Move, this)
            );
        }

        // Animator가 기본 스테이트로 초기화될 시간을 준 뒤(다음 프레임) 스테이트를 캡처하고 배회를 시작한다.
        // (SetupCollectionMode는 Instantiate 직후 같은 프레임에 호출되므로, 그 시점엔 아직 캡처하지 않는다)
        private void Start()
        {
            CaptureAnimatorStates();
            StateMachine.ChangeState(ActorStateType.Idle);
        }

        // ── 컬렉션(도감) 배회 모드 ──────────────────────────────────
        // Instantiate 직후(Start 이전)에 호출한다.
        // - 물리를 멈추고(kinematic) transform으로 직접 이동한다
        // - 하위 모든 Renderer 오브젝트를 collection_doll 레이어로 바꾼다
        // - 가만히 있을 땐 기본(idle) 애니, 이동할 땐 jump(없으면 Run) 애니로 배회한다
        public void SetupCollectionMode(IReadOnlyList<Bounds> roamAreas)
        {
            _roamAreas = roamAreas;

            // 기계용 물리가 동작하지 않도록 고정 (배회 이동은 transform으로 직접)
            if (TryGetComponent(out Rigidbody body))
            {
                // kinematic 바디는 Discrete/ContinuousSpeculative만 지원 - 프리팹 기본값이
                // ContinuousDynamic이라 isKinematic보다 먼저 모드를 내리지 않으면
                // 'Kinematic body only supports Speculative...' 에러가 인형마다 출력된다
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
                body.useGravity = false;
            }

            // 하위 모든 Renderer 오브젝트를 collection_doll 레이어로 (도감 카메라 렌더링용)
            int layer = LayerMask.NameToLayer(CollectionDollLayerName);
            if (layer >= 0)
            {
                gameObject.layer = layer;
                foreach (var childRenderer in GetComponentsInChildren<Renderer>(true))
                    childRenderer.gameObject.layer = layer;
            }
            else
            {
                Debug.LogError($"[CollectionActor] '{CollectionDollLayerName}' 레이어가 프로젝트에 없습니다.");
            }

            Animation.CacheAnimator(gameObject);
        }

        // 기본(idle) 스테이트는 이름이 모델/컨트롤러마다 다를 수 있으므로,
        // 초기화 직후 "현재 재생 중인 스테이트"를 그대로 idle로 캡처한다.
        private void CaptureAnimatorStates()
        {
            if (_animator == null)
                return;

            if (_idleStateHash != 0)
                return;

            _idleStateHash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

            foreach (string candidate in MoveStateCandidates)
            {
                int hash = Animator.StringToHash(candidate);
                if (_animator.HasState(0, hash))
                {
                    _moveStateHash = hash;
                    break;
                }
            }
        }

        // Idle 대기 시간이 끝났을 때 호출된다(ActorIdleState). 배회 목적지를 찾으면 Move로 전환한다.
        // 유효한 목적지를 못 찾으면 false를 반환해 다시 idle 대기(재시도)하게 한다.
        public override float GetIdleDuration() => Random.Range(ROAM_IDLE_MIN, ROAM_IDLE_MAX);

        public override bool OnIdleComplete()
        {
            if (!TryPickNextTarget(out Vector3 target))
                return false;

            MoveTargetPosition = target;
            StateMachine.ChangeState(ActorStateType.Move);
            return true;
        }

        // Move 상태 도착 시(ActorMoveState) 호출된다. 다시 idle 대기로 돌아간다.
        public override void OnMoveArrived()
        {
            StateMachine.ChangeState(ActorStateType.Idle);
        }

        // 현재 위치에서 가까운 랜덤 지점을 고른다. 배회 영역(플레인) 안쪽 지점만 채택한다.
        private bool TryPickNextTarget(out Vector3 target)
        {
            Vector3 pos = transform.position;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 dir = Random.insideUnitCircle;
                if (dir.sqrMagnitude < 0.001f)
                    dir = Vector2.right;
                dir.Normalize();

                float dist = Random.Range(ROAM_HOP_MIN, ROAM_HOP_MAX);
                Vector3 candidate = new Vector3(pos.x + dir.x * dist, pos.y, pos.z + dir.y * dist);

                if (IsInsideRoamArea(candidate, ROAM_AREA_MARGIN) && IsPathInsideRoamArea(pos, candidate))
                {
                    target = candidate;
                    return true;
                }
            }

            target = default;
            return false;
        }

        // 후보 지점이 배회 영역(플레인 union) 안쪽인지 검사한다. (margin = 가장자리 여유)
        private bool IsInsideRoamArea(Vector3 point, float margin)
        {
            if (_roamAreas == null)
                return false;

            for (int i = 0; i < _roamAreas.Count; i++)
            {
                Bounds area = _roamAreas[i];
                if (point.x >= area.min.x + margin && point.x <= area.max.x - margin
                    && point.z >= area.min.z + margin && point.z <= area.max.z - margin)
                    return true;
            }

            return false;
        }

        // 이동 경로(선분)가 플레인 밖(구멍)을 지나지 않는지 일정 간격으로 샘플링 검사한다.
        // (플레인들이 고리형으로 배치되어 union 안쪽에 미커버 구멍이 있을 수 있음 -
        //  목적지만 검사하면 인형이 구멍 위를 걸어서 통과한다. 경로 검사는 좁은 이음새를
        //  과도하게 거부하지 않도록 마진 0으로 수행)
        private bool IsPathInsideRoamArea(Vector3 from, Vector3 to)
        {
            const float sampleStep = 0.5f;

            float dist = Vector3.Distance(from, to);
            int steps = Mathf.CeilToInt(dist / sampleStep);
            for (int i = 1; i <= steps; i++)
            {
                if (!IsInsideRoamArea(Vector3.Lerp(from, to, (float)i / steps), 0f))
                    return false;
            }

            return true;
        }
    }
}
