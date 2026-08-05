using UnityEngine;

namespace FunRabbit
{
    // Actor 상태 머신의 상태 기반 클래스. BattleActor/CollectionActor가 공용으로 사용한다.
    // dev 작업 폴더(TeenyWorld)의 TeenyState/TeenyActorState 구조를 pick 규모(상태 몇 개, 소수 인스턴스)에
    // 맞게 단순화해 가져왔다 (key-index 배열/예약 상태/파라미터 슬롯 등은 pick에 불필요해 생략).
    public abstract class ActorState
    {
        // 목표 지점 도착 판정 거리. Move 상태 도착 판정 + BattleActor의 공격범위 허용치 등 공용으로 쓰인다.
        public const float ARRIVE_THRESHOLD = 0.05f;

        public int Key { get; }
        public bool IsPlayingState { get; private set; }

        protected readonly Actor _actor;

        private bool _hasRotateTarget;
        private Vector3 _rotateTargetPosition;

        protected ActorState(int key, Actor actor)
        {
            Key = key;
            _actor = actor;
        }

        public virtual void EnterState() { IsPlayingState = true; }

        // SetRotate로 설정된 회전 대상이 있으면 매 프레임 자동으로 회전을 진행한다.
        // 서브클래스가 UpdateState를 오버라이드해도 base.UpdateState(deltaTime)를 호출해야 회전이 이어진다.
        public virtual void UpdateState(float deltaTime)
        {
            if (_hasRotateTarget)
                _actor.FaceTarget(_rotateTargetPosition);
        }

        public virtual void LeaveState()
        {
            IsPlayingState = false;
            _hasRotateTarget = false;
        }

        // 회전 대상 위치를 설정(갱신)한다. 실제 회전 진행(Slerp)은 base.UpdateState에서 매 프레임 이뤄진다.
        // 모든 상태가 공용으로 사용할 수 있는 베이스 기능 - Attack 등 이동이 없는 상태에서도 회전이 가능하다.
        protected void SetRotate(Vector3 targetPosition)
        {
            _hasRotateTarget = true;
            _rotateTargetPosition = targetPosition;
        }

        // y(높이)를 제외한 수평(XZ) 거리. BattleActor는 bottomSocket 접지 보정(OnMoveUpdate)이 y를 계속
        // 건드리므로, 도착/사거리 판정에 3D 거리를 쓰면 y 오차만큼 영영 도착 판정이 나지 않는다.
        protected static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
