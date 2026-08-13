using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UICollectionPanel",
        Layer = UILayer.Contents,
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
                // (현재 도전 중인 스테이지 자신은 아직 클리어 전이라 미포함 - GameDollCreator.GetStageQuestPool과 동일 기준)
                int currentStage = GameQuestManager.IsCheckInstance()
                    ? GameQuestManager.Instance.CurrentStage
                    : 1;

                for (int i = 0; i < questDataList.stages.Count; i++)
                {
                    var stageData = questDataList.stages[i];
                    // animalKey 원문 대신 stringData의 다국어 이름으로 표시 (상세 팝업 타이틀에도 그대로 전달됨)
                    titles[i] = LanguageManager.Instance.Get(stageData.Doll.GetNameStringKey());
                    actives[i] = stageData.stage < currentStage;
                    // 클리어(획득)한 스테이지는 일반 인형, 미클리어 스테이지는 보스 버전을 보여준다
                    // (썸네일 = _boss 아이콘, 상세 팝업 3D 모델 = _mon_prefab 보스 프리팹)
                    fullPaths[i] = actives[i]
                        ? stageData.Doll.GetIconFullPath()
                        : stageData.Doll.GetBossIconFullPath();
                    modelPaths[i] = actives[i]
                        ? stageData.Doll.GetModelPrefabFullPath()
                        : stageData.Doll.GetBossModelPrefabFullPath();
                }

                view.SetCollectionItems(titles, fullPaths, modelPaths, actives);
            }
        }
    }
}
