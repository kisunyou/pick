using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 도감 한 칸의 표시 데이터 (UICollectionControl이 구성해 UICollectionItem에 전달)
    public struct UICollectionItemData
    {
        public string title;      // 다국어 동물 이름
        public string iconPath;   // 썸네일 스프라이트 경로 (미클리어도 일반 아이콘 - 그림자로 표시)
        public string modelPath;  // 상세 팝업 3D 모델 경로 (미클리어도 일반 프리팹 - 실루엣으로 표시)
        public string animalKey;  // 상세 팝업 모델에 적용할 actor.json 행 키
        public int grade;         // 이름 색 등급 (변형 _b/_r 은 신규 동물로 대체돼 현재 항상 0)
        public bool active;       // 획득(해당 원본 스테이지 클리어) 여부
    }

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

        public void SetCollectionItems(UICollectionItemData[] items)
        {
            // 프리팹 칸(12)보다 동물(28)이 많으면 마지막 칸을 복제해 채운다 (Content 의 GridLayout·ContentSizeFitter 가 배치·스크롤 길이를 맞춤)
            if (uICollectionItems.Length > 0 && uICollectionItems.Length < items.Length)
            {
                var list = new System.Collections.Generic.List<UICollectionItem>(uICollectionItems);
                UICollectionItem template = uICollectionItems[uICollectionItems.Length - 1];
                while (list.Count < items.Length)
                {
                    UICollectionItem copy = Instantiate(template, template.transform.parent);
                    copy.name = template.name;
                    list.Add(copy);
                }
                uICollectionItems = list.ToArray();
            }

            int count = Mathf.Min(uICollectionItems.Length, items.Length);
            for (int i = 0; i < count; i++)
            {
                uICollectionItems[i].Set(items[i]);
            }
        }

    }

    public class UICollectionControl
    {
        // 미획득 동물 이름 표기
        const string UnknownTitle = "???";

        // 신규 동물과 아직 교체하지 않은 색 변형을 스테이지 순서대로 한 칸씩 보여준다.
        public void OnStart()
        {
            var view = UICollectionPanel.Get();
            if (view == null)
                return;

            int slotCount = GameQuestManager.TotalPlayableStageCount;   // 28

            // 획득 판정은 "최고 클리어 스테이지" 기준 - 반복(28→1) 후에도 28 클리어 기록이 유지된다
            int maxCleared = GameQuestManager.IsCheckInstance()
                ? GameQuestManager.Instance.MaxClearedStage
                : 0;

            var items = new UICollectionItemData[slotCount];
            for (int stage = 1; stage <= slotCount; stage++)
            {
                StageQuestData stageData = GameQuestData.GetStage(stage);
                if (stageData == null)
                    continue;

                // 그 스테이지를 클리어했으면 획득(active) 처리 (현재 도전 중인 스테이지 자신은 미포함)
                bool active = maxCleared >= stage;

                items[stage - 1] = new UICollectionItemData
                {
                    // animalKey 원문 대신 stringData의 다국어 이름으로 표시 (상세 팝업 타이틀에도 그대로 전달됨)
                    // 아직 뽑지 않은 동물은 이름을 가린다 (언어 무관 "???")
                    title = active ? LanguageManager.Instance.Get(stageData.Doll.GetNameStringKey()) : UnknownTitle,
                    // 썸네일·상세 팝업 3D 모델 모두 항상 일반 인형 - 미클리어는 검은 그림자(실루엣)로 표시한다
                    iconPath = stageData.Doll.GetIconFullPath(),
                    modelPath = stageData.Doll.GetModelPrefabFullPath(),
                    animalKey = stageData.animalKey,
                    grade = 0,
                    active = active,
                };
            }

            view.SetCollectionItems(items);
        }
    }
}
