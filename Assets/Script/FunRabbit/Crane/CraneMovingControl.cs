using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;

namespace FunRabbit
{
    public class CraneMovingControl
    {
        private Crane _crane;
        private bool[] _isMovingValues = new bool[4];
        private bool _isAnyMoving = false;

        private bool _isMovingDown = false;
        public bool IsMovingDown
        {
            get { return _isMovingDown; }
            set { _isMovingDown = value; }
        }

        private bool _isMovingUp = false;
        public bool IsMoveingUp
        {
            get { return _isMovingUp; }
            set { _isMovingUp = value; }
        }

        private Vector2 _dpadDelta = Vector2.zero;

        private enum InputMode { None, Keyboard, DPad }
        private InputMode _inputMode = InputMode.None;
        private int _keyboardActiveCount = 0; // 현재 눌린 키 개수

        public CraneMovingControl([NotNull] Crane crane)
        {
            _crane = crane;
            
        }

        public void SetMovingValue(int index, bool value)
        {
            if (_isMovingValues[index] != value)
            {
                _isMovingValues[index] = value;

                bool anyMoving = false;
                for (int i = 0; i < _isMovingValues.Length; i++)
                {
                    if (_isMovingValues[i])
                    {
                        anyMoving = true;
                        break;
                    }
                }

                // 상태가 바뀐 경우에만 호출
                if (anyMoving != _isAnyMoving)
                {
                    if (anyMoving)
                        MoveXZStart();
                    else
                        MoveXZEnd();

                    _isAnyMoving = anyMoving;
                }
            }
        }

        public void MoveXZStart()
        {
            _crane.CraneTransform.MoveXZStart();
        }

        public bool IsMoveXZStarted()
        {
            return _crane.CraneTransform.IsMoveXZStarted();
        }

        public void MoveXZEnd()
        {
            _crane.CraneTransform.MoveXZEnd();
        }

        private void UpdateControlMoving()
        {
            // Space는 모드 무관하게 항상 처리
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _crane.StartGrabSequence();
            }

            // ── 키보드 입력 감지 ──────────────────────────────
            if (_inputMode != InputMode.DPad)
            {
                bool anyKeyEvent = false;

                if (Input.GetKeyDown(KeyCode.W)) { SetMovingValue(0, true); _keyboardActiveCount++; anyKeyEvent = true; }
                if (Input.GetKeyDown(KeyCode.S)) { SetMovingValue(1, true); _keyboardActiveCount++; anyKeyEvent = true; }
                if (Input.GetKeyDown(KeyCode.A)) { SetMovingValue(2, true); _keyboardActiveCount++; anyKeyEvent = true; }
                if (Input.GetKeyDown(KeyCode.D)) { SetMovingValue(3, true); _keyboardActiveCount++; anyKeyEvent = true; }

                if (Input.GetKeyUp(KeyCode.W)) { SetMovingValue(0, false); _keyboardActiveCount = Mathf.Max(0, _keyboardActiveCount - 1); anyKeyEvent = true; }
                if (Input.GetKeyUp(KeyCode.S)) { SetMovingValue(1, false); _keyboardActiveCount = Mathf.Max(0, _keyboardActiveCount - 1); anyKeyEvent = true; }
                if (Input.GetKeyUp(KeyCode.A)) { SetMovingValue(2, false); _keyboardActiveCount = Mathf.Max(0, _keyboardActiveCount - 1); anyKeyEvent = true; }
                if (Input.GetKeyUp(KeyCode.D)) { SetMovingValue(3, false); _keyboardActiveCount = Mathf.Max(0, _keyboardActiveCount - 1); anyKeyEvent = true; }

                if (anyKeyEvent)
                {
                    _inputMode = _keyboardActiveCount > 0 ? InputMode.Keyboard : InputMode.None;
                    return; // 키보드 이벤트 발생 시 DPad 처리 스킵
                }
            }

            // 키보드 모드 중이면 DPad 처리 안 함
            if (_inputMode == InputMode.Keyboard)
                return;

