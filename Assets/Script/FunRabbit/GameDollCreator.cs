using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace FunRabbit
{
    public class GameDollCreator : Singleton<GameDollCreator>
    {
        // 생성된 인형 보관 리스트 (다음 로드 시 정리용)
        private readonly List<GameObject> createdDolls = new List<GameObject>();

        // ▼ 변경: 11종 프리팹 경로 배열로 등록
        private static readonly string[] DOLL_PATHS = new string[]
        {
            "doll_bear_full_prefab",
            "doll_pig_full_prefab",
            "doll_cow_full_prefab",
            "doll_duck_full_prefab",
            "doll_frog_full_prefab",
            "doll_horse_full_prefab",
            "doll_koala_full_prefab",
            "doll_monk_full_prefab",
            "doll_panda_full_prefab",
            "doll_lion_full_prefab",
            "doll_elephant_full_prefab",
        };

        private void Start()
        {
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

            // 저장된 정보가 없으면 기존대로 로드한다.
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

            for (int i = 0; i < createPositions.Length; i++)
            {
                if (createPositions[i] == null)
                {
                    Debug.LogWarning($"[GameDollCreator] DollCreatePositions[{i}] null 스킵.");
                    continue;
                }

                // 랜덤 프리팹 경로 선택
                string randomPath = $"Prefabs/dollPrefabs/{DOLL_PATHS[Random.Range(0, DOLL_PATHS.Length)]}";

                ResourceRequest request = Resources.LoadAsync<GameObject>(randomPath);
                yield return request;

                if (request.asset == null)
                {
                    Debug.LogError($"[GameDollCreator] 프리팹 로드 실패: {randomPath}");
                    continue;
                }

                GameObject dollPrefab = request.asset as GameObject;
                Quaternion randomRot = createPositions[i].rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject doll = Instantiate(dollPrefab, createPositions[i].position, randomRot);

                // ▼ 변경: 실제 프리팹 이름 기반으로 네이밍
                doll.name = $"{dollPrefab.name}_{i}";
                createdDolls.Add(doll);
            }

            Debug.Log($"[GameDollCreator] 인형 {createPositions.Length}개 생성 완료");
        }

        // StageManager에 저장된 정보로 인형을 로드한다.
        private IEnumerator LoadFromStageData(StageManager.StageData data)
        {
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

                GameObject doll = Instantiate(dollPrefab, position, rotation);
                doll.transform.localScale = data.scales[i];
                doll.name = objectName;
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