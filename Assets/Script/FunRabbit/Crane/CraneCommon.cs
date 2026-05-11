using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class CraneBodyType
    {
        /// <summary>
        /// 크레인 바디
        /// </summary>
        public static int CENTER_BODY = 0;
        /// <summary>
        /// 집게 0
        /// </summary>
        public static int FINGER_0 = 1;
        /// <summary>
        /// 집게 1
        /// </summary>
        public static int FINGER_1 = 2;
        /// <summary>
        /// 집게 2
        /// </summary>
        public static int FINGER_2 = 3;
    }

    public class CraneStatus
    {
        public const int READY = 0;
        public const int CONTROL_MOVING = 1;
        public const int MOVING_DOWN = 2;
        public const int GRAP = 3;
        public const int MOVING_UP = 4;
        public const int MOVING_RETURN = 5;
        public const int DROP = 6;
    }
}