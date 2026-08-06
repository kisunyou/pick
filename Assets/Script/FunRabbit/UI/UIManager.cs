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
        Contents,
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

        public bool IsShow => gameObject.activeSelf;

        protected virtual void Awake()
        {
            Instance = this as T;

            // 뷰 계층 아래 모든 버튼에 공통 클릭 효과음을 바인딩한다
            UIButtonSound.BindAll(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public virtual void OnOpen() { }
        public virtual void OnClose() { }

        public void Show()
        {
            // Awake 이후 동적으로 추가된 버튼까지 커버하도록 표시 시점에 재바인딩 (기존 바인딩은 무시됨)
            UIButtonSound.BindAll(gameObject);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

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
            { UILayer.Contents,  200 },
            { UILayer.Popup,     300 },
            { UILayer.Message,   400 },
            { UILayer.Directing, 500 },
            { UILayer.Webview,   600 },
            { UILayer.System,    700 },
        };

        private readonly Dictionary<UILayer, Transform> _layerRoots
            = new Dictionary<UILayer, Transform>();

        private readonly Dictionary<Type, MonoBehaviour> _openedViews
            = new Dictionary<Type, MonoBehaviour>();

        // 레이어에 열린 뷰가 하나도 없다가 생기면 OnLayerOpened, 있다가 없어지면 OnLayerClosed
        public event Action<UILayer> OnLayerOpened;
        public event Action<UILayer> OnLayerClosed;

        private readonly Dictionary<UILayer, bool> _layerOpenState = new Dictionary<UILayer, bool>();

        // 빈 레이어 캔버스 on/off용 캐시 (레이어 루트의 Canvas/Raycaster/정렬값)
        private struct LayerCanvasEntry
        {
            public Canvas canvas;
            public GraphicRaycaster raycaster;
            public int sortOrder;
        }
        private readonly List<LayerCanvasEntry> _layerCanvasEntries = new List<LayerCanvasEntry>();

        protected override void Awake()
        {
            base.Awake();
            SetupRootCanvas();
            CreateLayerRoots();
            RefreshLayerCanvases(); // 시작 시 전부 비어있으므로 전 레이어 캔버스 off
        }

        private void LateUpdate()
        {
            RefreshLayerCanvases();
        }

        // 하위에 아무것도 붙어 있지 않은 레이어 캔버스는 꺼서 빈 캔버스의
        // 렌더 배칭/레이캐스트 오버헤드를 없앤다. (뷰가 생기면 다시 켠다)
        // 뷰 생성/파괴 경로가 다양해(Open/Close/CloseAll/외부 Destroy) 훅 대신 매 프레임 검사한다
        // - 레이어 9개의 childCount 확인이라 비용은 무시 가능하고, 뷰가 열린 프레임에도
        //   LateUpdate가 렌더링 전에 캔버스를 켜므로 깜빡임이 없다.
        private void RefreshLayerCanvases()
        {
            for (int i = 0; i < _layerCanvasEntries.Count; i++)
            {
                LayerCanvasEntry entry = _layerCanvasEntries[i];
                if (entry.canvas == null)
                    continue;

                bool hasChild = entry.canvas.transform.childCount > 0;
                if (entry.canvas.enabled == hasChild)
                    continue;

                entry.canvas.enabled = hasChild;

                // 중첩 Canvas는 비활성화를 거치면 overrideSorting이 풀릴 수 있어 켤 때 재적용한다
                if (hasChild)
                {
                    entry.canvas.overrideSorting = true;
                    entry.canvas.sortingOrder = entry.sortOrder;
                }

                if (entry.raycaster != null)
                    entry.raycaster.enabled = hasChild;
            }
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

            ConfigureCanvasScaler(scaler);

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        // Scale With Screen Size / 1080x1920 / Match Width Or Height(0=Width) / PPU 100 - 모든 Canvas 공통 설정
        private static void ConfigureCanvasScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            scaler.referencePixelsPerUnit = 100f;
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

                // CanvasScaler는 최상단 루트 캔버스(SetupRootCanvas) 하나만 갖는다 - 하위 레이어 캔버스는
                // overrideSorting만 쓰고 스케일은 루트에서 그대로 상속받으므로 중복 계산이 불필요하다.

                CanvasGroup canvasGroup = layerGo.AddComponent<CanvasGroup>();
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;

                GraphicRaycaster raycaster = layerGo.AddComponent<GraphicRaycaster>();

                _layerRoots[layer] = rect;
                _layerCanvasEntries.Add(new LayerCanvasEntry
                {
                    canvas = canvas,
                    raycaster = raycaster,
                    sortOrder = canvas.sortingOrder,
                });

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

            CheckLayerOpenChanged(attr.Layer);

            return view;
        }

        public void Close<T>(BaseUIView<T> view) where T : BaseUIView<T>
        {
            Type type = typeof(T);
            view.OnClose();

            if (_openedViews.ContainsKey(type))
                _openedViews.Remove(type);

            Destroy(view.gameObject);

            UIOptionAttribute attr = type.GetCustomAttribute<UIOptionAttribute>();
            if (attr != null)
                CheckLayerOpenChanged(attr.Layer);
        }

        public void SetCanvasGroup(UILayer layer, bool interactable)
        {
            if (!_layerRoots.TryGetValue(layer, out Transform root)) return;

            CanvasGroup cg = root.GetComponent<CanvasGroup>();
            if (cg == null) return;

            cg.interactable = interactable;
        }

        public bool IsOpen<T>() where T : BaseUIView<T>
        {
            return _openedViews.ContainsKey(typeof(T));
        }

        // 지정한 레이어에 열려있는 뷰(BaseUIView 상속 객체)가 하나라도 있는지 확인
        public bool IsLayerOpen(UILayer layer)
        {
            foreach (KeyValuePair<Type, MonoBehaviour> kv in _openedViews)
            {
                if (kv.Value == null)
                    continue;

                UIOptionAttribute attr = kv.Key.GetCustomAttribute<UIOptionAttribute>();
                if (attr != null && attr.Layer == layer)
                    return true;
            }

            return false;
        }

        // 레이어의 열림 상태가 바뀌었을 때만 OnLayerOpened/OnLayerClosed를 발생시킨다
        // (Multiple 모드로 뷰가 여러 개 열려도 비어있음 <-> 있음 전환 시에만 1회 발생)
        private void CheckLayerOpenChanged(UILayer layer)
        {
            bool isOpen = IsLayerOpen(layer);
            bool wasOpen = _layerOpenState.TryGetValue(layer, out bool prev) && prev;
            if (isOpen == wasOpen)
                return;

            _layerOpenState[layer] = isOpen;

            if (isOpen)
                OnLayerOpened?.Invoke(layer);
            else
                OnLayerClosed?.Invoke(layer);
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
            HashSet<UILayer> affectedLayers = new HashSet<UILayer>();
            for (int i = 0; i < keys.Count; i++)
            {
                MonoBehaviour view = _openedViews[keys[i]];
                if (view != null)
                {
                    UIOptionAttribute attr = keys[i].GetCustomAttribute<UIOptionAttribute>();
                    if (attr != null)
                        affectedLayers.Add(attr.Layer);

                    Destroy(view.gameObject);
                }
            }
            _openedViews.Clear();

            foreach (UILayer layer in affectedLayers)
                CheckLayerOpenChanged(layer);
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

            CheckLayerOpenChanged(layer);
        }
    }
}