using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI.Extensions;

namespace FunRabbit
{
    public class GameCheckPositions : InstanceSetter<GameCheckPositions>
    {
        [SerializeField] Transform[] cameraPositions;
        [SerializeField] Transform returnPosition;
        [SerializeField] BoxCollider craneLimitBoxCollider;
        [SerializeField] Transform[] dollCreatePositions;
        [SerializeField] Transform maxfloorPosition;
        [SerializeField] Transform pickMachine;
        [SerializeField] Transform collectionArea;

        public Transform[] CameraPositions
        {
            get { return cameraPositions; }
        }

        // 생성된 인형을 붙일 부모 Transform (인형뽑기 기계)
        public Transform PickMachine
        {
            get { return pickMachine; }
        }

        // 컬렉션(도감) 인형이 배회하는 영역 루트 (하위 Plane들이 이동 가능 바닥)
        public Transform CollectionArea
        {
            get { return collectionArea; }
        }

        public Transform[] DollCreatePositions
        {
            get { return dollCreatePositions; }
        }

        public Transform ReturnPosition
        {
            get { return returnPosition; }
        }

        public Transform MaxFloorPosition
        {
            get { return maxfloorPosition; }
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

