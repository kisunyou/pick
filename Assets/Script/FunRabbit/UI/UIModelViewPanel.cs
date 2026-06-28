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

    // 모델을 비동기로 로드한다. (control로 위임)
    public Awaitable LoadModel(string fullPath)
    {
        return _control.LoadModel(fullPath);
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

    public UIModelViewPanelControl(Transform parent)
    {
        _parent = parent;
    }

    public async Awaitable LoadModel(string fullPath)
    {
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
            {
                // UIModelViewPanel.transform을 부모로 생성 후 로컬 좌표/회전을 0으로 초기화
                _modelInstance = UnityEngine.Object.Instantiate(prefab, _parent, false);
                _modelInstance.transform.localPosition = Vector3.zero;
                _modelInstance.transform.localEulerAngles = new Vector3(0, 180, 0);

                // Rigidbody가 있으면 물리(중력/충돌 등) 영향을 받지 않도록 kinematic 처리
                DisablePhysics(_modelInstance);
            }
            else
            {
                Debug.LogError($"[UIModelViewPanelControl] 모델 로드 실패: {fullPath}");
            }
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
