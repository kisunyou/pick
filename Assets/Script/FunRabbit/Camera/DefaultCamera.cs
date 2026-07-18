using UnityEngine;

namespace FunRabbit
{
    // 기본 인게임 카메라.
    public class DefaultCamera : GameCamera
    {
        public override CameraMode Mode => CameraMode.Default;

        public override void OnChangedGameStatus(GameStatus status)
        {
            switch (status)
            {
                case GameStatus.LOBBY:
                    SetCameraPosition(CameraStatus.LOBBY);
                    break;

                case GameStatus.INGAME:
                    SetCameraPosition(CameraStatus.INGAME);
                    break;
            }
        }

        // 지정된 카메라 상태(위치 인덱스)의 위치/회전으로 카메라를 이동
        private void SetCameraPosition(int cameraStatus)
        {
            if (!GameCheckPositions.TryGetSetInstance(out GameCheckPositions cameraPositions))
            {
                Debug.LogError("[DefaultCamera] 카메라 위치 초기화 실패 - GameCheckPositions 없음");
                return;
            }

            Transform camPlayTransform = cameraPositions.CameraPositions[cameraStatus];
            SetPosition(camPlayTransform.position);
            SetRotation(camPlayTransform.rotation);
        }
    }
}
