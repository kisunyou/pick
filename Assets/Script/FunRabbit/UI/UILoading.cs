using UnityEngine;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UILoading",
        Layer = UILayer.System,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UILoading : BaseUIView<UILoading>
    {
        protected override void Awake()
        {
            base.Awake();
        }

        public override void OnOpen()
        {
        }

        public override void OnClose()
        {
        }
    }
}