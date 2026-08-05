using System;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIModelViewPanel : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] Camera UILayerCamera;
    [SerializeField] GameObject UILayerDirectionLight;
    [SerializeField] RawImage RawImage;
    // 이 패널 전용 렌더 레이어. -1이면 모델 레이어를 변경하지 않는다.
    [SerializeField] int modelLayer = -1;

    private UIModelViewController _modelController = null;
    private UIModelViewPanelControl _control = null;

    void Start()
    {
        UILayerCamera.gameObject.SetActive(true);
        UILayerDirectionLight.gameObject.SetActive(true);
        RawImage.enabled = true;
        if (RawImage.texture == null)
        {
            RawImage.texture = UILayerCamera.targetTexture;
        }
    }

    void InitControl()
    {
        if (null != _modelController)
            return;

        RawImage.raycastTarget = true;
        _modelController = RawImage.gameObject.AddComponent<EventTrigger>().gameObject.AddComponent<UIModelViewController>();
    }

    void Awake()
    {
        InitControl();
        // 로드된 모델은 이 패널의 transform을 부모로 사용한다.
        _control = new UIModelViewPanelControl(transform);
        RawImage.gameObject.SetActive(true);
        RawImage.enabled = false;
    }

    // 이 패널 전용 렌더 레이어를 지정한다.
    // 카메라 컬링 마스크와 (이미/이후) 로드되는 모델의 레이어를 함께 맞춰
    // 다른 패널의 카메라에 이 패널의 인형이 잡히지 않도록(겹침 방지) 한다.
    public void SetModelLayer(int layer)
    {
        modelLayer = layer;
        if (UILayerCamera != null && layer >= 0)
            UILayerCamera.cullingMask = 1 << layer;
        _control?.SetModelLayer(layer);
    }

    // 렌더 결과를 표시하는 RawImage의 RectTransform.
    // 패널/카메라/모델은 고정한 채 이 RawImage만 움직여 슬라이드 연출에 사용한다.
    public RectTransform ImageRect => RawImage != null ? RawImage.rectTransform : null;

    // RawImage(3D 렌더 결과) 색상을 지정한다. 미획득 인형을 검은 실루엣으로 표현하는 등에 사용.
    public void SetImageColor(Color color)
    {
        if (RawImage != null)
            RawImage.color = color;
    }

    // 모델을 비동기로 로드한다. (control로 위임)
    public Awaitable LoadModel(string fullPath)
    {
        return _control.LoadModel(fullPath, modelLayer);
    }

    // 모델을 동기로 즉시 로드/표시한다. (control로 위임) - 연출 시작 전 로딩 지연이 보이면 안 되는 경우 사용.
    public void LoadModelImmediate(string fullPath)
    {
        _control.LoadModelImmediate(fullPath, modelLayer);
    }

    void OnDestroy()
    {
        // 진행 중인 로드 취소 및 생성된 모델 정리
        _control?.Cleanup();
    }
}

public class UIModelViewPanelControl
{
    // 생성된 모델이 부모로 사용할 transform (UIModelViewPanel.transform)
    private readonly Transform _parent;

    // 현재 로드되어 표시 중인 모델 인스턴스
    private GameObject _modelInstance;

    // 진행 중인 비동기 로드를 취소하기 위한 토큰 소스
    private CancellationTokenSource _loadCts;

    // 로드된 모델에 적용할 레이어. -1이면 변경하지 않는다.
    private int _modelLayer = -1;

    public UIModelViewPanelControl(Transform parent)
    {
        _parent = parent;
    }

    // 전용 레이어 지정. 이미 로드된 모델이 있으면 즉시 반영한다.
    public void SetModelLayer(int layer)
    {
        _modelLayer = layer;
        if (_modelInstance != null && layer >= 0)
            SetLayerRecursively(_modelInstance, layer);
    }

