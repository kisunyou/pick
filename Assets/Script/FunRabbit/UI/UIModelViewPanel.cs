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
        RawImage.gameObject.SetActive(true);
        RawImage.enabled = false;
    }
}
