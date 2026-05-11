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
                        _crane.CraneTransform.MoveXZStart();
                    else
                        _crane.CraneTransform.MoveXZEnd();

                    _isAnyMoving = anyMoving;
                }
            }
        }



        private void UpdateControlMoving()
        {
            // W
            if (Input.GetKeyDown(KeyCode.W)) SetMovingValue(0, true);
            if (Input.GetKeyUp(KeyCode.W)) SetMovingValue(0, false);

            // S
            if (Input.GetKeyDown(KeyCode.S)) SetMovingValue(1, true);
            if (Input.GetKeyUp(KeyCode.S)) SetMovingValue(1, false);

            // A
            if (Input.GetKeyDown(KeyCode.A)) SetMovingValue(2, true);
            if (Input.GetKeyUp(KeyCode.A)) SetMovingValue(2, false);

            // D
            if (Input.GetKeyDown(KeyCode.D)) SetMovingValue(3, true);
            if (Input.GetKeyUp(KeyCode.D)) SetMovingValue(3, false);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _crane.StartGrabSequence();
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
    }
}
