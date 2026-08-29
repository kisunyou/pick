using System.Collections;
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

        // 스테이지 클리어 뒤 미션 클리어 연출(UIMissionClearPanel)이 끝날 때까지 스폰을 미뤄둔 다음 보스.
        // null 이 아니면 "보스 교체 대기" 상태 - 아군은 Idle 로 기다리고, 새로 획득한 아군은 대기열에만 쌓인다.
        private ActorData _pendingBossData;
        private Coroutine _pendingBossWatcher;

        // 클리어 패널이 이 시간 이상 보이지 않으면(연출을 못 띄운 경우 등) 대기 중인 보스를 그냥 스폰한다
        private const float PENDING_BOSS_NO_PANEL_TIMEOUT = 3f;
        private const float PENDING_BOSS_POLL_INTERVAL = 0.25f;

        // 보스가 바뀔 때마다(파괴/스폰) 1씩 증가. ActorAttackState 가 스윙 시점의 값을 기억해두고
        // HIT_DELAY 뒤 실제 타격 시 값이 달라졌으면(그 사이 보스가 죽고 교체됨) 데미지를 버린다 -
        // 이전 보스에게 날린 마지막 타격이 새 보스 hp 를 깎는 문제 방지.
        public int BossGeneration { get; private set; }

        // 새 보스 스폰을 기다리는 중인지 (클리어 연출 구간)
        public bool IsWaitingNextBoss => _pendingBossData != null;

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
        // actorData == null(올클리어 단계)이면 보스를 없애고, 공격 대상이 사라진 아군/대기열도 함께 정리한다.
        // deferSpawn == true(스테이지 클리어로 넘어온 경우)면 이전 보스만 치우고 새 보스 스폰은
        // 미션 클리어 연출이 끝날 때(SpawnPendingBoss)까지 미룬다 - 그동안 아군은 Idle 로 대기한다.
        public void SetBoss(ActorData actorData, bool deferSpawn = false)
        {
            if (bossTransform == null)
                return;

            DestroyBossInstance();
            CancelPendingBoss();

            if (actorData == null)
            {
                ClearAllAllies();
                return;
            }

            if (deferSpawn)
            {
                _pendingBossData = actorData;
                SetAlliesWaiting();
                _pendingBossWatcher = StartCoroutine(PendingBossWatcher());
                return;
            }

            SpawnBoss(actorData);
        }

        // 미션 클리어 연출이 끝났을 때(UIMissionClearPanel 파괴) 호출된다. 대기 중인 새 보스를 스폰하고
        // Idle 로 기다리던 아군을 다시 이동(새 보스 사거리 기준 재정렬) → 공격으로 되돌린다.
        public void SpawnPendingBoss()
        {
            if (_pendingBossData == null)
                return;

            ActorData actorData = _pendingBossData;
            CancelPendingBoss();

            SpawnBoss(actorData);
            ResumeAllies();
        }

        private void SpawnBoss(ActorData actorData)
        {
            GameObject prefab = LoadDollPrefab(GameCommon.GetBossModelPrefabFullPath(actorData.animalKey), "보스");
            if (prefab == null)
                return;

            _bossInstance = Instantiate(prefab, bossTransform.position, bossTransform.rotation, bossTransform);
            _bossInstance.transform.localScale = Vector3.one * GameActorData.GetInGameScale(actorData.animalKey);
            BossGeneration++;

            // 보스도 ally와 동일하게 자신의 hp/공격 스탯을 채운다.
            BossBattleActor battleActor = SetupBattleActor<BossBattleActor>(_bossInstance);
            battleActor?.Setup(actorData);
        }

        private void DestroyBossInstance()
        {
            if (_bossInstance == null)
                return;

            Destroy(_bossInstance);
            _bossInstance = null;
            BossGeneration++;
        }

        private void CancelPendingBoss()
        {
            _pendingBossData = null;
            if (_pendingBossWatcher != null)
            {
                StopCoroutine(_pendingBossWatcher);
                _pendingBossWatcher = null;
            }
        }

        // 클리어 패널이 닫히는 순간은 UIMissionClearPanel.OnDestroy 가 SpawnPendingBoss 로 알려주지만,
        // 패널이 아예 뜨지 않았거나(HUD 없음 등) 알림이 유실된 경우를 위해 주기적으로 패널 존재를 확인한다 -
        // 패널이 PENDING_BOSS_NO_PANEL_TIMEOUT 동안 계속 없으면 그냥 스폰한다.
        private IEnumerator PendingBossWatcher()
        {
            float noPanelTime = 0f;
            while (_pendingBossData != null)
            {
                yield return new WaitForSeconds(PENDING_BOSS_POLL_INTERVAL);

                if (UIMissionClearPanel.Get() != null)
                {
                    noPanelTime = 0f;
                    continue;
                }

                noPanelTime += PENDING_BOSS_POLL_INTERVAL;
                if (noPanelTime >= PENDING_BOSS_NO_PANEL_TIMEOUT)
                {
                    _pendingBossWatcher = null;
                    SpawnPendingBoss();
                    yield break;
                }
            }
            _pendingBossWatcher = null;
        }

        // 슬롯의 살아있는 아군을 모두 Idle(대기)로 전환한다 - 보스가 없는 동안 허공을 때리지 않도록.
        private void SetAlliesWaiting()
        {
            if (_slotActors == null)
                return;

            foreach (AllyBattleActor ally in _slotActors)
            {
                if (ally != null && ally.Hp > 0)
                    ally.EnterWaiting();
            }
        }

        // 대기 중이던 아군을 다시 전투로 - Move 재진입이라 새 보스의 사거리에 맞춰 위치를 다시 잡은 뒤 공격한다.
        private void ResumeAllies()
        {
            if (_slotActors == null)
                return;

            foreach (AllyBattleActor ally in _slotActors)
            {
                if (ally != null && ally.Hp > 0)
                    ally.ResumeBattle();
            }
        }

        // 슬롯의 아군과 대기열을 모두 정리한다 (올클리어 - 보스 없음). 대기열 UI 아이콘도 같은 수만큼 비운다.
        private void ClearAllAllies()
        {
            if (_slotActors == null)
                return;

            for (int i = 0; i < _slotActors.Length; i++)
            {
                if (_slotActors[i] != null)
                    Destroy(_slotActors[i].gameObject);

                _slotActors[i] = null;
                _slotAnimalKeys[i] = null;
                _slotWasOccupied[i] = false;
            }

            int pendingCount = _pendingQueue.Count;
            _pendingQueue.Clear();
            for (int i = 0; i < pendingCount; i++)
                RemoveStackUIItem();

            SaveAllyBattleState();
        }

        // 보스 액터가 필드에 있는지. 없으면(올클리어 단계, 프리팹 로드 실패 등) 아군을 추가/스폰하지 않는다.
        public bool HasBoss => _bossInstance != null;

        // 현재 슬롯에 있는 ally 목록(빈 슬롯은 null 포함)을 반환한다.
        // BossBattleActor가 타겟 검색에 사용한다 (FindObjectsByType 대신 이미 추적 중인 목록을 재사용).
        public IReadOnlyList<AllyBattleActor> GetAllySlotActors() => _slotActors;

        // 미션 대상 인형 획득 시 호출: 슬롯 여유와 무관하게 무조건 대기열에 먼저 넣는다.
        // PENDING_SPAWN_DELAY(1초)가 지나고 빈 슬롯이 생기면 TryDequeuePendingAlly가 꺼내 스폰한다.
        public void AddAllyActor(ActorData actorData)
        {
            if (actorData == null || _slotActors == null || _slotActors.Length == 0)
                return;

            // 보스 액터가 없으면(올클리어 단계, 보스 프리팹 로드 실패 등) 공격할 대상이 없으므로 아군을 추가하지 않는다.
            // 단, 클리어 연출 중(다음 보스 스폰 대기)에는 대기열에 넣어두고 보스가 등장하면 그때 스폰한다.
            if (!HasBoss && !IsWaitingNextBoss)
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

            // 보스 액터가 없으면 스폰하지 않고 대기열에 그대로 둔다 (보스가 생기면 그때 스폰)
            if (!HasBoss)
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
