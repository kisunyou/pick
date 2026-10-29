using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class UIRenderTexture : MonoBehaviour
{
    RenderTexture _renderTexture { get; set; }

    [SerializeField] bool InitFlag = true;
    [SerializeField] public int width = 512;
    [SerializeField] public int height = 512;
    [SerializeField] RenderTextureFormat format = RenderTextureFormat.ARGB32;

    void Awake()
    {
        if (InitFlag == false)
            return;

        this.Init();
    }

    public void Init()
    {
        // 재초기화 시 이전 텍스처를 먼저 해제 (중복 생성 누수 방지)
        ReleaseRenderTexture();

        _renderTexture = new RenderTexture(width, height, 24, format);
        _renderTexture.name = $"instanceTextrue_{transform.name}";

        var cam = this.GetComponent<Camera>();
        cam.targetTexture = _renderTexture;

        // URP 카메라 스택의 Overlay 카메라들도 같은 타겟 텍스처를 써야 한다 (출력 설정이 다르면
        // "output properties do not match" 경고가 뜨고, Overlay가 이 텍스처가 아닌 다른 곳에 그려져
        // 좌표가 어긋나 보인다).
        UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
        if (cameraData != null)
        {
            foreach (Camera overlay in cameraData.cameraStack)
            {
                if (overlay != null)
                    overlay.targetTexture = _renderTexture;
            }
        }
    }

    // 직접 new 한 RenderTexture 는 GC 로 해제되지 않아 뷰(도감 상세·클리어 패널 등)가 닫힐 때마다
    // GPU 메모리가 누적된다 → 파괴 시 카메라/오버레이 타겟을 끊고 Release + Destroy 한다.
    void OnDestroy()
    {
        ReleaseRenderTexture();
    }

    void ReleaseRenderTexture()
    {
        if (_renderTexture == null)
            return;

        var cam = this.GetComponent<Camera>();
        if (cam != null)
        {
            if (cam.targetTexture == _renderTexture)
                cam.targetTexture = null;

            UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
            if (cameraData != null && cameraData.cameraStack != null)
            {
                foreach (Camera overlay in cameraData.cameraStack)
                {
                    if (overlay != null && overlay.targetTexture == _renderTexture)
                        overlay.targetTexture = null;
                }
            }
        }

        _renderTexture.Release();
        Destroy(_renderTexture);
        _renderTexture = null;
    }
}