    public async Awaitable LoadModel(string fullPath, int layer = -1)
    {
        _modelLayer = layer;

        if (string.IsNullOrEmpty(fullPath))
            return;

        // 이전 로드/모델이 있으면 먼저 정리 (중복 호출 대비)
        CancelLoad();
        DestroyModel();

        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        try
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(fullPath);

            // 로드가 끝날 때까지 프레임 단위로 대기 (취소 시 OperationCanceledException 발생)
            while (!request.isDone)
            {
                await Awaitable.NextFrameAsync(token);
            }

            token.ThrowIfCancellationRequested();

            if (request.asset is GameObject prefab)
                InstantiateModel(prefab);
            else
                Debug.LogError($"[UIModelViewPanelControl] 모델 로드 실패: {fullPath}");
        }
        catch (OperationCanceledException)
        {
            // 패널 파괴 등으로 로드가 취소됨 - 정상 흐름이므로 무시
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIModelViewPanelControl] 모델 로드 중 예외: {fullPath}\n{e}");
        }
    }

    // fullPath의 프리팹을 동기(Resources.Load)로 즉시 로드해 표시한다. 연출 시작 전 모든 모델을
    // 미리 준비해둬야 할 때(예: UIMissionClearPanel) 로딩 지연 없이 바로 인스턴스화하기 위해 사용한다.
    // Resources.Load는 이미 로드된 에셋에 대해 캐시를 즉시 반환하므로, Preload로 미리 데워둔 경로를
    // 넘기면 사실상 지연이 없다.
    public void LoadModelImmediate(string fullPath, int layer = -1)
    {
        _modelLayer = layer;

        if (string.IsNullOrEmpty(fullPath))
            return;

        CancelLoad();
        DestroyModel();

        GameObject prefab = Resources.Load<GameObject>(fullPath);
        if (prefab == null)
        {
            Debug.LogError($"[UIModelViewPanelControl] 모델 로드 실패: {fullPath}");
            return;
        }

        InstantiateModel(prefab);
    }

    // fullPath의 프리팹 에셋을 미리 Resources 캐시에 올려둔다(표시는 하지 않음).
    // 여러 모델을 나중에 LoadModelImmediate로 순간 전환해야 할 때 미리 호출해두면 그 시점의 로딩 지연이 없다.
    public static void Preload(string fullPath)
    {
        if (!string.IsNullOrEmpty(fullPath))
            Resources.Load<GameObject>(fullPath);
    }

    // 로드된 prefab을 이 패널의 transform 아래에 생성하고 위치/물리/레이어를 정리한다.
    // (LoadModel의 비동기 경로와 LoadModelImmediate의 동기 경로가 공통으로 사용)
    private void InstantiateModel(GameObject prefab)
    {
        // UIModelViewPanel.transform을 부모로 생성 후 로컬 좌표/회전을 0으로 초기화
        _modelInstance = UnityEngine.Object.Instantiate(prefab, _parent, false);
        _modelInstance.transform.localPosition = Vector3.zero;
        _modelInstance.transform.localEulerAngles = new Vector3(0, 180, 0);

        // Rigidbody가 있으면 물리(중력/충돌 등) 영향을 받지 않도록 kinematic 처리
        DisablePhysics(_modelInstance);

        // 전용 레이어가 지정된 경우 모델 전체를 해당 레이어로 옮긴다. (패널별 카메라 분리)
        if (_modelLayer >= 0)
            SetLayerRecursively(_modelInstance, _modelLayer);
    }

    // 오브젝트와 모든 자식의 레이어를 재귀적으로 설정한다.
    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null)
            return;

        go.layer = layer;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    // 모델에 포함된 모든 Rigidbody를 kinematic으로 만들어 물리 시뮬레이션을 적용하지 않는다.
    private static void DisablePhysics(GameObject instance)
    {
        if (instance == null)
            return;

        // 비활성 오브젝트의 Rigidbody까지 포함해서 처리
        Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
        }
    }

    private void CancelLoad()
    {
        if (_loadCts != null)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    private void DestroyModel()
    {
        if (_modelInstance != null)
        {
            UnityEngine.Object.Destroy(_modelInstance);
            _modelInstance = null;
        }
    }

    // 패널 OnDestroy 시 호출: 진행 중 로드 취소 + 생성 모델 제거
    public void Cleanup()
    {
        CancelLoad();
        DestroyModel();
    }
}
