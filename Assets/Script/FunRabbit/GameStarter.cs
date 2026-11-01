using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameStarter : MonoBehaviour
    {
        void Start()
        {
            // 게임 중에는 절전(화면 어두워짐·꺼짐)으로 넘어가지 않게 한다.
            // 앱이 백그라운드로 가면 OS 가 기기 기본 설정으로 되돌리므로 앱 밖에는 영향이 없다.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            GameMain.MakeInstance();
        }
    }
}
