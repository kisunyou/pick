using UnityEngine;

namespace FunRabbit
{
    // 목표 지점(actor.MoveTargetPosition)까지 이동하며 그 방향을 바라보다가, 도착하면 actor.OnMoveArrived()를 호출한다.
    // 목표 지점 계산(PrepareMoveTarget)과 도착 후 처리(OnMoveArrived)는 각 Actor 서브클래스가 결정한다.
    // (BattleActor: 보스에게 접근 후 Attack 전환 / CollectionActor: 배회 목적지로 이동 후 Idle 전환)
    public class ActorMoveState : ActorState
    {
        public ActorMoveState(int key, Actor actor) : base(key, actor) { }

        public override void EnterState()
        {
            base.EnterState();
            _actor.Animation.PlayMoveAnimation();
            _actor.PrepareMoveTarget();
        }

        public override void UpdateState(float deltaTime)
        {
            SetRotate(_actor.MoveTargetPosition);
            base.UpdateState(deltaTime);

            Vector3 currentPosition = _actor.transform.position;
            Vector3 targetPosition = _actor.MoveTargetPosition;
            float horizontalDistance = HorizontalDistance(currentPosition, targetPosition);

            if (horizontalDistance <= ARRIVE_THRESHOLD)
            {
                _actor.OnMoveArrived();
                return;
            }

            // y는 건드리지 않는다 - BattleActor는 OnMoveUpdate(bottomSocket 접지 보정)가 y를 전담한다.
            Vector3 direction = new Vector3(targetPosition.x - currentPosition.x, 0f, targetPosition.z - currentPosition.z).normalized;
            float step = Mathf.Min(_actor.MoveSpeed * deltaTime, horizontalDistance);
            _actor.transform.position = currentPosition + direction * step;
            _actor.OnMoveUpdate();
        }
    }
}
