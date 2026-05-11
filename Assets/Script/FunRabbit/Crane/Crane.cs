using System.Collections;
using System.Linq;
using UnityEngine;

namespace FunRabbit
{
    [RequireComponent(typeof(CraneTransform))]
    public class Crane : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Rigidbody pivotRigidbody;
        [SerializeField] Rigidbody[] craneRigidbodys;

        [Header("Down / Return Settings")]
        [Tooltip("로프 끝이 닿아야 할 Y 높이")]
        [SerializeField] float targetDownHeight = 1.0f;
        [Tooltip("수평 복귀 시 도달해야 할 XZ 위치 (로프 끝)")]
        [SerializeField] Vector3 returnPositionXZ;

        private int _status = CraneStatus.READY;

        public int Status
        {
            get => _status;
            private set => _status = value;
        }

        public CraneTransform CraneTransform { get; private set; }

        private CraneMovingControl _craneMovingControl;

        private Vector3 _initialLopPosition;
        
        // MOVING_UP 상태를 위한 코루틴
        private Coroutine _movingUpCoroutine;
        
        // GRAP 상태에서 한 번만 실행되도록 하는 플래그
        private bool _hasGrapStarted = false;

        private float _checkTimer = 0.0f;

        void Start()
        {
            _craneMovingControl = new CraneMovingControl(this);
            // 상태 초기화
            _status = CraneStatus.CONTROL_MOVING;

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
                    else if(_checkTimer > 2.0f)
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

                    if (_craneMovingControl.IsArriveMovingUp())
                    {
                        _craneMovingControl.MovingUpStop();
                        SetStatus(CraneStatus.DROP);
                    }
                    break;

                case CraneStatus.MOVING_RETURN:
                    //// X축 복귀
                    //float dx = returnPositionXZ.x - lop.position.x;
                    //if (Mathf.Abs(dx) > 0.05f)
                    //{
                    //    if (dx > 0) CraneTransform.MoveRight();
                    //    else CraneTransform.MoveLeft();
                    //    break;
                    //}

                    //// Z축 복귀
                    //float dz = returnPositionXZ.z - lop.position.z;
                    //if (Mathf.Abs(dz) > 0.05f)
                    //{
                    //    if (dz > 0) CraneTransform.MoveFront();
                    //    else CraneTransform.MoveBack();
                    //    break;
                    //}

                    //// 복귀 완료
                    //SetStatus(CraneStatus.DROP);
                    break;

                case CraneStatus.DROP:
                    _craneMovingControl.Release();
                    SetStatus(CraneStatus.CONTROL_MOVING);
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

        private void SetStatus(int status)
        {
            // 상태가 변경될 때 플래그 리셋
            if (_status != status)
            {
                _hasGrapStarted = false;
            }
            
            _status = status;
            Debug.Log($"[Crane] 상태 전환: {status}");
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
