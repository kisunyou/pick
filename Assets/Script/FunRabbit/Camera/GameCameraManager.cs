using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 전체 GameCamera(모드별 카메라)를 컨트롤하는 매니저.
    // - 등록된 카메라 중 현재 모드(CameraMode)에 해당하는 카메라 하나만 활성 상태로 유지한다.
    // - ChangeCameraMode로 모드를 전환하면 이전 활성 카메라는 비활성화, 새 카메라가 활성화된다.
    // - 게임 상태(GameStatus) 변경 구독은 매니저가 전담해 현재 활성 카메라에만 전달한다.
    public class GameCameraManager : Singleton<GameCameraManager>
    {
        private readonly Dictionary<CameraMode, GameCamera> _cameras = new Dictionary<CameraMode, GameCamera>();

        private GameCamera _activeCamera;
        private CameraMode _currentMode = CameraMode.Default;

        // 현재 활성 모드 카메라의 실제 Camera (터치 레이캐스트 등 외부 사용)
        public Camera ActiveCamera
        {
            get { return _activeCamera != null ? _activeCamera.MainCamera : null; }
        }

        // 현재 활성 모드의 GameCamera (모드별 카메라 기능 접근용 - 예: CollectionCamera.FocusOn)
        public GameCamera ActiveGameCamera
        {
            get { return _activeCamera; }
        }

        private void Start()
        {
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        // 게임 상태 변경 시: 상태에 맞는 카메라 모드로 전환하고(LOBBY/INGAME → Default,
        // COLLECTION → Collection), 현재 활성 카메라에 상태를 전달한다.
        // (비활성 카메라는 갱신하지 않음)
        private void OnChangedGameStatus(GameStatus status)
        {
            switch (status)
            {
                case GameStatus.LOBBY:
                case GameStatus.INGAME:
                    ChangeCameraMode(CameraMode.Default);
                    break;

                case GameStatus.COLLECTION:
                    ChangeCameraMode(CameraMode.Collection);
                    break;
            }

            _activeCamera?.OnChangedGameStatus(status);
        }

        // 카메라 등록 (각 GameCamera.Awake에서 호출). 현재 모드와 일치하면 즉시 활성화,
        // 아니면 비활성화 상태로 대기시킨다.
        public void Register(GameCamera camera)
        {
            if (camera == null)
                return;

            _cameras[camera.Mode] = camera;

            if (camera.Mode == _currentMode)
                ActivateCamera(camera);
            else
                camera.OnDeactivate();
        }

        public void Unregister(GameCamera camera)
        {
            if (camera == null)
                return;

            if (_cameras.TryGetValue(camera.Mode, out GameCamera registered) && registered == camera)
                _cameras.Remove(camera.Mode);

            if (_activeCamera == camera)
                _activeCamera = null;
        }

        // 카메라 모드를 전환한다. 해당 모드의 카메라가 아직 등록되지 않았으면,
        // 나중에 등록되는 시점(Register)에 자동으로 활성화된다.
        public void ChangeCameraMode(CameraMode mode)
        {
            if (_currentMode == mode && _activeCamera != null)
                return;

            _currentMode = mode;

            if (_cameras.TryGetValue(mode, out GameCamera camera))
                ActivateCamera(camera);
            else
                Debug.LogWarning($"[GameCameraManager] {mode} 모드에 등록된 카메라가 없습니다. 등록되면 자동으로 활성화됩니다.");
        }

        private void ActivateCamera(GameCamera camera)
        {
            if (_activeCamera == camera)
                return;

            _activeCamera?.OnDeactivate();
            _activeCamera = camera;
            camera.OnActivate();

            // 새로 활성화된 카메라에 현재 게임 상태를 즉시 반영
            if (GameMain.IsCheckInstance() && GameMain.Instance.HasStatus)
                camera.OnChangedGameStatus(GameMain.Instance.CurrentStatus);
        }

        // 3D 월드 좌표를 화면(2D) 좌표로 변환한다. (z = 0)
        // UIHud 캔버스가 Screen Space - Overlay라 반환값의 x,y가 곧 UI 좌표로 쓰인다.
        // 활성 카메라의 MainCamera를 사용하며, 없으면 원본 좌표를 그대로 반환한다.
        public Vector3 Convert3dTo2dCoord(Vector3 worldPos)
        {
            Camera cam = _activeCamera != null ? _activeCamera.MainCamera : null;
            if (cam == null)
                return worldPos;

            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            screenPos.z = 0f;
            return screenPos;
        }
    }
}
