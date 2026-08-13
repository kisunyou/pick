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

        // ── Grap 스프링 램프 ──────────────────────────────────────
        // 스프링을 한 스텝에 200→8500으로 올리면 과도한 토크가 접촉 솔버를
        // 이겨 집게가 인형을 뚫는다. 목표각(0°)은 즉시 주되, 스프링/댐퍼는
        // GRAP_RAMP_DURATION에 걸쳐 서서히 올린다. (최종 그립력은 기존과 동일)
        const float GRAP_RAMP_DURATION = 0.4f;   // 목표 스프링까지 올리는 시간(초)
        const float GRAP_START_SPRING = 200f;    // 씬 대기 상태와 같은 시작값
        const float GRAP_TARGET_SPRING = 8500f;  // 바닥에서 집을 때의 그립력 (기존 Grap 값)
        const float GRAP_START_DAMPER = 20f;
        const float GRAP_TARGET_DAMPER = 2500f;

        private bool _isGrapRamping = false;
        private float _grapRampElapsed = 0f;

        // ── 하강 얹힘(soft) 모드 ──────────────────────────────────
        // 기존 하강은 매 스텝 vy를 0으로 리셋하고 힘을 재주입하는 "속도원" 방식이라,
        // 인형이 밀어내도 다음 스텝에 다시 2.94 m/s로 내려꽂혀 더미를 짓누른다.
        // 집게 중심 반경 내에 인형이 감지되면 속도 재주입을 멈추고 약한 힘만 가해,
        // 접촉 솔버가 크레인을 실제로 밀어낼 수 있게 한다 → 집게가 더미 위에 얹힌다.
        const float DOWN_SOFT_CHECK_RADIUS = 1.5f; // 인형 감지 반경 (잡힘 판정 holdCheckRadius와 동일 값)
        const float DOWN_SOFT_FORCE_RATIO = 0.2f;  // 감지 시 하강력·가라앉기 속도 상한 배율 (기존 등속 대비)

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
            // maxDepenetrationVelocity도 인형(Actor.cs)과 동일하게 상향 - 하강/그립으로
            // 생긴 겹침이 하강 속도(≈2.94 m/s)보다 빠르게 풀리도록 한다.
            foreach (var body in craneRigidbodys)
            {
                if (body != null)
                {
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.maxDepenetrationVelocity = 4f;
                }
            }

            _craneHingeJoints = new HingeJoint[3];
            _craneHingeJoints[0] = craneRigidbodys[1].GetComponent<HingeJoint>();
            _craneHingeJoints[1] = craneRigidbodys[2].GetComponent<HingeJoint>();
            _craneHingeJoints[2] = craneRigidbodys[3].GetComponent<HingeJoint>();

            // 놓기(Release) 시 인형 통과 처리용 - 크레인 바디 전체의 콜라이더를 캐시
            var craneColliderSet = new HashSet<Collider>();
            foreach (var body in craneRigidbodys)
            {
                if (body == null)
                    continue;
                foreach (var col in body.GetComponentsInChildren<Collider>(true))
                    craneColliderSet.Add(col);
            }
            _craneColliders = new Collider[craneColliderSet.Count];
            craneColliderSet.CopyTo(_craneColliders);
        }

        // 들어올린 뒤 집게가 실제로 인형을 잡고 있는지 판별한다.
        // 최고점에서는 잡힌 인형만 집게 근처에 있고, 못 잡았으면 인형들은 모두 아래 더미에 남아 있으므로
        // 집게 중심부(CENTER_BODY) 반경 안에 인형(Actor)이 있는지로 판단한다.
        public bool IsHoldingDoll(float radius)
        {
            Rigidbody center = _craneRigidbodys[CraneBodyType.CENTER_BODY];
            if (center == null)
                return false;

            return StageManager.IsAnyActorNear(center.position, radius);
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

            // 집게 중심 반경 내 인형 감지 → 얹힘(soft) 모드
            Rigidbody center = _craneRigidbodys[CraneBodyType.CENTER_BODY];
            bool nearDoll = center != null
                && StageManager.IsAnyActorNear(center.position, DOWN_SOFT_CHECK_RADIUS);

            if (nearDoll)
            {
                // 속도 재주입을 멈추고 (vy 리셋 없음 - 솔버의 밀어내기 허용),
                // 매달린 하중·중력으로 인한 과속만 상한으로 막는다.
                float softMaxDown = 9.81f * downSpeed * Time.fixedDeltaTime * DOWN_SOFT_FORCE_RATIO; // ≈0.59 m/s
                Vector3 softVel = _pivotRigidbody.linearVelocity;
                if (softVel.y < -softMaxDown)
                {
                    softVel.y = -softMaxDown;
                    _pivotRigidbody.linearVelocity = softVel;
                }

                // 기존의 20% 힘만 가함 - 인형 더미가 버티면 그 위에 얹힌 채 멈춘다
                _pivotRigidbody.AddForce(
                    Vector3.up * -9.81f * downSpeed * DOWN_SOFT_FORCE_RATIO * _pivotRigidbody.mass,
                    ForceMode.Force);
                return;
            }

            // 자유 하강 (기존 방식): 매 스텝 vy 리셋 + 힘 재주입 = 등속 ≈2.94 m/s
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


        /// <summary>
        /// 목표 XZ 위치로 등속 velocity 이동. 도착(threshold 이내) 시 XZ 속도를 0으로 만들고 true.
        /// MovePosition 방식은 조인트로 매달린 하중과 매 스텝 경합해 저프레임(모바일)에서
        /// 덜컹거림을 만들고, 렌더 프레임당 이동량이 커져 목표 주변을 오버슛하며 진동
        /// (도착 판정 실패 → 집게가 안 열리는 멈춤)까지 유발하므로 velocity로 이동한다.
        /// speedRatio : 기본 XZ 속도(HorizontalSpeed × fixedDeltaTime) 대비 배율.
        /// </summary>
        public bool MoveTowardXZVelocity(Vector3 targetXZ, float speedRatio, float threshold)
        {
            Vector3 pos = _pivotRigidbody.position;
            Vector3 delta = new Vector3(targetXZ.x - pos.x, 0f, targetXZ.z - pos.z);
            float distance = delta.magnitude;

            Vector3 vel = _pivotRigidbody.linearVelocity;
            if (distance <= threshold)
            {
                vel.x = 0f;
                vel.z = 0f;
                _pivotRigidbody.linearVelocity = vel;
                return true;
            }

            // 기존 튜닝 체감 보존: 등가 속도(units/sec) = HorizontalSpeed × fixedDeltaTime (MoveXZVelocity와 동일 공식)
            float baseSpeed = GameMain.Instance.HorizontalSpeed * Time.fixedDeltaTime * speedRatio;
            // 한 물리 스텝에 남은 거리보다 멀리 가지 못하게 상한 → 목표 주변 오버슛 진동 원천 차단
            float speed = Mathf.Min(baseSpeed, distance / Time.fixedDeltaTime);

            Vector3 dir = delta / distance;
            vel.x = dir.x * speed;
            vel.z = dir.z * speed;
            _pivotRigidbody.linearVelocity = vel;
            return false;
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
            // 즉시 8500을 주지 않고 램프를 시작한다. 실제 스프링 상승은
            // ManualFixedUpdate()가 매 물리 스텝 처리한다.
            _isGrapRamping = true;
            _grapRampElapsed = 0f;
            ApplyGrapSpring(GRAP_START_SPRING, GRAP_START_DAMPER);
        }

        // Crane.FixedUpdate → CraneMovingControl.ManualFixedUpdate 에서 매 물리 스텝 호출.
        public void ManualFixedUpdate()
        {
            UpdateGrapRamp();
            UpdatePassThrough();
        }

        private void UpdateGrapRamp()
        {
            if (!_isGrapRamping)
                return;

            _grapRampElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(_grapRampElapsed / GRAP_RAMP_DURATION);

            ApplyGrapSpring(
                Mathf.Lerp(GRAP_START_SPRING, GRAP_TARGET_SPRING, t),
                Mathf.Lerp(GRAP_START_DAMPER, GRAP_TARGET_DAMPER, t));

            if (t >= 1f)
                _isGrapRamping = false;
        }

        private void ApplyGrapSpring(float springValue, float damperValue)
        {
            foreach (var joint in _craneHingeJoints)
            {
                if (joint != null)
                {
                    var spring = joint.spring;
                    spring.spring = springValue;
                    spring.damper = damperValue;   // damper도 함께 올려, 강한 스프링이어도 천천히 닫혀 그랩 시 튕김 방지
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



        // ── 놓기(Release) 시 인형 털어내기 ──────────────────────────
        // 드물게 손끝이 인형에 박힌 채(뚫림) 집게를 열어도 인형이 매달려 있는 경우가 있어,
        // 놓는 순간 집게 근처(=잡힌) 인형에 하향 초기 속도를 주고, 관통된 채 훅에 걸린
        // 인형도 그대로 통과해 떨어지도록 클로와의 충돌을 잠시 무시시킨다.
        const float RELEASE_SHAKEOFF_RADIUS = 1.5f;      // 잡힘 판정(holdCheckRadius)과 동일 값
        const float RELEASE_SHAKEOFF_SPEED = 3f;         // 하향 초기 속도(m/s)
        const float RELEASE_PASS_THROUGH_DURATION = 1f;  // 클로↔인형 충돌 무시 시간(초) - 다음 집기 전에 충분히 복구됨

        private static readonly List<Actor> _shakeOffBuffer = new List<Actor>();

        private Collider[] _craneColliders;

        // 충돌 무시를 되돌리기 위한 대기 항목 (놓은 인형별 1개)
        private class PassThroughEntry
        {
            public Collider[] dollColliders;
            public float remaining;
        }
        private readonly List<PassThroughEntry> _passThroughList = new List<PassThroughEntry>();

        private void ShakeOffHeldDolls()
        {
            Rigidbody center = _craneRigidbodys[CraneBodyType.CENTER_BODY];
            if (center == null)
                return;

            _shakeOffBuffer.Clear();
            StageManager.GetActorsNear(center.position, RELEASE_SHAKEOFF_RADIUS, _shakeOffBuffer);
            foreach (var actor in _shakeOffBuffer)
            {
                if (actor == null)
                    continue;

                // 하향 킥만으로는 관통(꼬치 상태)된 인형이 손가락 훅에 걸려 못 떨어지므로,
                // 클로와의 충돌을 잠시 꺼 손가락을 그대로 통과해 낙하하게 한다.
                BeginPassThrough(actor);

                if (actor.TryGetComponent(out Rigidbody body))
                    body.AddForce(Vector3.down * RELEASE_SHAKEOFF_SPEED, ForceMode.VelocityChange);
            }
        }

        // 인형의 모든 콜라이더 x 클로의 모든 콜라이더 충돌을 끄고, 복구 목록에 등록한다.
        private void BeginPassThrough(Actor actor)
        {
            Collider[] dollColliders = actor.GetComponentsInChildren<Collider>(true);
            SetIgnoreCraneCollision(dollColliders, true);
            _passThroughList.Add(new PassThroughEntry
            {
                dollColliders = dollColliders,
                remaining = RELEASE_PASS_THROUGH_DURATION,
            });
        }

        private void SetIgnoreCraneCollision(Collider[] dollColliders, bool ignore)
        {
            foreach (var craneCollider in _craneColliders)
            {
                if (craneCollider == null)
                    continue;

                foreach (var dollCollider in dollColliders)
                {
                    // 인형이 먼저 파괴된 경우(바스켓 획득 등) 파괴된 콜라이더는 건너뛴다
                    if (dollCollider == null)
                        continue;

                    Physics.IgnoreCollision(dollCollider, craneCollider, ignore);
                }
            }
        }

        // 통과 시간이 끝난 인형의 클로 충돌을 복구한다. (ManualFixedUpdate에서 매 물리 스텝 호출)
        private void UpdatePassThrough()
        {
            for (int i = _passThroughList.Count - 1; i >= 0; i--)
            {
                var entry = _passThroughList[i];
                entry.remaining -= Time.fixedDeltaTime;
                if (entry.remaining > 0f)
                    continue;

                SetIgnoreCraneCollision(entry.dollColliders, false);
                _passThroughList.RemoveAt(i);
            }
        }

        public void Release()
        {
            // 진행 중인 그립 램프가 있으면 중단한다 (램프가 놓기 스프링을 덮어쓰지 않도록)
            _isGrapRamping = false;

            // 집게를 열기 전에, 잡혀 있는 인형을 아래로 털어낸다 (뚫림으로 인한 매달림 방지)
            ShakeOffHeldDolls();

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
