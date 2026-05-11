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
        }

        protected override void OnDestroy()
        {
            GameCamera.TryGetSetInstance(out GameCamera gameCamera);
        }
    }
}

