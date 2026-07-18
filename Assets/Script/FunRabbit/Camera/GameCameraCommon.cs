using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class CameraStatus
    {
        public static int LOBBY = 0;
        public static int INGAME = 1;
    }

    // GameCameraManager.ChangeCameraMode의 대상 키. 모드별 GameCamera 파생 클래스가
    // Mode 프로퍼티로 자신이 어느 모드에 속하는지 선언한다.
    public enum CameraMode
    {
        Default = 0,
        Collection = 1,
    }
}

