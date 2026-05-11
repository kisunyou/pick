using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI.Extensions;

namespace FunRabbit
{
    public class GameCheckPositions : InstanceSetter<GameCheckPositions>
    {
        [SerializeField] Transform[] cameraPositions;
        [SerializeField] BoxCollider craneLimitBoxCollider;
        [SerializeField] Transform[] dollCreatePositions;

        public Transform[] CameraPositions
        {
            get { return cameraPositions; }
        }

        public Transform[] DollCreatePositions
        {
            get { return dollCreatePositions; }
        }

        /// <summary>
        /// 크래인의 위치를 BoxCollider 위치로 제한.
        /// </summary>
        /// <param name="cranePos"></param>
        public bool ClampPositionToBoxCollider(ref Vector3 cranePos)
        {
            Bounds bounds = craneLimitBoxCollider.bounds;

            // ClosestPoint 대신 직접 각 축을 클램프
            cranePos.x = Mathf.Clamp(cranePos.x, bounds.min.x, bounds.max.x);
            cranePos.y = Mathf.Clamp(cranePos.y, bounds.min.y, bounds.max.y);
            cranePos.z = Mathf.Clamp(cranePos.z, bounds.min.z, bounds.max.z);

            return bounds.Contains(cranePos);
        }
    }
}

