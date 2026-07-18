using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace FunRabbit
{
    public class GameDollCreator : Singleton<GameDollCreator>
    {
        // 생성된 인형 보관 리스트 (다음 로드 시 정리용)
        private readonly List<GameObject> createdDolls = new List<GameObject>();

        private void Start()
        {
        }

        // 현재 스테이지를 저장 정보와 무관하게 새 인형으로 다시 생성한다.
        public void ResetCurrentStage()
        {
            StartCoroutine(ResetAndCreateDolls());
        }

        private IEnumerator ResetAndCreateDolls()
        {
            // 기존 인형을 정리한 뒤 새로 생성한다.
            ClearCreatedDolls();
            yield return CreateRandomDolls();
        }

        public void CreateDolls()
        {
            StartCoroutine(LoadAndCreateDolls());
        }

        private IEnumerator LoadAndCreateDolls()
        {
            // 기존에 로드된 인형들을 먼저 삭제한다.
            ClearCreatedDolls();

            // 현재 스테이지에 저장된 정보가 있으면 그 정보로 로드한다.
            int currentStage = GameQuestManager.Instance.CurrentStage;
            StageManager.StageData savedData = StageManager.GetStage(currentStage);

            if (savedData != null && savedData.prefabNames != null && savedData.prefabNames.Length > 0)
            {
                yield return LoadFromStageData(savedData);
                yield break;
            }

            // 저장된 정보가 없으면 기존대로 새로 생성한다.
            yield return CreateRandomDolls();
        }

        // 생성 위치마다 랜덤 프리팹을 골라 새 인형을 생성한다.
        private IEnumerator CreateRandomDolls()
        {
            if (!GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos))
            {
                Debug.LogError("[GameDollCreator] GameCheckPositions 인스턴스가 존재하지 않습니다.");
                yield break;
            }

            Transform[] createPositions = checkPos.DollCreatePositions;
            if (createPositions == null || createPositions.Length == 0)
            {
                Debug.LogError("[GameDollCreator] DollCreatePositions 배열이 비어있습니다.");
                yield break;
            }

            // 현재 스테이지까지 등장한 동물들의 StageQuestData 풀을 누적 구성한다.
            List<StageQuestData> dollPool = GetStageQuestPool();
            if (dollPool.Count == 0)
            {
                Debug.LogError("[GameDollCreator] 현재 스테이지에 사용할 인형 프리팹이 없습니다.");
                yield break;
            }

            for (int i = 0; i < createPositions.Length; i++)
            {
                if (createPositions[i] == null)
                {
                    Debug.LogWarning($"[GameDollCreator] DollCreatePositions[{i}] null 스킵.");
                    continue;
                }

                // 스테이지 풀에서 랜덤 StageQuestData 선택
                StageQuestData randomQuest = dollPool[Random.Range(0, dollPool.Count)];
                string randomPath = randomQuest.Doll.GetModelPrefabFullPath();

                ResourceRequest request = Resources.LoadAsync<GameObject>(randomPath);
                yield return request;

                if (request.asset == null)
                {
                    Debug.LogError($"[GameDollCreator] 프리팹 로드 실패: {randomPath}");
                    continue;
                }

                GameObject dollPrefab = request.asset as GameObject;
                Quaternion randomRot = createPositions[i].rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject doll = Instantiate(dollPrefab, createPositions[i].position, randomRot, checkPos.PickMachine);

                // 인게임(뽑기 기계) 인형은 actor.json의 inGameScale로 스케일링한다
                doll.transform.localScale = Vector3.one * GameActorData.GetInGameScale(randomQuest.animalKey);

                Actor actor = doll.GetComponent<Actor>();
                if (actor != null)
                    actor.Data = randomQuest.Doll;

                // ▼ 변경: 실제 프리팹 이름 기반으로 네이밍
                doll.name = $"{dollPrefab.name}_{i}";
                createdDolls.Add(doll);
            }

            Debug.Log($"[GameDollCreator] 인형 {createPositions.Length}개 생성 완료");
        }

        // StageManager에 저장된 정보로 인형을 로드한다.
        private IEnumerator LoadFromStageData(StageManager.StageData data)
        {
            if (!GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos))
            {
                Debug.LogError("[GameDollCreator] GameCheckPositions 인스턴스가 존재하지 않습니다.");
                yield break;
            }

            int count = data.prefabNames.Length;

            for (int i = 0; i < count; i++)
            {
                string objectName = data.prefabNames[i];
                if (string.IsNullOrEmpty(objectName))
                {
                    Debug.LogWarning($"[GameDollCreator] StageData prefabNames[{i}] 비어있음. 스킵.");
                    continue;
                }

                string prefabName = GetPrefabName(objectName);
                string path = $"Prefabs/dollPrefabs/{prefabName}";

                ResourceRequest request = Resources.LoadAsync<GameObject>(path);
                yield return request;

                if (request.asset == null)
                {
                    Debug.LogError($"[GameDollCreator] 프리팹 로드 실패: {path}");
                    continue;
                }

                GameObject dollPrefab = request.asset as GameObject;
                Vector3 position = data.positions[i];
                Quaternion rotation = Quaternion.Euler(data.rotations[i]);
                DollData dollData = FindDollDataByPrefabName(prefabName);

                GameObject doll = Instantiate(dollPrefab, position, rotation, checkPos.PickMachine);
                // 저장 당시 스케일 대신 현재 테이블(actor.json inGameScale)을 적용한다
                // (테이블 튜닝이 저장 데이터에 묻히지 않도록 항상 테이블이 우선.
                //  동물을 특정 못 하는 예외 케이스만 저장값 폴백)
                doll.transform.localScale = dollData != null
                    ? Vector3.one * GameActorData.GetInGameScale(dollData.animalKey)
                    : data.scales[i];
                doll.name = objectName;

                Actor actor = doll.GetComponent<Actor>();
                if (actor != null)
                    actor.Data = dollData;

                createdDolls.Add(doll);
            }

            Debug.Log($"[GameDollCreator] 저장된 스테이지 정보로 인형 {count}개 로드 완료");
        }

        // 기존에 생성된 인형들을 모두 삭제하고 리스트를 비운다.
        private void ClearCreatedDolls()
        {
            for (int i = 0; i < createdDolls.Count; i++)
            {
                if (createdDolls[i] != null)
                    Destroy(createdDolls[i]);
            }

            createdDolls.Clear();
        }

        // 현재 스테이지(1~N)까지 등장한 동물들의 StageQuestData를 누적해서 반환한다.
        // 예) 스테이지 1 → 곰만, 스테이지 3 → 곰·돼지·소
        private static List<StageQuestData> GetStageQuestPool()
        {
            int currentStage = GameQuestManager.Instance.CurrentStage;
            List<StageQuestData> pool = new List<StageQuestData>();

            for (int stage = 1; stage <= currentStage; stage++)
            {
                // GetStage는 첫 매치를 반환하므로 같은 stage 중복 항목(예: octopus)은 자연히 제외된다.
                StageQuestData data = GameQuestData.GetStage(stage);
                if (data == null)
                    continue;

                // 같은 동물(animalKey) 중복 제외
                if (pool.Exists(q => q.animalKey == data.animalKey))
                    continue;

                pool.Add(data);
            }

            return pool;
        }

        // 프리팹 이름(doll_{animalKey}_full_prefab)에 해당하는 DollData를 찾는다.
        private static DollData FindDollDataByPrefabName(string prefabName)
        {
            List<StageQuestData> stages = GameQuestData.StageQuestDataList?.stages;
            if (stages == null)
                return null;

            StageQuestData matched = stages.Find(q => q.Doll.GetModelPrefabName() == prefabName);
            return matched?.Doll;
        }

        // gameObject.name(예: doll_bear_full_prefab_0)에서 끝의 _숫자 인덱스를 제거해 프리팹 이름을 얻는다.
        private static string GetPrefabName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return objectName;

            int idx = objectName.LastIndexOf('_');
            if (idx > 0 && idx < objectName.Length - 1)
            {
                string suffix = objectName.Substring(idx + 1);
                if (int.TryParse(suffix, out _))
                    return objectName.Substring(0, idx);
            }

            return objectName;
        }
    }
}