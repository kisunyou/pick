using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance_;
        private static object _lock = new object();

        public static void ClearVariablesSingleton()
        {
            instance_ = null;
            applicationIsQuitting = false;
        }

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    Debug.LogWarning("[Singleton] Instance '" + typeof(T) +
                        "' already destroyed on application quit." +
                        " Won't create again - returning null.");
                    return null;
                }
                return MakeInstance();
            }
        }

        public static T MakeInstance()
        {
            lock (_lock)
            {
                if (instance_ == null)
                {
                    instance_ = (T)FindObjectOfType(typeof(T));

                    if (FindObjectsOfType(typeof(T)).Length > 1)
                    {
                        Debug.LogError("[Singleton] Something went really wrong " +
                            " - there should never be more than 1 singleton!" +
                            " Reopening the scene might fix it.");
                        return instance_;
                    }

                    if (instance_ == null)
                    {
                        GameObject singleton = new GameObject();
                        singleton.transform.parent = null;
                        singleton.AddComponent<T>();
                        instance_ = singleton.GetComponent<T>();
                        singleton.name = "(singleton) " + typeof(T).ToString();
                        DontDestroyOnLoad(singleton);
                        Debug.Log($"MakeInstance_{singleton.name}_{singleton.transform.parent}");
                        Debug.Log("[Singleton] An instance of " + typeof(T) +
                            " is needed in the scene, so '" + singleton +
                            "' was created with DontDestroyOnLoad.");
                    }
                    else
                    {
                        Debug.Log("[Singleton] Using instance already created: " +
                            instance_.gameObject.name);
                    }
                }
                return instance_;
            }
        }

        private static bool applicationIsQuitting = false;

        public static bool IsCheckInstance()
        {
            if (true == applicationIsQuitting)
                return false;
            return instance_ != null ? true : false;
        }

        public static bool IsQuitting
        {
            get { return applicationIsQuitting; }
        }

        // virtual Ãß°¡
        protected virtual void Awake()
        {
            if (instance_ == null)
            {
                instance_ = this as T;
            }
        }

        void OnDestroy()
        {
            applicationIsQuitting = true;
        }
    }
}