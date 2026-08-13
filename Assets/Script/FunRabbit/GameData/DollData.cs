namespace FunRabbit
{
    // 특정 인형(Actor)의 정체성 데이터. animalKey를 기반으로 모델/아이콘 리소스 경로를 제공한다.
    // (스테이지/미션 규칙은 StageQuestData가 담당한다)
    [System.Serializable]
    public class DollData
    {
        public string animalKey;

        public DollData() { }

        public DollData(string animalKey)
        {
            this.animalKey = animalKey;
        }

        public string GetModelPrefabName()
        {
            return GameCommon.GetModelPrefabName(animalKey);
        }

        // Resources 기준 모델 프리팹 전체 경로
        public string GetModelPrefabFullPath()
        {
            return GameCommon.GetModelPrefabFullPath(animalKey);
        }

        // 보스 전용 모델 프리팹 전체 경로 (_mon_prefab)
        public string GetBossModelPrefabFullPath()
        {
            return GameCommon.GetBossModelPrefabFullPath(animalKey);
        }

        public string GetIconPrefabFullPath()
        {
            return GameCommon.GetIconPrefabFullPath(animalKey);
        }

        public string GetIconFullPath()
        {
            return GameCommon.GetIconFullPath(animalKey);
        }

        // 보스 버전 썸네일 (미클리어 스테이지의 도감 표시용)
        public string GetBossIconFullPath()
        {
            return GameCommon.GetBossIconFullPath(animalKey);
        }

        // 인형 표시 이름의 stringData 키 (표시 시점에 LanguageManager.Get으로 변환해서 사용)
        public string GetNameStringKey()
        {
            return GameCommon.GetDollNameStringKey(animalKey);
        }
    }
}
