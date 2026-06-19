using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UICollectionPanel",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UICollectionPanel : BaseUIView<UICollectionPanel>
    {
        [SerializeField] Button closeButton;
        [SerializeField] UICollectionItem[] uICollectionItems;

        private UICollectionControl _control = new UICollectionControl();

        void Start()
        {
            closeButton.onClick.AddListener(() =>
            {
                Close();
            });
            _control.OnStart();
        }

        public void SetCollectionItems(string[] titles, string[] fullPaths)
        {
            int count = Mathf.Min(uICollectionItems.Length, titles.Length, fullPaths.Length);
            for (int i = 0; i < count; i++)
            {
                uICollectionItems[i].Set(titles[i], fullPaths[i]);
            }
        }

    }

    public class UICollectionControl
    {
        public void OnStart()
        {
            var view = UICollectionPanel.Get();
            if(view != null)
            {
                var questDataList = GameQuestData.QuestDataList;
                string[] titles = new string[questDataList.stages.Count];
                string[] fullPaths = new string[questDataList.stages.Count];

                for (int i = 0; i < questDataList.stages.Count; i++)
                {
                    var stageData = questDataList.stages[i];
                    titles[i] = stageData.animalKey;
                    fullPaths[i] = stageData.GetIconFullPath();
                }

                view.SetCollectionItems(titles, fullPaths);
            }
        }
    }
}
