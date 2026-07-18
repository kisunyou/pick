using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class Actor : MonoBehaviour
    {
        // 실제 인형뽑기 인형처럼 "묵직하게" 가라앉고, 크레인/바닥에서 튕겨 날아가지 않도록
        // 런타임에 물리 감쇠 값을 일괄 적용한다.
        // (인형 프리팹이 11종으로 흩어져 있어, 값이 제각각이 되지 않도록 한 곳에서 관리)
        const float LINEAR_DAMPING = 0.5f;            // 이동 감쇠: 멀리 미끄러지거나 날아가지 않게
        const float ANGULAR_DAMPING = 1.0f;           // 회전 감쇠: 마구 구르지 않고 무게감 있게 정지
        // 겹침(끼임) 해소 속도 상한. 크레인 하강 속도(≈2.94 m/s)보다 작으면 인형이
        // 밀려나는 속도보다 클로가 파고드는 속도가 빨라 겹침(뚫림)이 계속 커진다.
        // 반드시 하강 속도보다 크게 유지할 것. (튕김은 damping 0.5/1.0이 억제)
        const float MAX_DEPENETRATION_VELOCITY = 4f;

        // ── 컬렉션(도감) 배회 모드 설정 ─────────────────────────────
        const string CollectionDollLayerName = "collection_doll";

        // 이동 애니메이션 스테이트 후보. 앞에서부터 존재하는 첫 스테이트를 사용한다.
        // (현재 공유 컨트롤러 model_base_anim_ctrl에는 jump가 없어 Run이 사용된다.
        //  컨트롤러에 jump 스테이트를 추가하면 코드 수정 없이 jump가 우선 적용된다)
        static readonly string[] MoveStateCandidates = { "jump", "Jump", "Run" };

        const float ROAM_MOVE_SPEED = 1.5f;   // 배회 이동 속도 (units/s)
        const float ROAM_TURN_SPEED = 8f;     // 진행 방향으로 도는 속도
        const float ROAM_IDLE_MIN = 1.5f;     // idle 최소 대기(초)
        const float ROAM_IDLE_MAX = 4f;       // idle 최대 대기(초)
        const float ROAM_HOP_MIN = 2f;        // 한 번에 이동하는 최소 거리
        const float ROAM_HOP_MAX = 5f;        // 한 번에 이동하는 최대 거리
        const float ROAM_AREA_MARGIN = 0.8f;  // 플레인 가장자리 여유 (밖으로 삐져나가지 않게)
        const float ARRIVE_THRESHOLD = 0.05f; // 도착 판정 거리

        public DollData Data { get; set; }

        // 컬렉션(도감) 배회 인형인지 (터치 판정 등에서 기계 인형과 구분용)
        public bool IsCollectionMode => _isCollectionMode;

        private bool _isCollectionMode;
        private IReadOnlyList<Bounds> _roamAreas; // 배회 가능 영역 (CollectionManager가 전달, 공유 리스트)
        private Animator _animator;
        private int _idleStateHash;
        private int _moveStateHash;
        private int _currentStateHash;

        private void Start()
        {
            // 컬렉션 배회 인형은 기계 인형용 물리/스테이지 등록을 하지 않는다
            // (StageManager에 등록되면 크레인 잡힘 판정/리셋버튼 카운트를 오염시킨다)
            if (_isCollectionMode)
                return;

            ApplyDollPhysics();
            StageManager.AddActor(this);
        }

        // 인형 루트의 Rigidbody에 무게감/안정성 위주의 물리 값을 적용한다.
        // Rigidbody가 없는 오브젝트(전시용 프리팹 등)는 그대로 무시한다.
        private void ApplyDollPhysics()
        {
            if (!TryGetComponent(out Rigidbody body))
                return;

            body.linearDamping = LINEAR_DAMPING;
            body.angularDamping = ANGULAR_DAMPING;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // 크레인 집게가 빠르게 닫히거나 하강할 때 인형을 뚫고 지나가는(터널링) 현상을
            // 막기 위해, 가장 터널링에 강한 ContinuousSpeculative로 설정한다.
            // (크레인 바디 쪽은 CraneTransform 생성자에서 동일하게 설정)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = MAX_DEPENETRATION_VELOCITY;
        }

        private void OnDestroy()
        {
            StageManager.RemoveActor(this);
        }

        // 컬렉션 인형이 다시 활성화(도감 재진입)되면 배회를 재개한다.
        // (SetActive(false)로 중단된 코루틴은 재활성화해도 자동 재개되지 않는다.
        //  최초 생성 시점의 OnEnable은 _isCollectionMode가 아직 false라 아무것도 하지 않고,
        //  그때는 SetupCollectionMode가 직접 코루틴을 시작한다)
        private void OnEnable()
        {
            if (_isCollectionMode)
                StartCoroutine(RoamCoroutine());
        }

        // ── 컬렉션(도감) 배회 모드 ──────────────────────────────────
        // Instantiate 직후(Start 이전)에 호출한다.
        // - 물리를 멈추고(kinematic) transform으로 직접 이동한다
        // - 하위 모든 Renderer 오브젝트를 collection_doll 레이어로 바꾼다
        // - 가만히 있을 땐 기본(idle) 애니, 이동할 땐 jump(없으면 Run) 애니로 배회한다
        public void SetupCollectionMode(IReadOnlyList<Bounds> roamAreas)
        {
            _isCollectionMode = true;
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
                Debug.LogError($"[Actor] '{CollectionDollLayerName}' 레이어가 프로젝트에 없습니다.");
            }

            _animator = GetComponentInChildren<Animator>();

            StartCoroutine(RoamCoroutine());
        }

        // idle 대기 → 근처 랜덤 지점으로 이동(jump/Run 애니) → 다시 idle 을 반복한다.
        private IEnumerator RoamCoroutine()
        {
            // Animator가 기본 스테이트로 초기화될 시간을 준 뒤 스테이트를 캡처한다
            yield return null;
            CaptureAnimatorStates();

            while (true)
            {
                // 가만히 있는 동안은 기본(idle) 애니메이션
                PlayState(_idleStateHash);
                yield return new WaitForSeconds(Random.Range(ROAM_IDLE_MIN, ROAM_IDLE_MAX));

                // 다음 목적지 선택 (영역 밖만 걸리면 이번 턴은 쉼)
                if (!TryPickNextTarget(out Vector3 target))
                    continue;

                // 이동 애니메이션으로 전환 후 목적지까지 등속 이동
                PlayState(_moveStateHash);

                while (true)
                {
                    Vector3 toTarget = target - transform.position;
                    toTarget.y = 0f;

                    if (toTarget.sqrMagnitude <= ARRIVE_THRESHOLD * ARRIVE_THRESHOLD)
                        break;

                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(toTarget.normalized), Time.deltaTime * ROAM_TURN_SPEED);
                    transform.position = Vector3.MoveTowards(transform.position, target, ROAM_MOVE_SPEED * Time.deltaTime);

                    yield return null;
                }
            }
        }

        // 기본(idle) 스테이트는 이름이 모델/컨트롤러마다 다를 수 있으므로,
        // 초기화 직후 "현재 재생 중인 스테이트"를 그대로 idle로 캡처한다.
        // (재활성화로 코루틴이 재시작될 때는 캡처를 건너뛰고 현재 상태만 idle로 되돌린다)
        private void CaptureAnimatorStates()
        {
            if (_animator == null)
                return;

            if (_idleStateHash == 0)
            {
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

            // 비활성화됐다 재활성화되면 Animator는 기본 스테이트부터 다시 시작한다
            _currentStateHash = _idleStateHash;
        }

        // 지정 스테이트로 크로스페이드한다. (같은 스테이트로의 중복 전환은 무시)
        // CrossFade(normalized)는 블렌드 길이가 클립 길이에 비례해 모델마다 달라지므로,
        // 고정 시간(초) 버전을 사용해 전 인형이 일정한 전환 속도를 갖게 한다.
        private void PlayState(int stateHash)
        {
            if (_animator == null || stateHash == 0 || stateHash == _currentStateHash)
                return;

            _currentStateHash = stateHash;
            _animator.CrossFadeInFixedTime(stateHash, 0.15f);
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
