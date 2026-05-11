using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameCameraManager : Singleton<GameCameraManager>
    {
        [SerializeField] Camera _mainCamera;
        [SerializeField] Transform[] _statusTransforms;


        private void Awake()
        {
            
        }

        private void Start()
        {
            
        }

        public void SetStatus(int status)
        {
            if (status == CameraStatus.PLAY)
            {
                
            }
        }
    }
}