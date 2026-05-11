using System.Collections;
using UnityEngine;

namespace FunRabbit
{
    public class GameDollCreator : Singleton<GameDollCreator>
    {
        private const string DOLL_FROG_PATH = "Prefabs/dollPrefabs/doll_frog_full_prefab";

        private void Start()
        {
        }

        public void CreateDolls()
        {
            StartCoroutine(LoadAndCreateDolls());
        }

        private IEnumerator LoadAndCreateDolls()
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(DOLL_FROG_PATH);
            yield return request;

            if (request.asset == null)
            {
                Debug.LogError($"[GameDollCreator] 프리팹 로드 실패: {DOLL_FROG_PATH}");
                yield break;
            }

            GameObject dollPrefab = request.asset as GameObject;

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

                Quaternion randomRot = createPositions[i].rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject doll = Instantiate(dollPrefab, createPositions[i].position, randomRot);
                doll.name = $"doll_frog_{i}";
            }

            Debug.Log($"[GameDollCreator] 인형 {createPositions.Length * 3}개 생성 완료");
        }
    }
}