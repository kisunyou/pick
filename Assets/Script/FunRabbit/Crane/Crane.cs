using System.Collections;
using System.Linq;
using UnityEngine;

namespace FunRabbit
{
    [RequireComponent(typeof(CraneTransform))]
    public class Crane : InstanceSetter<Crane>
    {
        [Header("References")]
        [SerializeField] Rigidbody pivotRigidbody;
        [SerializeField] Rigidbody[] craneRigidbodys;

        [Header("Down / Return Settings")]
        [Tooltip("로프 끝이 닿아야 할 Y 높이")]
        [SerializeField] float targetDownHeight = 1.0f;
        [Tooltip("수평 복귀 시 도달해야 할 XZ 위치 (로프 끝)")]
        [SerializeField] Vector3 returnPositionXZ;

        [Header("Grab Check")]
        [Tooltip("들어올린 뒤 집게 중심부 이 반경 안에 인형이 있으면 '잡은 것'으로 판정. 인형 크기에 맞춰 조절")]
        [SerializeField] float holdCheckRadius = 1.5f;

        private int _status = CraneStatus.READY;

        public int Status
        {
            get => _status;
            private set => _status = value;
        }

        // 크레인 상태 변경 이벤트 (GameMain.OnChangedStatus와 동일한 패턴)
        public System.Action<int> OnChangedStatus { get; set; }

        public CraneTransform CraneTransform { get; private set; }

        private CraneMovingControl _craneMovingControl;

        private Vector3 _initialLopPosition;

        // MOVING_UP 상태를 위한 코루틴
        private Coroutine _movingUpCoroutine;

        // GRAP 상태에서 한 번만 실행되도록 하는 플래그
        private bool _hasGrapStarted = false;

        // DROP 상태에서 집게를 한 번만 열도록 하는 플래그
        private bool _hasReleased = false;

        private float _checkTimer = 0.0f;

        void Start()
        {
            _craneMovingControl = new CraneMovingControl(this);
            // 상태 초기화
            //_status = CraneStatus.CONTROL_MOVING;

            // CraneTransform 세팅
            CraneTransform = new CraneTransform(craneRigidbodys, pivotRigidbody);

            // 로프 끝(initial) 위치 기록
            _initialLopPosition = craneRigidbodys[0].position;

            // 기본 복귀 위치는 시작 위치와 동일하게
            returnPositionXZ = new Vector3(_initialLopPosition.x, 0, _initialLopPosition.z);
        }

        private void Update()
        {
            _craneMovingControl.ManualUpdate();

            var lop = craneRigidbodys[0];
            switch (_status)
            {
                case CraneStatus.MOVING_DOWN:
                    _checkTimer += Time.deltaTime;

                    if (_checkTimer > 3.0f)
                    {
                        _checkTimer = 0.0f;
                        _craneMovingControl.Grap();
                        SetStatus(CraneStatus.GRAP);
                    }
                    else if (_checkTimer > 2.0f)
                    {
                        _craneMovingControl.MovingDownStart();
                    }
                    break;

                case CraneStatus.GRAP:
                    _checkTimer += Time.deltaTime;
                    if (_checkTimer >= 2.0f)
                    {
                        _checkTimer = 0.0f;
                        _craneMovingControl.MovingUpStart();
                        SetStatus(CraneStatus.MOVING_UP);
                    }
                    break;

                case CraneStatus.MOVING_UP:
                    if (_checkTimer > 0)
                    {
                        _checkTimer -= Time.deltaTime;
                        if (_checkTimer <= 0.0f)
                        {
                            _checkTimer = 0.0f;

                            // 들어올린 뒤 인형을 실제로 잡았는지 확인한다.
                            // - 잡았으면: 출구로 옮긴다 (MOVING_RETURN)
                            // - 못 잡았으면: 출구로 가지 않고 곧장 집게를 열고 중간 위치로 복귀한다
                            //   (DROP 상태가 Release → 중간 위치 이동 → READY 를 처리)
                            if (_craneMovingControl.IsHoldingDoll(holdCheckRadius))
                            {
                                SetStatus(CraneStatus.MOVING_RETURN);
                            }
                            else
                            {
                                SetStatus(CraneStatus.DROP);
                            }
                        }
                    }
                    else
                    {
                        if (_craneMovingControl.IsArriveMovingUp())
                        {
                            _checkTimer = 0.5f;
                            _craneMovingControl.MovingUpStop();

                        }
                    }

                    break;

                case CraneStatus.MOVING_RETURN:
                    if (_checkTimer > 0)
                    {
                        _checkTimer -= Time.deltaTime;
                        if (_checkTimer <= 0.0f)
                        {
                            SetStatus(CraneStatus.DROP);
                            _checkTimer = 0.0f;
                        }
                    }
                    else if (GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos))
                    {
                        Vector3 returnTarget = new Vector3(
                            checkPos.ReturnPosition.position.x,
                            0f,
                            checkPos.ReturnPosition.position.z
                        );

                        if (_craneMovingControl.IsMoveXZStarted() == false)
                        {
                            _craneMovingControl.MoveXZStart();
                        }

                        bool arrived = _craneMovingControl.MoveTowardXZ(returnTarget);
                        if (arrived)
                        {
                            _checkTimer = 0.5f;
                            _craneMovingControl.MoveXZEnd();
                        }
                    }
                    break;

                case CraneStatus.DROP:
                    if (!_hasReleased)
                    {
                        // 1) 집게를 열어 인형을 떨어뜨린다
                        _craneMovingControl.Release();
                        _hasReleased = true;
                        _checkTimer = 0.5f;
                    }
                    else if (_checkTimer > 0)
                    {
                        // 2) 인형이 빠질 때까지 잠시 대기
                        _checkTimer -= Time.deltaTime;
                    }
                    else
                    {
                        // 3) 중간(시작) 위치로 이동 후 READY 로 전환
                        Vector3 centerTarget = new Vector3(
                            CraneTransform.StartPivotPosition.x,
                            0f,
                            CraneTransform.StartPivotPosition.z
                        );

                        if (_craneMovingControl.IsMoveXZStarted() == false)
                        {
                            _craneMovingControl.MoveXZStart();
                        }

                        if (_craneMovingControl.MoveTowardXZ(centerTarget))
                        {
                            _craneMovingControl.MoveXZEnd();
                            SetStatus(CraneStatus.READY);
                            StageManager.Save(GameQuestManager.Instance.CurrentStage);
                        }
                    }
                    break;

                case CraneStatus.READY:
                default:
                    // 아무 동작도 하지 않음
                    break;
            }
        }

