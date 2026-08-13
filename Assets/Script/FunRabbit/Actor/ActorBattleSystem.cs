using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // Stage0에 배치된 보스 전투 연출 담당 (씬당 1개, battle_field).
    // 스테이지별 보스 몬스터 모델을 표시하고, 미션 대상 인형을 뽑을 때마다 스폰되는
    // ally 액터가 attackPower만큼 보스를 공격하는 연출을 재생한다.
    // 실제 hp 수치 기록/스테이지 클리어 판정은 GameQuestManager가 담당한다.
    //
    // ally 슬롯(왼쪽/오른쪽, allyTransforms)은 최대 2개까지 동시에 싸울 수 있다. AddAllyActor는 슬롯이
    // 비어있어도 무조건 대기열(_pendingQueue)에 먼저 넣고, PENDING_SPAWN_DELAY(1초)가 지나야 빈 슬롯에
    // 스폰될 수 있다 (매 프레임 큐 앞쪽을 확인). 슬롯 점유 상태(animalKey/hp)와 대기열은 PlayerPrefs에
    // 저장해 게임을 재시작해도 복원된다.
    public class ActorBattleSystem : InstanceSetter<ActorBattleSystem>
    {
        [SerializeField] private Transform bottomFloor;
        [SerializeField] private Transform bossTransform;
        [SerializeField] private Transform[] allyTransforms;

        private const string KEY_ALLY_SLOT_ANIMAL_KEY = "AllySlotAnimalKey";
        private const string KEY_ALLY_SLOT_HP = "AllySlotHp";
        private const string KEY_ALLY_PENDING_QUEUE = "AllyPendingQueue";
        private const char QUEUE_DELIMITER = ',';

        // 대기열에 들어간 뒤 스폰 가능해지기까지 최소 대기 시간(초)
        private const float PENDING_SPAWN_DELAY = 1f;

        // 대기열 항목 하나 (스폰 가능해지는 시각을 함께 들고 있다)
        private struct PendingAllyEntry
        {
            public readonly string AnimalKey;
            public readonly float ReadyTime;

            public PendingAllyEntry(string animalKey, float readyTime)
            {
                AnimalKey = animalKey;
                ReadyTime = readyTime;
            }
        }

        private GameObject _bossInstance;

        // 슬롯별 상태 (allyTransforms와 같은 길이). Start에서 초기화한다.
        private AllyBattleActor[] _slotActors;
        private string[] _slotAnimalKeys;
        private int[] _slotSavedHp;
        private bool[] _slotWasOccupied;

        // 스폰을 기다리는 목록 (슬롯 여유와 무관하게 AddAllyActor 시 항상 여기로 들어간다)
        private readonly Queue<PendingAllyEntry> _pendingQueue = new Queue<PendingAllyEntry>();

        private void Start()
        {
            InitSlots();

            // GameQuestManager는 PlayerPrefs 기반이라 씬 로드 여부와 무관하게 바로 사용 가능하다.
            StageQuestData stageData = GameQuestManager.Instance.GetCurrentStageData();

            // 최초 실행(PlayerPrefs 없음)이면 위 조회 자체가 GameQuestManager.SetCurrentStage(1)을 트리거해
            // RefreshBattleBoss -> SetBoss가 이미 호출됐을 수 있다(재진입). 중복 스폰을 막는다.
            if (stageData != null && _bossInstance == null)
                SetBoss(GameActorData.Get(stageData.animalKey));

            RestoreAllyBattleState();

            // 랜덤박스 패널이 열린 채 앱이 종료됐던 경우: 지급 대기 중인 아군 액터 보상을
            // 트레일 연출 없이 바로 대기열에 넣는다. (평상시 지급은 UIRandomboxPanel이 패널 닫힘 시 처리)
            GrantPendingAllyRewards();
        }

        // 지급 대기 중인 랜덤박스 아군 액터 보상을 모두 지급한다 (재시작 복원 전용).
        private void GrantPendingAllyRewards()
        {
            foreach (PlayerContext.PendingAllyReward reward in PlayerContext.GetPendingAllyRewards())
            {
                PlayerContext.RemovePendingAllyReward(reward.animalKey, reward.count);
                AddAllyActors(reward.animalKey, reward.count);
            }
        }

        private void InitSlots()
        {
            int count = allyTransforms != null ? allyTransforms.Length : 0;
            _slotActors = new AllyBattleActor[count];
            _slotAnimalKeys = new string[count];
            _slotSavedHp = new int[count];
            _slotWasOccupied = new bool[count];
        }

        private void Update()
        {
            for (int i = 0; i < _slotActors.Length; i++)
                UpdateSlot(i);

            TryDequeuePendingAlly();
        }

        // 슬롯 하나의 상태를 갱신한다: 전투가 막 끝났으면(파괴됨) 정리하고,
        // 살아있으면 hp 변경분을 저장한다(피격 중 재접속해도 최신 hp로 복원되도록).
        private void UpdateSlot(int index)
        {
            AllyBattleActor ally = _slotActors[index];

            if (ally == null)
            {
                if (_slotWasOccupied[index])
                {
                    _slotWasOccupied[index] = false;
                    _slotAnimalKeys[index] = null;
                    SaveAllyBattleState();
                }

                return;
            }

            _slotWasOccupied[index] = true;

            int currentHp = ally.Hp;
            if (currentHp != _slotSavedHp[index])
            {
                _slotSavedHp[index] = currentHp;
                PlayerPrefs.SetInt(SlotHpKey(index), currentHp);
                PlayerPrefs.Save();
            }
        }

        // 스테이지의 보스 몬스터 모델을 (재)스폰한다. GameQuestManager가 스테이지 전환마다 호출한다.
        public void SetBoss(ActorData actorData)
        {
            if (bossTransform == null || actorData == null)
                return;

            if (_bossInstance != null)
                Destroy(_bossInstance);

            GameObject prefab = LoadDollPrefab(GameCommon.GetBossModelPrefabFullPath(actorData.animalKey), "보스");
            if (prefab == null)
                return;

            _bossInstance = Instantiate(prefab, bossTransform.position, bossTransform.rotation, bossTransform);
            _bossInstance.transform.localScale = Vector3.one * GameActorData.GetInGameScale(actorData.animalKey);

            // 보스도 ally와 동일하게 자신의 hp/공격 스탯을 채운다.
            BossBattleActor battleActor = SetupBattleActor<BossBattleActor>(_bossInstance);
            battleActor?.Setup(actorData);
        }

        // 현재 슬롯에 있는 ally 목록(빈 슬롯은 null 포함)을 반환한다.
        // BossBattleActor가 타겟 검색에 사용한다 (FindObjectsByType 대신 이미 추적 중인 목록을 재사용).
        public IReadOnlyList<AllyBattleActor> GetAllySlotActors() => _slotActors;

        // 미션 대상 인형 획득 시 호출: 슬롯 여유와 무관하게 무조건 대기열에 먼저 넣는다.
        // PENDING_SPAWN_DELAY(1초)가 지나고 빈 슬롯이 생기면 TryDequeuePendingAlly가 꺼내 스폰한다.
        public void AddAllyActor(ActorData actorData)
        {
            if (actorData == null || _slotActors == null || _slotActors.Length == 0)
                return;

            EnqueuePendingAlly(actorData.animalKey);
        }

        // 랜덤박스 아군 액터 보상 지급: animalKey ally를 count마리 대기열에 넣는다.
        // (패널 닫힘 트레일 도착 시 UIRandomboxPanelControl이, 재시작 복원은 GrantPendingAllyRewards가 호출)
        public void AddAllyActors(string animalKey, int count)
        {
            ActorData actorData = GameActorData.Get(animalKey);
            if (actorData == null)
            {
                Debug.LogError($"[ActorBattleSystem] AddAllyActors: 알 수 없는 animalKey {animalKey}");
                return;
            }

            for (int i = 0; i < count; i++)
                AddAllyActor(actorData);
        }

        private bool TryFindFreeSlot(out int slotIndex)
        {
            for (int i = 0; i < _slotActors.Length; i++)
            {
                if (_slotActors[i] == null)
                {
                    slotIndex = i;
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }

        private void EnqueuePendingAlly(string animalKey)
        {
            _pendingQueue.Enqueue(new PendingAllyEntry(animalKey, Time.time + PENDING_SPAWN_DELAY));
            SaveAllyBattleState();
            AddStackUIItem(animalKey);
        }

        // 매 프레임 대기열 맨 앞을 확인해, 대기 시간이 지났고 빈 슬롯이 있으면 그제서야 스폰한다.
        private void TryDequeuePendingAlly()
        {
            if (_pendingQueue.Count == 0)
                return;

            PendingAllyEntry entry = _pendingQueue.Peek();
            if (Time.time < entry.ReadyTime)
                return;

            if (!TryFindFreeSlot(out int slotIndex))
                return;

            _pendingQueue.Dequeue();
            RemoveStackUIItem();

            ActorData actorData = GameActorData.Get(entry.AnimalKey);
            if (actorData != null)
                SpawnAllyAtSlot(actorData, slotIndex, actorData.allyHp);

            SaveAllyBattleState();
        }

        // 대기열(UIAllyStackActors) UI에 항목을 추가/제거한다. _pendingQueue의 Enqueue/Dequeue와
        // 항상 짝을 맞춰 호출해 화면에 보이는 순서가 실제 대기열 순서와 일치하도록 한다.
        private void AddStackUIItem(string animalKey)
        {
            if (UIHud.Instance != null && UIHud.Instance.AllyStackActors != null)
                UIHud.Instance.AllyStackActors.AddItem(GameCommon.GetIconFullPath(animalKey));
        }

        private void RemoveStackUIItem()
        {
            if (UIHud.Instance != null && UIHud.Instance.AllyStackActors != null)
                UIHud.Instance.AllyStackActors.RemoveOldestItem();
        }

        // allyTransforms[slotIndex] 자식으로 ally 인형 모델을 스폰한다. overrideHp로 시작 hp를 지정한다
        // (신규 스폰은 만피, 재접속 복원은 저장된 hp).
        // 이후 이동/공격/죽음은 AllyBattleActor.Setup이 자체적으로 진행한다.
        private void SpawnAllyAtSlot(ActorData actorData, int slotIndex, int overrideHp)
        {
            GameObject prefab = LoadDollPrefab(GameCommon.GetModelPrefabFullPath(actorData.animalKey), "ally");
            if (prefab == null)
                return;

            Transform slotTransform = allyTransforms[slotIndex];
            GameObject instance = Instantiate(prefab, slotTransform.position, slotTransform.rotation, slotTransform);
            instance.transform.localScale = Vector3.one * GameActorData.GetInGameScale(actorData.animalKey);

            AllyBattleActor battleActor = SetupBattleActor<AllyBattleActor>(instance);
            if (battleActor == null)
                return;

            battleActor.Setup(actorData, bossTransform);

            if (overrideHp > 0 && overrideHp != battleActor.Hp)
                battleActor.Hp = overrideHp;

            _slotActors[slotIndex] = battleActor;
            _slotAnimalKeys[slotIndex] = actorData.animalKey;
            _slotSavedHp[slotIndex] = battleActor.Hp;
            _slotWasOccupied[slotIndex] = true;

            SaveAllyBattleState();
        }

        // 슬롯 점유 상태(animalKey/hp)와 대기열을 PlayerPrefs에 저장한다.
        private void SaveAllyBattleState()
        {
            for (int i = 0; i < _slotActors.Length; i++)
            {
                if (_slotActors[i] != null)
                {
                    PlayerPrefs.SetString(SlotAnimalKeyKey(i), _slotAnimalKeys[i]);
                    PlayerPrefs.SetInt(SlotHpKey(i), _slotSavedHp[i]);
                }
                else
                {
                    PlayerPrefs.DeleteKey(SlotAnimalKeyKey(i));
                    PlayerPrefs.DeleteKey(SlotHpKey(i));
                }
            }

            // PlayerPrefs에는 animalKey만 저장한다 (ReadyTime은 재시작 후엔 의미가 없어 복원 시 즉시 준비 상태로 취급).
            List<string> pendingAnimalKeys = new List<string>(_pendingQueue.Count);
            foreach (PendingAllyEntry entry in _pendingQueue)
                pendingAnimalKeys.Add(entry.AnimalKey);

            PlayerPrefs.SetString(KEY_ALLY_PENDING_QUEUE, string.Join(QUEUE_DELIMITER.ToString(), pendingAnimalKeys));
            PlayerPrefs.Save();
        }

        // 저장된 슬롯 점유 상태/대기열을 복원한다 (게임 재시작 시 Start에서 호출).
        private void RestoreAllyBattleState()
        {
            for (int i = 0; i < _slotActors.Length; i++)
            {
                string animalKey = PlayerPrefs.GetString(SlotAnimalKeyKey(i), string.Empty);
                if (string.IsNullOrEmpty(animalKey))
                    continue;

                ActorData actorData = GameActorData.Get(animalKey);
                if (actorData == null)
                    continue;

                int savedHp = PlayerPrefs.GetInt(SlotHpKey(i), actorData.allyHp);
                SpawnAllyAtSlot(actorData, i, savedHp);
            }

            string queueString = PlayerPrefs.GetString(KEY_ALLY_PENDING_QUEUE, string.Empty);
            if (string.IsNullOrEmpty(queueString))
                return;

            // 재시작 직후라 대기 시간(PENDING_SPAWN_DELAY)은 의미가 없으므로 즉시 스폰 가능한 상태로 복원한다.
            foreach (string animalKey in queueString.Split(QUEUE_DELIMITER))
            {
                if (string.IsNullOrEmpty(animalKey))
                    continue;

                _pendingQueue.Enqueue(new PendingAllyEntry(animalKey, Time.time));
                AddStackUIItem(animalKey);
            }
        }

        private static string SlotAnimalKeyKey(int index) => $"{KEY_ALLY_SLOT_ANIMAL_KEY}{index}";
        private static string SlotHpKey(int index) => $"{KEY_ALLY_SLOT_HP}{index}";

        // 프리팹에 baked-in된 base Actor를 T(AllyBattleActor/BossBattleActor)로 교체한다.
        // (Collection/DollBox 모드와 동일하게, 타입 분기는 스폰 시점의 컴포넌트 교체로 처리)
        private T SetupBattleActor<T>(GameObject instance) where T : BattleActor
        {
            Actor baseActor = instance.GetComponent<Actor>();
            if (baseActor == null)
                return null;

            // DestroyImmediate + AddComponent<T>는 새 컴포넌트를 기본값으로 만들어 SerializeField가 유실되므로,
            // 스왑 직전에 값을 읽어뒀다가 새 컴포넌트에 그대로 옮겨준다.
            Rigidbody rigidbody = baseActor.RigidbodyComponent;
            Collider[] colliders = baseActor.CollidersArray;
            Transform bottomSocket = baseActor.BottomSocket;
            Transform headSocket = baseActor.HeadSocket;

            DestroyImmediate(baseActor);

            T battleActor = instance.AddComponent<T>();
            battleActor.SetSwappedReferences(rigidbody, colliders, bottomSocket, headSocket);
            battleActor.DisablePhysics();
            battleActor.SetupFloor(bottomFloor);

            return battleActor;
        }

        // 지정 경로의 인형 모델 프리팹을 로드한다.
        private GameObject LoadDollPrefab(string path, string logLabel)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"[ActorBattleSystem] {logLabel} 프리팹 로드 실패: {path}");

            return prefab;
        }
    }
}
