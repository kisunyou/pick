using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameCamera : GameTransform<GameCamera>
    {
        [SerializeField] private Camera _mainCamera;

        protected override void Awake()
        {
            base.Awake();
            base.SetTargetTransform(_mainCamera.transform);
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        protected override void OnDestroy()
        {
            GameCamera.TryGetSetInstance(out GameCamera gameCamera);
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
        }

        // 게임 상태 변경 시 카메라 위치/회전 갱신
        private void OnChangedGameStatus(GameStatus status)
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
                Debug.LogError("카메라 초기화 실패");
                return;
            }

            Transform camPlayTransform = cameraPositions.CameraPositions[cameraStatus];
            SetPosition(camPlayTransform.position);
            SetRotation(camPlayTransform.rotation);
        }
    }
}

