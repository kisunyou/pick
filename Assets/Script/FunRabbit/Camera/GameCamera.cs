using UnityEngine;

namespace FunRabbit
{
    // 모드별 카메라의 베이스 클래스. 직접 배치하지 않고 모드별로 상속해서 사용한다
    // (예: DefaultCamera, CollectionCamera). GameCameraManager.ChangeCameraMode(mode)
    // 호출 시 Mode가 일치하는 파생 카메라 하나만 활성화된다.
    public abstract class GameCamera : GameTransform<GameCamera>
    {
        // 이 카메라가 사용하는 실제 Camera. 상속받는 모든 모드별 카메라가 기본으로 갖는다.
        [SerializeField] private Camera _mainCamera;

        // 매니저의 화면 좌표 변환 등 외부에서 실제 Camera가 필요할 때 참조한다.
        public Camera MainCamera => _mainCamera;

        // 이 카메라가 속한 모드. 파생 클래스가 override로 지정한다.
        public abstract CameraMode Mode { get; }

        protected override void Awake()
        {
            base.Awake();
            SetTargetTransform(_mainCamera.transform);
            GameCameraManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameCameraManager.IsCheckInstance())
                GameCameraManager.Instance.Unregister(this);

            base.OnDestroy();
        }

        // 이 카메라가 활성 모드가 될 때 매니저가 호출한다. 기본 동작: 실제 카메라를 켠다.
        public virtual void OnActivate()
        {
            if (_mainCamera != null)
                _mainCamera.gameObject.SetActive(true);
        }

        // 다른 모드로 전환되어 비활성화될 때 매니저가 호출한다. 기본 동작: 실제 카메라를 끈다.
        public virtual void OnDeactivate()
        {
            if (_mainCamera != null)
                _mainCamera.gameObject.SetActive(false);
        }

        // 이 카메라가 활성 상태인 동안 게임 상태(GameStatus) 변경을 받는다.
        // (매니저가 활성 카메라에만 호출 - 비활성 카메라는 갱신하지 않음)
        public virtual void OnChangedGameStatus(GameStatus status) { }
    }
}
