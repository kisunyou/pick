using JetBrains.Annotations;
using Unity.VisualScripting;
using UnityEngine;

namespace FunRabbit
{
    public class CraneMovingControl
    {
        // 크레인 이동(수동 XZ / 상승 / 하강) 중 루프 재생할 이동음
        const string CraneMoveSoundName = "game_sounds/ui/stick_go_1";

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
            // Grap 스프링 램프 등 CraneTransform의 물리 스텝 갱신
            _crane.CraneTransform?.ManualFixedUpdate();

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

            UpdateMoveSound();
        }

        // 이동음 판정: 입력/상태 플래그가 아니라 pivot의 "실제 이동량"으로 판단한다.
        // 플래그 방식은 하강 후 바닥에 걸려 멈춘 상태(플래그 true, 실제 정지)나
        // 자동 복귀 이동(플래그 없음, 실제 이동)에서 실제 움직임과 어긋난다.
        // 순간 속도가 아니라 "정지 이후 누적 이동 거리"로 판단해, 물리 잔떨림 같은
        // 미세한 순간 이동에는 소리가 나지 않고 실제로 일정 거리 이상 움직여야 재생된다.
        const float MoveSoundDistanceThreshold = 0.17f;  // 이동음이 시작되는 데 필요한 누적 이동 거리(units)
        const float MoveSoundStillStepDistance = 0.001f; // 이 거리 미만이면 해당 물리 스텝은 "정지"로 간주

        private Vector3 _lastPivotPosition;
        private bool _hasLastPivotPosition;
        private float _accumulatedMoveDistance;
        private bool _isMoveSoundPlaying;

        // 크레인이 실제로 움직이는 동안(상하좌우/자동 복귀 포함) 이동음을 루프 재생하고,
        // 멈추면 정지한다. (AudioManager가 같은 클립 중복 재생을 막아줘 매 스텝 호출해도 안전)
        private void UpdateMoveSound()
        {
            var audio = AudioManager.Instance;
            if (audio == null)
                return;

            Vector3 pivotPos = _crane.CraneTransform.PivotPosition;
            if (!_hasLastPivotPosition)
            {
                _lastPivotPosition = pivotPos;
                _hasLastPivotPosition = true;
                return;
            }

            // 직전 물리 스텝 동안 실제로 이동한 거리
            float stepDistance = (pivotPos - _lastPivotPosition).magnitude;
            _lastPivotPosition = pivotPos;

            if (stepDistance < MoveSoundStillStepDistance)
            {
                // 사실상 정지 - 누적치를 초기화하고 소리를 멈춘다 (다음 이동은 다시 거리를 채워야 함)
                _accumulatedMoveDistance = 0f;
                if (_isMoveSoundPlaying)
                {
                    _isMoveSoundPlaying = false;
                    audio.StopLoopSfx();
                }
                return;
            }

            _accumulatedMoveDistance += stepDistance;

            if (!_isMoveSoundPlaying && _accumulatedMoveDistance >= MoveSoundDistanceThreshold)
            {
                _isMoveSoundPlaying = true;
                audio.PlayLoopSfx(CraneMoveSoundName);
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

        // 들어올린 뒤 집게가 인형을 실제로 잡고 있는지 (근접 판정)
        public bool IsHoldingDoll(float radius)
        {
            return _crane.CraneTransform.IsHoldingDoll(radius);
        }

        /// <summary>
        /// 목표 XZ 위치로 등속 이동. 도착 시 true 반환.
        /// MovePosition 프레임 이동 방식은 모바일(저프레임)에서 덜컹거림과
        /// 목표 주변 오버슛 진동(도착 실패)을 만들어 velocity 방식으로 이동한다.
        /// speedRatio : 기본 XZ 속도 대비 배율 (기본 0.9)
        /// </summary>
        public bool MoveTowardXZ(Vector3 targetXZ, float speedRatio = 0.9f)
        {
            const float threshold = 0.05f;

            return _crane.CraneTransform.MoveTowardXZVelocity(targetXZ, speedRatio, threshold);
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
