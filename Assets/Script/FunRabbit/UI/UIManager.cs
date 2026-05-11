using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    public enum UILayer
    {
        None,
        Ingame,
        Hud,
        Popup,
        Message,
        Directing,
        Webview,
        System
    }

    public enum UIOpenMode
    {
        Single,
        Multiple
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UIOptionAttribute : Attribute
    {
        public string Path { get; set; }
        public UILayer Layer { get; set; }
        public UIOpenMode OpenMode { get; set; }
        public bool isPool { get; set; }
    }

    public abstract class BaseUIView<T> : MonoBehaviour where T : BaseUIView<T>
    {
        public static T Instance { get; private set; }

        protected virtual void Awake()
        {
            Instance = this as T;
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public virtual void OnOpen() { }
        public virtual void OnClose() { }

        /// <summary>
        /// 열려있으면 기존 반환, 없으면 새로 생성
        /// </summary>
        public static T CreateOrGet()
        {
            return UIManager.Instance.Open<T>();
        }

        /// <summary>
        /// 현재 열려있는 인스턴스 반환 (없으면 null)
        /// </summary>
        public static T Get()
        {
            return UIManager.Instance.Get<T>();
        }

        /// <summary>
        /// 닫기
        /// </summary>
        public void Close()
        {
            UIManager.Instance.Close(this);
        }
    }

    public class UIManager : Singleton<UIManager>
    {
        private static readonly Dictionary<UILayer, int> LayerSortOrders
            = new Dictionary<UILayer, int>
        {
            { UILayer.None,      0   },
            { UILayer.Ingame,    10  },
            { UILayer.Hud,       100 },
            { UILayer.Popup,     200 },
            { UILayer.Message,   300 },
            { UILayer.Directing, 400 },
            { UILayer.Webview,   500 },
            { UILayer.System,    600 },
        };

        private readonly Dictionary<UILayer, Transform> _layerRoots
            = new Dictionary<UILayer, Transform>();

        private readonly Dictionary<Type, MonoBehaviour> _openedViews
            = new Dictionary<Type, MonoBehaviour>();

        protected override void Awake()
        {
            base.Awake();
            SetupRootCanvas();
            CreateLayerRoots();
        }

        private void SetupRootCanvas()
        {
            Canvas rootCanvas = GetComponent<Canvas>();
            if (rootCanvas == null)
                rootCanvas = gameObject.AddComponent<Canvas>();

            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        private void CreateLayerRoots()
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                GameObject layerGo = new GameObject(layer.ToString());
                layerGo.layer = LayerMask.NameToLayer("UI");

                RectTransform rect = layerGo.AddComponent<RectTransform>();
                rect.SetParent(this.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                Canvas canvas = layerGo.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = LayerSortOrders.ContainsKey(layer)
                    ? LayerSortOrders[layer]
                    : 0;

                CanvasGroup canvasGroup = layerGo.AddComponent<CanvasGroup>();
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;

                layerGo.AddComponent<GraphicRaycaster>();

                _layerRoots[layer] = rect;

                Debug.Log($"[UIManager] 레이어 생성: {layer} (SortOrder: {canvas.sortingOrder})");
            }
        }

        public T Open<T>() where T : BaseUIView<T>
        {
            Type type = typeof(T);

            UIOptionAttribute attr = type.GetCustomAttribute<UIOptionAttribute>();
            if (attr == null)
            {
                Debug.LogError($"[UIManager] {type.Name} 에 UIOptionAttribute 가 없습니다.");
                return null;
            }

            if (attr.OpenMode == UIOpenMode.Single && _openedViews.ContainsKey(type))
            {
                Debug.LogWarning($"[UIManager] {type.Name} 이미 열려있습니다.");
                return _openedViews[type] as T;
            }

            GameObject prefab = Resources.Load<GameObject>(attr.Path);
            if (prefab == null)
            {
                Debug.LogError($"[UIManager] 프리팹 로드 실패: {attr.Path}");
                return null;
            }

            if (!_layerRoots.TryGetValue(attr.Layer, out Transform layerRoot))
            {
                Debug.LogError($"[UIManager] 레이어 루트 없음: {attr.Layer}");
                return null;
            }

            GameObject go = Instantiate(prefab, layerRoot);
            T view = go.GetComponent<T>();
            if (view == null)
            {
                Debug.LogError($"[UIManager] {type.Name} 컴포넌트를 찾을 수 없습니다.");
                Destroy(go);
                return null;
            }

            _openedViews[type] = view;
            view.OnOpen();

            return view;
        }

        public void Close<T>(BaseUIView<T> view) where T : BaseUIView<T>
        {
            Type type = typeof(T);
            view.OnClose();

            if (_openedViews.ContainsKey(type))
                _openedViews.Remove(type);

            Destroy(view.gameObject);
        }

        public void SetLayerVisible(UILayer layer, bool visible)
        {
            if (!_layerRoots.TryGetValue(layer, out Transform root)) return;

            CanvasGroup cg = root.GetComponent<CanvasGroup>();
            if (cg == null) return;

            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }

        public bool IsOpen<T>() where T : BaseUIView<T>
        {
            return _openedViews.ContainsKey(typeof(T));
        }

        public T Get<T>() where T : BaseUIView<T>
        {
            Type type = typeof(T);
            if (_openedViews.TryGetValue(type, out MonoBehaviour view))
                return view as T;

            return null;
        }

        public void CloseAll()
        {
            List<Type> keys = new List<Type>(_openedViews.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                MonoBehaviour view = _openedViews[keys[i]];
                if (view != null)
                    Destroy(view.gameObject);
            }
            _openedViews.Clear();
        }

        public void CloseAllInLayer(UILayer layer)
        {
            List<Type> keys = new List<Type>(_openedViews.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                MonoBehaviour view = _openedViews[keys[i]];
                if (view == null) continue;

                UIOptionAttribute attr = view.GetType().GetCustomAttribute<UIOptionAttribute>();
                if (attr != null && attr.Layer == layer)
                {
                    Destroy(view.gameObject);
                    _openedViews.Remove(keys[i]);
                }
            }
        }
    }
}