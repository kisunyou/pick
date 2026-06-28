using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIMissionClearPanel",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIMissionClearPanel : BaseUIView<UIMissionClearPanel>
    {
        [SerializeField] UIModelViewPanel uiModelViewPanel;
        [SerializeField] Button closeButton;

        void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        public void SetData(string modelFullPath)
        {
            if (uiModelViewPanel != null)
                _ = uiModelViewPanel.LoadModel(modelFullPath);
        }
    }
}
