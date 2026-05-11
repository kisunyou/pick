using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameMain : Singleton<GameMain>
    {
        [SerializeField] public float HorizontalSpeed = 500.0f;
        [SerializeField] public float DownSpeed = 15.0f;
        [SerializeField] public float UpSpeed = 30.0f;

        private void Start()
        {
            UILoading.CreateOrGet();
            SceneLoader.Instance.LoadAsync("Stage0", this.OnLoadedStage);
        }

        private void OnLoadedStage()
        {
            // 카메라 위치 초기화
            if (GameCamera.TryGetSetInstance(out var gameCamera) &&
                GameCheckPositions.TryGetSetInstance(out GameCheckPositions cameraPositions))
            {
                Transform camPlayTransform = cameraPositions.CameraPositions[CameraStatus.PLAY];
                gameCamera.SetPosition(camPlayTransform.position);
                gameCamera.SetRotation(camPlayTransform.rotation);
            }
            else
            {
                Debug.LogError("카메라 초기화 실패");
                return;
            }

            GameDollCreator.Instance.CreateDolls();
        }
    }
}