        void FixedUpdate()
        {
            _craneMovingControl.ManualFixedUpdate();
        }

        /// <summary>
        /// MOVING_UP 시퀀스를 시작 (2초 대기 후 위로 이동)
        /// </summary>
        private void StartMovingUpSequence()
        {
            if (_movingUpCoroutine != null)
            {
                StopCoroutine(_movingUpCoroutine);
            }

            _movingUpCoroutine = StartCoroutine(MovingUpCoroutine());
        }

        /// <summary>
        /// MOVING_UP 상태 처리 코루틴
        /// </summary>
        private IEnumerator MovingUpCoroutine()
        {
            Debug.Log("[Crane] GRAP 완료, 2초 대기 시작...");

            // 1. 2초 대기
            yield return new WaitForSeconds(2.0f);

            Debug.Log("[Crane] 2초 대기 완료, MOVING_UP 시작");
            SetStatus(CraneStatus.MOVING_UP);

            // 2. 최대 높이까지 올라가기
            var lop = craneRigidbodys[0];

            while (true)
            {
                // 위로 이동
                CraneTransform.OnMoveUp();

                // 최대 높이 도달 체크
                if (lop.position.y >= _initialLopPosition.y)
                {
                    Debug.Log($"[Crane] 최대 높이 도달! 현재: {lop.position.y}, 초기: {_initialLopPosition.y}");
                    break;
                }

                // 추가 안전 체크: 초기 위치보다 더 높이 올라갔을 경우
                if (lop.position.y >= _initialLopPosition.y + 1.0f)
                {
                    Debug.Log($"[Crane] 안전 높이 초과! 강제 정지");
                    break;
                }

                yield return null; // 다음 프레임까지 대기
            }

            CraneTransform.MoveXZEnd();


            Debug.Log("[Crane] MOVING_UP 완료, MOVING_RETURN으로 전환");
            SetStatus(CraneStatus.MOVING_RETURN);

            _movingUpCoroutine = null;
        }

        /// <summary>
        /// 외부에서 그랩 시퀀스를 시작할 때 호출
        /// </summary>
        public void StartGrabSequence()
        {
            if (_status == CraneStatus.CONTROL_MOVING)
            {
                _craneMovingControl.MovingDownStart();
                SetStatus(CraneStatus.MOVING_DOWN);
            }
        }

        public void SetStatus(int status)
        {
            // 상태가 변경될 때 플래그 리셋
            if (_status != status)
            {
                _hasGrapStarted = false;
                _hasReleased = false;
            }

            _status = status;
            Debug.Log($"[Crane] 상태 전환: {status}");

            OnChangedStatus?.Invoke(status);
        }

        // 상태 변경 구독 (+ 현재 상태를 즉시 1회 반영)
        public void SubscribeStatus(System.Action<int> handler)
        {
            OnChangedStatus -= handler;
            OnChangedStatus += handler;

            handler(_status);
        }

        public void UnsubscribeStatus(System.Action<int> handler)
        {
            OnChangedStatus -= handler;
        }

        /// <summary>
        /// 컴포넌트 비활성화 시 실행 중인 코루틴 정리
        /// </summary>
        private void OnDisable()
        {
            if (_movingUpCoroutine != null)
            {
                StopCoroutine(_movingUpCoroutine);
                _movingUpCoroutine = null;
            }
        }
    }
}
