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

        public void SetCollectionItems(string[] titles, string[] fullPaths, string[] modelPaths, bool[] actives)
        {
            int count = Mathf.Min(uICollectionItems.Length, titles.Length, fullPaths.Length, modelPaths.Length, actives.Length);
            for (int i = 0; i < count; i++)
            {
                uICollectionItems[i].Set(titles[i], fullPaths[i], modelPaths[i], actives[i]);
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
                var questDataList = GameQuestData.StageQuestDataList;
                string[] titles = new string[questDataList.stages.Count];
                string[] fullPaths = new string[questDataList.stages.Count];
                string[] modelPaths = new string[questDataList.stages.Count];
                bool[] actives = new bool[questDataList.stages.Count];

                // 현재 스테이지보다 낮은(이미 클리어한) 스테이지만 획득(active) 처리
                int currentStage = GameQuestManager.IsCheckInstance()
                    ? GameQuestManager.Instance.CurrentStage
                    : 1;

                for (int i = 0; i < questDataList.stages.Count; i++)
                {
                    var stageData = questDataList.stages[i];
                    titles[i] = stageData.animalKey;
                    fullPaths[i] = stageData.Doll.GetIconFullPath();
                    modelPaths[i] = stageData.Doll.GetModelPrefabFullPath();
                    actives[i] = stageData.stage <= currentStage;
                }

                view.SetCollectionItems(titles, fullPaths, modelPaths, actives);
            }
        }
    }
}
