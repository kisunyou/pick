using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace FunRabbit
{
    public class GameDollCreator : Singleton<GameDollCreator>
    {
        // 랜덤박스(doll_random_prefab) 프리팹 이름/경로 및 새로 생성할 때마다 섞어 넣을 개수 범위 (1~2개)
        private const string RandomBoxDollActorPrefabName = "doll_random_prefab";
        private const string RandomBoxPrefabPath = "Prefabs/dollPrefabs/" + RandomBoxDollActorPrefabName;
        private const int MinRandomBoxCount = 1;
        private const int MaxRandomBoxCountInclusive = 2;

        // 뽑기 풀 범위: 현재 스테이지 기준 뒤로 포함할 스테이지 수 (현재-15 ~ 현재-1, 현재/미래 제외)
        // (MissionSystem이 actor 미션 대상의 진행 가능 여부 판정에도 참조한다)
        public const int PoolStageRange = 15;

        // 황금 인형: 랜덤박스와 동일한 1~2개 규칙으로 섞여 나오는 특수 인형 (뽑으면 아군 3마리 합류).
        // 저장/로드 식별은 오브젝트 이름 끝의 suffix로 한다 (StageManager가 이름만 저장하므로).
        private const string GoldenNameSuffix = "_golden";
        private static readonly Color GoldenColor = new Color(1f, 0.75f, 0.1f, 1f);

        // 생성된 인형 보관 리스트 (다음 로드 시 정리용)
        private readonly List<GameObject> createdDolls = new List<GameObject>();

        // 리셋(전체 삭제 → 재생성) 진행 중 여부. 이 동안 인형 수가 잠깐 0이 되므로,
        // "인형 부족 무료 리셋" 조건 판정(UIHud)은 이 값을 보고 그 출렁임을 무시한다.
        public static bool IsResetting { get; private set; }

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
            IsResetting = true;

            // 기존 인형을 정리한 뒤 새로 생성한다.
            ClearCreatedDolls();
            yield return CreateRandomDolls();

            IsResetting = false;
            // 리셋이 끝난 상태로 무료 리셋 조건/카운트 배지를 재평가시킨다
            StageManager.NotifyActorCountChanged();
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

            // 현재 스테이지 직전 15단계(-15 ~ 현재-1)에 해당하는 actor.json 행(ActorData) 풀을 구성한다.
            List<ActorData> dollPool = GetDollActorPool();
            if (dollPool.Count == 0)
            {
                Debug.LogError("[GameDollCreator] 현재 스테이지에 사용할 인형 프리팹이 없습니다.");
                yield break;
            }

            // 이번 생성에서 랜덤박스로 대신 채울 위치 인덱스를 1~2개 무작위로 고른다.
            HashSet<int> randomBoxIndices = PickSpecialIndices(createPositions.Length, null);

            // 황금 인형 위치도 같은 1~2개 규칙으로 고른다 (랜덤박스 위치와 겹치지 않게).
            HashSet<int> goldenIndices = PickSpecialIndices(createPositions.Length, randomBoxIndices);

            for (int i = 0; i < createPositions.Length; i++)
            {
                if (createPositions[i] == null)
                {
                    Debug.LogWarning($"[GameDollCreator] DollCreatePositions[{i}] null 스킵.");
                    continue;
                }

                if (randomBoxIndices.Contains(i))
                {
                    yield return CreateRandomBoxDoll(createPositions[i], checkPos, i);
                    continue;
                }

                // 스테이지 풀에서 랜덤 액터 선택
                ActorData randomActor = dollPool[Random.Range(0, dollPool.Count)];
                string randomPath = GameCommon.GetModelPrefabFullPath(randomActor.animalKey);

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
                doll.transform.localScale = Vector3.one * GameActorData.GetInGameScale(randomActor.animalKey);

                // 프리팹에 baked-in된 base Actor를 DollBoxActor로 교체한다
                // (Collection/DollBox 모드가 동일 프리팹을 공유하므로, 타입 분기는 스폰 시점의 컴포넌트 교체로 처리)
                bool isGolden = goldenIndices.Contains(i);
                Actor baseActor = doll.GetComponent<Actor>();
                if (baseActor != null)
                {
                    DestroyImmediate(baseActor);
                    DollBoxActor actor = isGolden
                        ? doll.AddComponent<GoldenDollBoxActor>()
                        : doll.AddComponent<DollBoxActor>();
                    actor.Context.Data = new DollData(randomActor.animalKey);
                }

                // actor.json 행의 texture 필드 적용 - 변형(_g/_r) 인형은 색이 바뀐다
                // (황금 인형은 이어지는 황금 틴트가 텍스처를 덮으므로 순서 무관)
                GameCommon.ApplyDataTexture(doll, randomActor.animalKey);

                if (isGolden)
                    ApplyGoldenTint(doll);

                // animalKey 기반 네이밍 - 변형(bear_g 등)이 같은 프리팹을 공유하므로 저장/로드 식별은 키로 한다
                // (황금 인형은 추가 suffix)
                doll.name = isGolden ? $"{randomActor.animalKey}_{i}{GoldenNameSuffix}" : $"{randomActor.animalKey}_{i}";
                createdDolls.Add(doll);
            }

            Debug.Log($"[GameDollCreator] 인형 {createPositions.Length}개 생성 완료");

            // 생성 직후 배치를 저장해, 크레인 조작 전에 종료해도 황금/랜덤박스 구성이
            // 재접속 시 그대로 유지되게 한다. (Actor 등록은 Start에서 이뤄지므로 한 프레임 뒤 저장)
            yield return null;
            StageManager.Save(GameQuestManager.Instance.CurrentStage);
        }

        // createPositions 중 특수 인형(랜덤박스/황금 인형)으로 채울 위치 인덱스를
        // 1~2개(가용 슬롯 수 초과 불가) 중복 없이 고른다. exclude에 담긴 인덱스는 제외한다.
        private static HashSet<int> PickSpecialIndices(int slotCount, HashSet<int> exclude)
        {
            int excludeCount = exclude != null ? exclude.Count : 0;
            int available = Mathf.Max(0, slotCount - excludeCount);
            int count = Mathf.Min(Random.Range(MinRandomBoxCount, MaxRandomBoxCountInclusive + 1), available);

            HashSet<int> indices = new HashSet<int>();
            while (indices.Count < count)
            {
                int index = Random.Range(0, slotCount);
                if (exclude == null || !exclude.Contains(index))
                    indices.Add(index);
            }

            return indices;
        }

        // 로드된 인형 중 황금 인형이 하나도 없으면 일반 인형(랜덤박스 제외) 중 1~2개를
        // 황금으로 승격한다(타입 교체 + 틴트 + 이름 suffix). 승격했으면 true를 반환한다.
        private bool PromoteGoldenIfMissing()
        {
            List<GameObject> candidates = new List<GameObject>();

            foreach (GameObject doll in createdDolls)
            {
                if (doll == null)
                    continue;

                if (doll.GetComponent<GoldenDollBoxActor>() != null)
                    return false; // 이미 황금 인형이 있음

                if (doll.GetComponent<RandomBoxDollActor>() != null)
                    continue;

                if (doll.GetComponent<DollBoxActor>() != null)
                    candidates.Add(doll);
            }

            if (candidates.Count == 0)
                return false;

            int count = Mathf.Min(Random.Range(MinRandomBoxCount, MaxRandomBoxCountInclusive + 1), candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(0, candidates.Count);
                GameObject doll = candidates[pick];
                candidates.RemoveAt(pick);

                DollBoxActor oldActor = doll.GetComponent<DollBoxActor>();
                DollData dollData = oldActor.Context.Data;
                DestroyImmediate(oldActor);

                DollBoxActor golden = doll.AddComponent<GoldenDollBoxActor>();
                golden.Context.Data = dollData;

                ApplyGoldenTint(doll);

                if (!doll.name.EndsWith(GoldenNameSuffix))
                    doll.name += GoldenNameSuffix;
            }

            Debug.Log($"[GameDollCreator] 저장 배치에 황금 인형 없음 - {count}개 승격");
            return true;
        }

        // 인형의 모든 렌더러 머티리얼을 "순수 황금색"으로 만든다 (URP _BaseColor / 빌트인 _Color 모두 대응).
        // 텍스처는 아예 제거(null)해서 뜨개 무늬 없이 단색 황금으로 렌더링되게 한다.
        // renderer.materials 접근으로 인스턴스가 생성되므로 원본 머티리얼은 오염되지 않는다.
        private static void ApplyGoldenTint(GameObject doll)
        {
            foreach (Renderer renderer in doll.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (material == null)
                        continue;

                    // 메인 텍스처 제거 (mainTexture가 URP _BaseMap / 빌트인 _MainTex로 매핑된다)
                    material.mainTexture = null;
                    if (material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", null);
                    if (material.HasProperty("_MainTex"))
                        material.SetTexture("_MainTex", null);

                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", GoldenColor);
                    else if (material.HasProperty("_Color"))
                        material.SetColor("_Color", GoldenColor);

                    // GhibliSoft(인형 셰이더)의 회화풍 라이팅: 밝은 면은 순백, 그림자 면은 순흑으로
                    // 만들어 황금색 대비를 극대화한다
                    if (material.HasProperty("_WarmColor"))
                        material.SetColor("_WarmColor", Color.white);
                    if (material.HasProperty("_CoolColor"))
                        material.SetColor("_CoolColor", Color.black);
                }
            }
        }

        // position 위치에 랜덤박스(doll_random_prefab) 인형을 생성한다. animalKey가 없어 actor.json과 무관하다.
        private IEnumerator CreateRandomBoxDoll(Transform position, GameCheckPositions checkPos, int index)
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(RandomBoxPrefabPath);
            yield return request;

            if (request.asset == null)
            {
                Debug.LogError($"[GameDollCreator] 랜덤박스 프리팹 로드 실패: {RandomBoxPrefabPath}");
                yield break;
            }

            GameObject dollPrefab = request.asset as GameObject;
            Quaternion randomRot = position.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject doll = Instantiate(dollPrefab, position.position, randomRot, checkPos.PickMachine);

            Actor baseActor = doll.GetComponent<Actor>();
            if (baseActor != null)
            {
                DestroyImmediate(baseActor);
                doll.AddComponent<RandomBoxDollActor>();
            }

            doll.name = $"{dollPrefab.name}_{index}";
            createdDolls.Add(doll);
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

                // 이름에서 식별 토큰을 얻는다 (_golden/_숫자 제거).
                // 신규 저장 = animalKey(bear_g 등) / 구버전 저장 = 프리팹 이름(doll_bear_full_prefab)
                bool isGolden = objectName.EndsWith(GoldenNameSuffix);
                string token = GetPrefabName(objectName);

                ActorData actorRow = GameActorData.Get(token);

                string path;
                DollData dollData;
                bool isRandomBox = false;

                if (actorRow != null)
                {
                    // 신규 저장: actor.json 행이 모델/텍스처/스탯을 결정한다
                    path = GameCommon.GetModelPrefabFullPath(token);
                    dollData = new DollData(token);
                }
                else if (token == RandomBoxDollActorPrefabName)
                {
                    // 랜덤박스는 actor.json에 없는 별도 프리팹이라 DollData로 다룰 수 없다
                    path = RandomBoxPrefabPath;
                    dollData = null;
                    isRandomBox = true;
                }
                else
                {
                    // 구버전 저장(프리팹 이름 기반) 호환 - 원본 동물로 로드한다
                    path = $"Prefabs/dollPrefabs/{token}";
                    dollData = FindDollDataByPrefabName(token);
                }

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

                GameObject doll = Instantiate(dollPrefab, position, rotation, checkPos.PickMachine);
                doll.name = objectName;

                if (isRandomBox)
                {
                    // 저장 당시 스케일을 그대로 쓰고, 타입도 RandomBoxDollActor로 복원한다
                    doll.transform.localScale = data.scales[i];

                    Actor randomBoxBaseActor = doll.GetComponent<Actor>();
                    if (randomBoxBaseActor != null)
                    {
                        DestroyImmediate(randomBoxBaseActor);
                        doll.AddComponent<RandomBoxDollActor>();
                    }

                    createdDolls.Add(doll);
                    continue;
                }

                // 저장 당시 스케일 대신 현재 테이블(actor.json inGameScale)을 적용한다
                // (테이블 튜닝이 저장 데이터에 묻히지 않도록 항상 테이블이 우선.
                //  동물을 특정 못 하는 예외 케이스만 저장값 폴백)
                doll.transform.localScale = dollData != null
                    ? Vector3.one * GameActorData.GetInGameScale(dollData.animalKey)
                    : data.scales[i];

                Actor baseActor = doll.GetComponent<Actor>();
                if (baseActor != null)
                {
                    DestroyImmediate(baseActor);
                    DollBoxActor actor = isGolden
                        ? doll.AddComponent<GoldenDollBoxActor>()
                        : doll.AddComponent<DollBoxActor>();
                    actor.Context.Data = dollData;
                }

                // actor.json 행의 texture 필드 복원 (황금 틴트가 덮으므로 순서 무관)
                if (dollData != null)
                    GameCommon.ApplyDataTexture(doll, dollData.animalKey);

                if (isGolden)
                    ApplyGoldenTint(doll);

                createdDolls.Add(doll);
            }

            Debug.Log($"[GameDollCreator] 저장된 스테이지 정보로 인형 {count}개 로드 완료");

            // 황금 인형 도입 전의 저장 데이터(또는 황금을 모두 뽑아간 배치)에는 황금이 없다 -
            // 일반 인형 중에서 1~2개를 황금으로 승격해 재접속 시에도 항상 등장하게 한다.
            if (PromoteGoldenIfMissing())
            {
                yield return null;
                StageManager.Save(GameQuestManager.Instance.CurrentStage);
            }
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

        // 뽑기 기계에 나오는 인형은 현재 스테이지 기준 직전 PoolStageRange(15)단계의 액터로 제한한다.
        // 현재 도전 중/미래 스테이지의 동물은 나오지 않는다. 예) 스테이지 20 → 5~19단계 액터.
        // (초반은 최소 두 동물이 나오도록 상한을 2단계로 보정 - 이때만 현재 스테이지 동물이 포함될 수 있다)
        private static List<ActorData> GetDollActorPool()
        {
            int currentStage = GameQuestManager.Instance.CurrentStage;
            int minStage = Mathf.Max(1, currentStage - PoolStageRange);
            int maxStage = Mathf.Max(2, currentStage - 1);
            List<ActorData> pool = new List<ActorData>();

            for (int stage = minStage; stage <= maxStage; stage++)
            {
                ActorData data = GameActorData.GetByStage(stage);
                if (data == null)
                    continue;

                // 같은 동물(animalKey) 중복 제외
                if (pool.Exists(a => a.animalKey == data.animalKey))
                    continue;

                pool.Add(data);
            }

            return pool;
        }

        // 프리팹 이름(doll_{animalKey}_full_prefab)에 해당하는 DollData를 찾는다. (구버전 저장 호환용)
        // 변형(bear_g 등)도 같은 프리팹 이름을 쓰지만 actor.json에서 원본(1~12)이 먼저 오므로 원본이 매칭된다.
        private static DollData FindDollDataByPrefabName(string prefabName)
        {
            List<ActorData> actors = GameActorData.Actors;
            if (actors == null)
                return null;

            ActorData matched = actors.Find(a => GameCommon.GetModelPrefabName(a.animalKey) == prefabName);
            return matched != null ? new DollData(matched.animalKey) : null;
        }

        // gameObject.name(예: doll_bear_full_prefab_0, doll_bear_full_prefab_0_golden)에서
        // 끝의 황금 인형 suffix와 _숫자 인덱스를 제거해 프리팹 이름을 얻는다.
        private static string GetPrefabName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return objectName;

            // 황금 인형 suffix 먼저 제거 (프리팹 이름에는 포함되지 않는다)
            if (objectName.EndsWith(GoldenNameSuffix))
                objectName = objectName.Substring(0, objectName.Length - GoldenNameSuffix.Length);

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