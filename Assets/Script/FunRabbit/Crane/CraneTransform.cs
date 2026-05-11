using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace FunRabbit
{
    public class CraneTransform
    {
        private Rigidbody _pivotRigidbody;
        private Rigidbody[] _craneRigidbodys;
        private HingeJoint[] _craneHingeJoints;

        private Vector3 _startPivotPosition;

        public CraneTransform(Rigidbody[] craneRigidbodys, Rigidbody pivotRigidbodys)
        {
            this._craneRigidbodys = craneRigidbodys;
            this._pivotRigidbody = pivotRigidbodys;

            _startPivotPosition = this._pivotRigidbody.transform.position;

            _craneHingeJoints = new HingeJoint[3];
            _craneHingeJoints[0] = craneRigidbodys[1].GetComponent<HingeJoint>();
            _craneHingeJoints[1] = craneRigidbodys[2].GetComponent<HingeJoint>();
            _craneHingeJoints[2] = craneRigidbodys[3].GetComponent<HingeJoint>();
        }

        public void MoveLeft()
        {
            Vector3 moveValue = Vector3.zero;
            moveValue = -Vector3.right * Time.deltaTime * GameMain.Instance.HorizontalSpeed;
            MoveXZ(moveValue);
        }

        public void MoveRight()
        {
            Vector3 moveValue = Vector3.zero;
            moveValue = Vector3.right * Time.deltaTime * GameMain.Instance.HorizontalSpeed;
            MoveXZ(moveValue);
        }

        public void MoveFront()
        {
            Vector3 moveValue = Vector3.zero;
            moveValue = Vector3.forward * Time.deltaTime * GameMain.Instance.HorizontalSpeed;
            MoveXZ(moveValue);
        }

        public void MoveBack()
        {
            Vector3 moveValue = Vector3.zero;
            moveValue = -Vector3.forward * Time.deltaTime * GameMain.Instance.HorizontalSpeed;
            MoveXZ(moveValue);
        }

        public void OnMoveDownStart()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }

        public void OnMoveDown()
        {
            float downSpeed = GameMain.Instance.DownSpeed;
            Vector3 vel = _pivotRigidbody.linearVelocity;
            vel.y = 0;
            _pivotRigidbody.linearVelocity = vel;
            _pivotRigidbody.AddForce(Vector3.up * -9.81f * downSpeed * _pivotRigidbody.mass, ForceMode.Force);
        }

        public bool IsArriveMovingUp()
        {
            return _startPivotPosition.y <= _pivotRigidbody.transform.position.y;
        }

        public void OnStartMoveUp()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationY;
        }

        public void OnStopMoveUp()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePosition;
        }

        public void OnMoveUp()
        {
            float upSpeed = GameMain.Instance.UpSpeed;
            Vector3 vel = _pivotRigidbody.linearVelocity;
            vel = Vector3.zero;
            _pivotRigidbody.linearVelocity = vel;
            _pivotRigidbody.AddForce(Vector3.up * 9.81f * upSpeed * _pivotRigidbody.mass, ForceMode.Force);
        }


        private void MoveXZ(Vector3 moveValue)
        {
            if (GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos))
            {
                // 1. 이동 후 목표 위치 먼저 계산
                Vector3 targetPos = _pivotRigidbody.position + moveValue * Time.fixedDeltaTime;

                // 2. 목표 위치를 bounds 안으로 클램프
                checkPos.ClampPositionToBoxCollider(ref targetPos);

                // 3. 클램프된 위치로 이동 (항상 이동, 경계에서도 벽에 붙어있음)
                _pivotRigidbody.MovePosition(targetPos);
            }
        }


        public void MoveXZStart()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePositionY;
        }

        public void MoveXZEnd()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePosition;
        }

        public void SetMoveDown()
        {
            _pivotRigidbody.isKinematic = false;
            _pivotRigidbody.constraints &= ~RigidbodyConstraints.FreezePositionY;
        }

        public void Grap()
        {
            foreach (var joint in _craneHingeJoints)
            {
                if (joint != null)
                {
                    var spring = joint.spring;
                    spring.spring = 10000f;   // 높을수록 빠르게 닫힘
                    spring.damper = 600f;    // spring 대비 10% 비율로 충격 흡수
                    spring.targetPosition = 0f;

                    joint.spring = spring;
                    joint.useSpring = true;
                }
            }
        }



        public void Release()
        {
            foreach (var joint in _craneHingeJoints)
            {
                if (joint != null)
                {
                    var spring = joint.spring;     // struct 복사
                    spring.spring = 500f;          // 스프링 강도 (값이 클수록 강하게 조여짐)
                    spring.damper = 10f;           // 감속 (출렁거림 방지)
                    spring.targetPosition = -60f;    // 닫힌 상태의 목표 각도 (joint local angle 기준)

                    joint.spring = spring;         // 다시 할당해야 적용됨
                    joint.useSpring = true;        // spring 사용 켜기
                }
            }
        }

        //public void MoveUpStart()
        //{
        //    _pivotRigidbody.constraints &= ~RigidbodyConstraints.FreezeAll;

        //}

        public void MoveUpEnd()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePosition;
        }

        public bool IsArrivedUpPoision()
        {
            return _startPivotPosition.y <= _pivotRigidbody.position.y;
        }
    }
}
