using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameStarter : MonoBehaviour
    {
        void Start()
        {
            GameMain.MakeInstance();            
        }
    }
}