            // ── DPad 입력 ────────────────────────────────────
            UIDpad dpad = UIDpad.Get();
            if (dpad != null && dpad.IsDragging())
            {
                _dpadDelta = dpad.GetDelta();

                if (_dpadDelta != Vector2.zero)
                {
                    if (_inputMode != InputMode.DPad)
                    {
                        _inputMode = InputMode.DPad;
                        MoveXZStart();
                        _isAnyMoving = true;
                    }
                }
                else
                {
                    if (_inputMode == InputMode.DPad)
                    {
                        _inputMode = InputMode.None;
                        MoveXZEnd();
                        _isAnyMoving = false;
                    }
                }
            }
            else
            {
                _dpadDelta = Vector2.zero;

                if (_inputMode == InputMode.DPad)
                {
                    _inputMode = InputMode.None;
                    MoveXZEnd();
                    _isAnyMoving = false;
                }
            }
        }

        public void ManualUpdate()
        {
            if (_crane.Status == CraneStatus.CONTROL_MOVING)
            {
                this.UpdateControlMoving();
            }
        }

        public void ManualFixedUpdate()
        {
            if (_isMovingValues[0]) _crane.CraneTransform.MoveFront();
            if (_isMovingValues[1]) _crane.CraneTransform.MoveBack();
            if (_isMovingValues[2]) _crane.CraneTransform.MoveLeft();
            if (_isMovingValues[3]) _crane.CraneTransform.MoveRight();

            if (_isMovingUp) _crane.CraneTransform.OnMoveUp();
            if (_isMovingDown) _crane.CraneTransform.OnMoveDown();

            // DPad 입력 - Delta.y = 앞/뒤(Z), Delta.x = 좌/우(X)
            if (_dpadDelta != Vector2.zero)
            {
                if (_dpadDelta.y > 0f) _crane.CraneTransform.MoveFront(_dpadDelta.y);
                else if (_dpadDelta.y < 0f) _crane.CraneTransform.MoveBack(-_dpadDelta.y);

                if (_dpadDelta.x > 0f) _crane.CraneTransform.MoveRight(_dpadDelta.x);
                else if (_dpadDelta.x < 0f) _crane.CraneTransform.MoveLeft(-_dpadDelta.x);
            }
        }

        public void MovingDownStart()
        {
            _isMovingDown = true;
            _crane.CraneTransform.OnMoveDownStart();
        }

        public void MovingDownStop()
        {
            _isMovingDown = false;
        }

        public void Grap()
        {
            _crane.CraneTransform.Grap();
        }

        public void Release()
        {
            _crane.CraneTransform.Release();
        }

        public void MovingUpStart()
        {
            _isMovingUp = true;
            _crane.CraneTransform.OnStartMoveUp();
        }

        public bool IsArriveMovingUp()
        {
            return _crane.CraneTransform.IsArriveMovingUp();
        }

        public void MovingUpStop()
        {
            _isMovingUp = false;
            _crane.CraneTransform.MoveUpEnd();
        }

        /// <summary>
        /// 목표 XZ 위치로 한 프레임씩 이동. 도착 시 true 반환.
        /// x, z 속도를 남은 거리 비율로 나눠 직선(대각선)으로 이동하므로
        /// 두 축이 동시에 도착한다. (한 축만 먼저 끝나는 ㄱ자 이동 방지)
        /// </summary>
        public bool MoveTowardXZ(Vector3 targetXZ)
        {
            Vector3 pivotPos = _crane.CraneTransform.PivotPosition;
            float dx = targetXZ.x - pivotPos.x;
            float dz = targetXZ.z - pivotPos.z;

            const float threshold = 0.05f;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance <= threshold)
                return true;

            float speed = 0.7f;
            // 목표 방향 단위 벡터 비율만큼 각 축 속도를 배분 → x, z 동시 도착
            float xMul = speed * Mathf.Abs(dx) / distance;
            float zMul = speed * Mathf.Abs(dz) / distance;

            if (dx > 0f) _crane.CraneTransform.MoveRight(xMul);
            else if (dx < 0f) _crane.CraneTransform.MoveLeft(xMul);

            if (dz > 0f) _crane.CraneTransform.MoveFront(zMul);
            else if (dz < 0f) _crane.CraneTransform.MoveBack(zMul);

            return false;
        }

        /// <summary>
        /// XZ 이동 완전 정지
        /// </summary>
        public void StopXZMove()
        {
            _crane.CraneTransform.MoveXZEnd();
        }
    }
}
