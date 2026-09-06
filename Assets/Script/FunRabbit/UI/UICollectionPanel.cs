using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 도감 한 칸의 표시 데이터 (UICollectionControl이 구성해 UICollectionItem에 전달)
    public struct UICollectionItemData
    {
        public string title;      // 다국어 동물 이름
        public string iconPath;   // 썸네일 스프라이트 경로 (미클리어 = 보스 아이콘)
        public string modelPath;  // 상세 팝업 3D 모델 경로 (미클리어 = 보스 프리팹)
        public string animalKey;  // 모델 텍스처에 적용할 actor.json 행 키 (변형 등급 반영, 예: bear_g)
        public int grade;         // 클리어 변형 등급: 0=원본, 1=_g(노멀 구간 클리어), 2=_r(하드 구간 클리어)
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
            int count = Mathf.Min(uICollectionItems.Length, items.Length);
            for (int i = 0; i < count; i++)
            {
                uICollectionItems[i].Set(items[i]);
            }
        }

    }

    public class UICollectionControl
    {
        // 도감은 원본 동물(1~StagesPerBand=12) 한 칸씩만 보여주고, 변형(_g/_r) 스테이지 클리어는
        // 이름 색(초록/빨강)과 상세 팝업 모델의 변형 텍스처로 표현한다.
        public void OnStart()
        {
            var view = UICollectionPanel.Get();
            if (view == null)
                return;

            int baseCount = GameQuestManager.StagesPerBand;   // 원본 동물 수 (12)

            // 등급/획득 판정은 "최고 클리어 스테이지" 기준 - 하드 순환(36→25) 후에도 36 클리어 기록이 유지된다
            int maxCleared = GameQuestManager.IsCheckInstance()
                ? GameQuestManager.Instance.MaxClearedStage
                : 0;

            var items = new UICollectionItemData[baseCount];
            for (int stage = 1; stage <= baseCount; stage++)
            {
                StageQuestData baseData = GameQuestData.GetStage(stage);
                if (baseData == null)
                    continue;

                // 이 동물의 최고 클리어 변형 등급: 하드(_r) 스테이지 클리어 > 노멀(_g) 클리어 > 원본
                // 예) 13(bear_g) 클리어 → 곰 등급 1(초록) / 36(elephant_r)까지 클리어 → 전 동물 등급 2(빨강)
                int grade = maxCleared >= stage + baseCount * 2 ? 2
                          : maxCleared >= stage + baseCount ? 1 : 0;

                // 등급에 해당하는 actor.json 행 키 (예: bear_g). 변형은 모델 프리팹을 원본과 공유하므로
                // 경로는 원본 그대로 쓰고, 상세 팝업에서 이 키의 행 texture만 덧입힌다.
                StageQuestData gradeData = grade > 0 ? GameQuestData.GetStage(stage + baseCount * grade) : baseData;
                string animalKey = gradeData != null ? gradeData.animalKey : baseData.animalKey;

                // 원본 스테이지를 클리어했으면 획득(active) 처리 (현재 도전 중인 스테이지 자신은 미포함)
                bool active = maxCleared >= stage;

                items[stage - 1] = new UICollectionItemData
                {
                    // animalKey 원문 대신 stringData의 다국어 이름으로 표시 (상세 팝업 타이틀에도 그대로 전달됨)
                    title = LanguageManager.Instance.Get(baseData.Doll.GetNameStringKey()),
                    // 클리어(획득)한 동물은 일반 인형, 미클리어는 보스 버전을 보여준다
                    // (썸네일 = _boss 아이콘, 상세 팝업 3D 모델 = _mon_prefab 보스 프리팹)
                    iconPath = active ? baseData.Doll.GetIconFullPath() : baseData.Doll.GetBossIconFullPath(),
                    modelPath = active ? baseData.Doll.GetModelPrefabFullPath() : baseData.Doll.GetBossModelPrefabFullPath(),
                    animalKey = animalKey,
                    grade = grade,
                    active = active,
                };
            }

            view.SetCollectionItems(items);
        }
    }
}
