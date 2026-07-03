using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameCameraManager : Singleton<GameCameraManager>
    {
        [SerializeField] Camera _mainCamera;
        [SerializeField] Transform[] _statusTransforms;

        // 3D 월드 좌표를 화면(2D) 좌표로 변환한다. (z = 0)
        // UIHud 캔버스가 Screen Space - Overlay라 반환값의 x,y가 곧 UI 좌표로 쓰인다.
        // 카메라가 없으면 원본 좌표를 그대로 반환한다.
        public Vector3 Convert3dTo2dCoord(Vector3 worldPos)
        {
            if (_mainCamera == null)
                return worldPos;

            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);
            screenPos.z = 0f;
            return screenPos;
        }


        private void Awake()
        {
            
        }

        private void Start()
        {
            
        }

        public void SetStatus(int status)
        {
            if (status == CameraStatus.INGAME)
            {
                
            }
        }
    }
}