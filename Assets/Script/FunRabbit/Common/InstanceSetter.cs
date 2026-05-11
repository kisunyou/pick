using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    /// <summary>
    /// 싱글톤 패턴이 아니라, MonoBehaviour에서 생성될 때만 _instance가 설정되는 클래스
    /// 모노에서 오브젝트 생성 시 마다 새롭게 갱신 됨.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class InstanceSetter<T> : MonoBehaviour where T : MonoBehaviour
    {
        protected static T _instance = null;

        public static bool TryGetSetInstance(out T instance)
        {
            if (_instance == null)
            {
                instance = null;
                return false;
            }

            instance = _instance;
            return true;
        }

        protected virtual void Awake()
        {
            _instance = this as T;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
