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

        public Vector3 PivotPosition => _pivotRigidbody.position;

        // 시작(중간) 위치 - READY 상태에서 이동 목표
        public Vector3 StartPivotPosition => _startPivotPosition;

        public CraneTransform(Rigidbody[] craneRigidbodys, Rigidbody pivotRigidbodys)
        {
            this._craneRigidbodys = craneRigidbodys;
            this._pivotRigidbody = pivotRigidbodys;

            _startPivotPosition = this._pivotRigidbody.position;

            // 빠른 하강/집게 회전 시 인형을 뚫고 지나가는(터널링) 현상을 막기 위해,
            // 인형과 실제로 충돌하는 크레인 바디(로프 끝 + 집게)를 가장 터널링에 강한
            // ContinuousSpeculative 모드로 설정한다. (인형 쪽은 Actor.cs에서 동일하게 설정)
            foreach (var body in craneRigidbodys)
            {
                if (body != null)
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            _craneHingeJoints = new HingeJoint[3];
            _craneHingeJoints[0] = craneRigidbodys[1].GetComponent<HingeJoint>();
            _craneHingeJoints[1] = craneRigidbodys[2].GetComponent<HingeJoint>();
            _craneHingeJoints[2] = craneRigidbodys[3].GetComponent<HingeJoint>();
        }

        public void MoveLeft(float multiplier = 1f)
        {
            Vector3 moveValue = -Vector3.right * Time.deltaTime * GameMain.Instance.HorizontalSpeed * multiplier;
            MoveXZ(moveValue);
        }

        public void MoveRight(float multiplier = 1f)
        {
            Vector3 moveValue = Vector3.right * Time.deltaTime * GameMain.Instance.HorizontalSpeed * multiplier;
            MoveXZ(moveValue);
        }

        public void MoveFront(float multiplier = 1f)
        {
            Vector3 moveValue = Vector3.forward * Time.deltaTime * GameMain.Instance.HorizontalSpeed * multiplier;
            MoveXZ(moveValue);
        }

        public void MoveBack(float multiplier = 1f)
        {
            Vector3 moveValue = -Vector3.forward * Time.deltaTime * GameMain.Instance.HorizontalSpeed * multiplier;
            MoveXZ(moveValue);
        }

        public void OnMoveDownStart()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }

        public void OnMoveDown()
        {
            // 최소 높이 제한: maxfloorPosition.y 아래로는 내려가지 않도록 한다.
            if (GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos)
                && checkPos.MaxFloorPosition != null)
            {
                float minY = checkPos.MaxFloorPosition.position.y;
                if (_pivotRigidbody.position.y <= minY)
                {
                    // 하한 도달 - 수직 속도를 0으로, 위치를 minY로 고정하고 아래 방향 힘은 가하지 않는다.
                    Vector3 clampedVel = _pivotRigidbody.linearVelocity;
                    clampedVel.y = 0f;
                    _pivotRigidbody.linearVelocity = clampedVel;

                    Vector3 clampedPos = _pivotRigidbody.position;
                    clampedPos.y = minY;
                    _pivotRigidbody.position = clampedPos;
                    return;
                }
            }

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


        public void MoveXZ(Vector3 moveValue)
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

        /// <summary>
        /// XZ 이동을 "목표 위치 텔레포트(MovePosition)"가 아니라 "등속 velocity"로 처리한다.
        /// MovePosition은 비kinematic + 조인트로 매달린 하중과 매 스텝 경합해 "드드득"
        /// 떨림을 만들지만, velocity는 등속이라 로프/집게가 자연스럽게 끌려와 부드럽다.
        /// dir : 월드 기준 입력 방향(크기 0~1, 대각선/DPad 세기 반영). 매 FixedUpdate 호출.
        /// </summary>
        public void MoveXZVelocity(Vector3 dir)
        {
            Vector3 vel = _pivotRigidbody.linearVelocity;

            Vector3 horizontal = new Vector3(dir.x, 0f, dir.z);
            if (horizontal.sqrMagnitude < 1e-6f)
            {
                // 입력 없음 → XZ 속도 정지 (Y는 제약/하강 로직이 관리하므로 건드리지 않음)
                vel.x = 0f;
                vel.z = 0f;
                _pivotRigidbody.linearVelocity = vel;
                return;
            }

            // 대각선이 더 빨라지지 않도록 크기를 1로 제한 (DPad의 부분 입력은 비례 유지)
            if (horizontal.sqrMagnitude > 1f)
                horizontal = horizontal.normalized;

            // 기존 튜닝(HorizontalSpeed) 체감을 보존: 기존 변위가 speed*dt*fixedDt 였으므로
            // 등가 속도(units/sec)는 speed*fixedDt 이다. (HorizontalSpeed 값 그대로 사용 가능)
            float speed = GameMain.Instance.HorizontalSpeed * Time.fixedDeltaTime;
            Vector3 desiredVel = horizontal * speed;

            // 경계 밖으로 나가려는 축은 속도를 0으로 (벽에 붙되 떨림 없이)
            if (GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos))
            {
                Vector3 predicted = _pivotRigidbody.position + desiredVel * Time.fixedDeltaTime;
                Vector3 clamped = predicted;
                checkPos.ClampPositionToBoxCollider(ref clamped);
                if (!Mathf.Approximately(clamped.x, predicted.x)) desiredVel.x = 0f;
                if (!Mathf.Approximately(clamped.z, predicted.z)) desiredVel.z = 0f;
            }

            vel.x = desiredVel.x;
            vel.z = desiredVel.z;
            _pivotRigidbody.linearVelocity = vel;
        }


        public void MoveXZStart()
        {
            _pivotRigidbody.constraints = RigidbodyConstraints.FreezePositionY;
        }

        public bool IsMoveXZStarted()
        {
            return (_pivotRigidbody.constraints & RigidbodyConstraints.FreezePositionY) == 0;
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
                    spring.spring = 8500f;   // 바닥에서 인형을 처음 집을 때의 그립력 (현재 값이 적당)
                    spring.damper = 2500f;   // damper도 함께 높여, 강한 스프링이어도 천천히 닫혀 그랩 시 튕김 방지
                    spring.targetPosition = 0f;

                    joint.spring = spring;
                    joint.useSpring = true;
                }
            }
        }

        // 들어올릴 때만 집게를 더 꽉 쥐게 한다 (MOVING_UP 시작 시 호출).
        // MOVING_UP ~ MOVING_RETURN 동안 유지되다가 DROP의 Release()에서 풀린다.
        public void GrapTight()
        {
            foreach (var joint in _craneHingeJoints)
            {
                if (joint != null)
                {
                    var spring = joint.spring;
                    spring.spring = 17000f;  // 들어올리며 이동할 때 인형을 놓치지 않도록 그립력 강화 (집을 때의 2배)
                    spring.damper = 4000f;   // damper도 비례해 올려 강한 스프링이어도 튕김 방지
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
