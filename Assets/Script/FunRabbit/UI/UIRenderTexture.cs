using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        _renderTexture = new RenderTexture(width, height, 24, format);
        _renderTexture.name = $"instanceTextrue_{transform.name}";

        var cam = this.GetComponent<Camera>();
        cam.targetTexture = _renderTexture;
    }
}
