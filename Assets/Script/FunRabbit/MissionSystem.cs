using UnityEngine;

namespace FunRabbit
{
    // 뽑기 미션 시스템. GameQuestManager(스테이지/보스 진행)와는 독립적인 반복 미션이다.
    // mission.json의 미션을 percent 가중치로 하나 추첨해 진행한다:
    //  - actor     : 지정된 동물 인형을 collection_count개 뽑으면 성공 → 그 동물 아군 reward_count마리 소환
    //  - randombox : 랜덤박스를 collection_count개 뽑으면 성공 → 랜덤박스 reward_count개 지급
    // actor 미션의 대상은 "마지막 클리어 스테이지(=현재-1) 기준 -5까지" 총 6단계의 액터 중 랜덤 선정
    // (뽑기 기계 풀(GameDollCreator.PoolStageRange)에 반드시 포함되는 범위라 항상 진행 가능하다).
    // 성공 보상은 UIMissionHud 아이콘 위치에서 목적지(아군 스택 / 랜덤박스 버튼)로 트레일 연출 후 지급.
    // 진행 상황(미션 키/대상/진행도)은 PlayerContext(PlayerPrefs)에 저장되어 재시작해도 이어진다.
    public class MissionSystem : Singleton<MissionSystem>
    {
        // actor 미션 대상 선정 범위: 마지막 클리어 스테이지 기준 뒤로 몇 단계까지 후보로 삼을지 (-5 = 총 6단계)
        private const int TargetStageRange = 5;

        // 랜덤박스 미션의 HUD 아이콘 스프라이트 / 트레일로 날릴 아이콘 프리팹
        private const string RandomBoxIconSpritePath = "UI2/Images/UI_Etc/random_box_icon";
        private const string RandomBoxTrailIconPrefabPath = "UI2/Prefabs/MissionIconPrefab/randomBoxMissionIcon";

        // 현재 연결된 미션 HUD (UIMissionHud.Start/OnDestroy에서 등록/해제)
        private UIMissionHud _hud;

        // PlayerContext에 저장된 현재 진행 중 미션 (없으면 null)
        private MissionData CurrentMission => GameMissionData.Get(PlayerContext.GetMissionKey());

        // ── HUD 연결 ─────────────────────────────────────────────────

        public void AttachHud(UIMissionHud hud)
        {
            _hud = hud;
            SubscribeStageChanged();
            EnsureMission();
            RefreshHud();
        }

        // 스테이지 전환(클리어/디버그 점프) 감시 - 진행 불가 미션을 그 즉시 걸러내기 위함
        private void SubscribeStageChanged()
        {
            GameQuestManager questManager = GameQuestManager.Instance;
            questManager.OnStageChanged -= OnStageChanged;
            questManager.OnStageChanged += OnStageChanged;
        }

        // 스테이지가 바뀌면 진행 중 미션이 계속 진행 가능한지 검사한다.
        // 예) 스테이지 1 액터 10마리 미션인데 스테이지가 30이 되면 그 액터가 기계 풀(-15)에서 빠져
        // 더는 뽑을 수 없다 - 이때는 미션을 새로 추첨하고 HUD를 갱신한다.
        private void OnStageChanged(int stage, bool isClear)
        {
            MissionData mission = CurrentMission;
            if (mission == null)
                return;

            if (IsMissionPlayable(mission))
                return;

            Debug.Log($"[MissionSystem] 스테이지 {stage} 전환으로 미션 대상({PlayerContext.GetMissionAnimalKey()})이 " +
                      "뽑기 풀에서 벗어남 - 미션 재추첨");
            AssignNewMission();
            RefreshHud();
        }

        public void DetachHud(UIMissionHud hud)
        {
            if (_hud == hud)
                _hud = null;
        }

        // ── 뽑기 이벤트 (Basket에서 호출) ─────────────────────────────

        // 동물 인형 획득: actor 미션의 대상과 animalKey가 일치하면 진행도 +count.
        // count = 이 인형으로 합류하는 아군 수 (일반 1, 황금 인형 3 - Basket이 넘긴다)
        public void OnDollCollected(string animalKey, int count = 1)
        {
            EnsureMission();

            MissionData mission = CurrentMission;
            if (mission == null || !mission.IsActorType)
                return;

            if (animalKey != PlayerContext.GetMissionAnimalKey())
                return;

            AddProgress(mission, count);
        }

        // 랜덤박스 획득: randombox 미션이면 진행도 +1
        public void OnRandomBoxCollected()
        {
            EnsureMission();

            MissionData mission = CurrentMission;
            if (mission == null || mission.IsActorType)
                return;

            AddProgress(mission);
        }

        // ── 미션 수급/진행 ───────────────────────────────────────────

        // 진행 중 미션이 없거나(첫 실행/클리어 직후) 진행 불가능해졌으면 새 미션을 추첨한다.
        private void EnsureMission()
        {
            MissionData mission = CurrentMission;
            if (mission != null && IsMissionPlayable(mission))
                return;

            AssignNewMission();
        }

        // actor 미션의 대상이 현재 뽑기 기계 풀 범위(현재-PoolStageRange ~ 현재-1)에 있는지 확인.
        // 미션을 오래 묵혀 대상이 풀에서 빠지면(스테이지가 크게 진행) 진행 불가라 재추첨한다.
        private bool IsMissionPlayable(MissionData mission)
        {
            if (!mission.IsActorType)
                return true;

            ActorData target = GameActorData.Get(PlayerContext.GetMissionAnimalKey());
            if (target == null)
                return false;

            int currentStage = GameQuestManager.Instance.CurrentStage;
            int poolMax = Mathf.Max(2, currentStage - 1);
            int poolMin = Mathf.Max(1, currentStage - GameDollCreator.PoolStageRange);
            return target.stage >= poolMin && target.stage <= poolMax;
        }

        private void AssignNewMission()
        {
            MissionData mission = GameMissionData.PickRandomByPercent();
            if (mission == null)
            {
                Debug.LogError("[MissionSystem] mission.json에서 미션을 추첨할 수 없습니다.");
                return;
            }

            string targetAnimalKey = mission.IsActorType ? PickTargetAnimalKey() : string.Empty;
            PlayerContext.SetMission(mission.key, targetAnimalKey);

            Debug.Log($"[MissionSystem] 새 미션: key={mission.key} type={mission.mission_type} " +
                      $"target={targetAnimalKey} 목표={mission.collection_count} 보상={mission.reward_count}");
        }

        // 마지막 클리어 스테이지(=현재-1) 기준 -TargetStageRange까지의 액터 중 랜덤 선정
        private static string PickTargetAnimalKey()
        {
            int maxStage = Mathf.Max(2, GameQuestManager.Instance.CurrentStage - 1);
            int minStage = Mathf.Max(1, maxStage - TargetStageRange);

            ActorData data = GameActorData.GetByStage(Random.Range(minStage, maxStage + 1));
            if (data == null)
                data = GameActorData.GetByStage(1);

            return data != null ? data.animalKey : string.Empty;
        }

        private void AddProgress(MissionData mission, int amount = 1)
        {
            // 목표를 넘겨도 표기가 "4 / 3"처럼 되지 않게 목표치로 잘라 저장한다 (완료 판정에는 영향 없음)
            int progress = Mathf.Min(PlayerContext.GetMissionProgress() + amount, mission.collection_count);
            PlayerContext.SetMissionProgress(progress);

            if (_hud != null)
                _hud.UpdateMissionProgressText(progress, mission.collection_count);

            if (progress >= mission.collection_count)
                CompleteMission(mission);
        }

        // ── 미션 성공: 보상 트레일 + 지급 + 다음 미션 ─────────────────

        private void CompleteMission(MissionData mission)
        {
            string targetAnimalKey = PlayerContext.GetMissionAnimalKey();
            Debug.Log($"[MissionSystem] 미션 성공: key={mission.key} type={mission.mission_type} " +
                      $"target={targetAnimalKey} 보상={mission.reward_count}");

            PlayTrailAndGrantReward(mission, targetAnimalKey);

            // 다음 미션을 바로 추첨해 HUD를 새 미션으로 갱신한다 (보상 트레일은 그와 별개로 날아간다)
            AssignNewMission();
            RefreshHud();
        }

        // UIMissionHud 아이콘 위치 → (actor: 아군 스택 / randombox: HUD 랜덤박스 버튼) 트레일 후 도착 시 지급.
        // 연출을 재생할 수 없는 상황에서도 보상 지급은 보장한다.
        private void PlayTrailAndGrantReward(MissionData mission, string targetAnimalKey)
        {
            UIHud hud = UIHud.CreateOrGet();
            UIGetDollTrailHud trailHud = hud != null ? hud.GetDollTrailHud : null;
            if (trailHud == null)
            {
                GrantReward(mission, targetAnimalKey);
                return;
            }

            Vector3 start = _hud != null ? _hud.IconPosition : hud.transform.position;

            if (mission.IsActorType)
            {
                Transform allyTarget = hud.AllyStackActors != null ? hud.AllyStackActors.transform : null;
                if (allyTarget == null)
                {
                    GrantReward(mission, targetAnimalKey);
                    return;
                }

                trailHud.PlayTrail(start, allyTarget.position,
                    GameCommon.GetIconPrefabFullPath(targetAnimalKey),
                    () => GrantReward(mission, targetAnimalKey));
            }
            else
            {
                Transform boxTarget = trailHud.GetRandomBoxTargetTransform;
                if (boxTarget == null)
                {
                    GrantReward(mission, targetAnimalKey);
                    return;
                }

                trailHud.PlayTrail(start, boxTarget.position, RandomBoxTrailIconPrefabPath,
                    () => GrantReward(mission, targetAnimalKey));
            }
        }

        private static void GrantReward(MissionData mission, string targetAnimalKey)
        {
            if (mission.IsActorType)
            {
                ActorData actorData = GameActorData.Get(targetAnimalKey);
                if (actorData == null)
                {
                    Debug.LogError($"[MissionSystem] 보상 지급 실패 - 액터 없음: {targetAnimalKey}");
                    return;
                }

                if (!ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
                    return;

                for (int i = 0; i < mission.reward_count; i++)
                    battleSystem.AddAllyActor(actorData);
            }
            else
            {
                PlayerContext.AddRandomBox(mission.reward_count);
            }
        }

        // ── HUD 표시 ─────────────────────────────────────────────────

        // 현재 미션에 맞게 HUD 전체(아이콘/제목/진행도)를 갱신한다.
        private void RefreshHud()
        {
            if (_hud == null)
                return;

            MissionData mission = CurrentMission;
            if (mission == null)
                return;

            if (mission.IsActorType)
            {
                string targetAnimalKey = PlayerContext.GetMissionAnimalKey();
                _hud.SetMissionIconSprite(SpriteCache.Get(GameCommon.GetIconFullPath(targetAnimalKey)));
                _hud.SetMissionTitle(LanguageManager.Instance.Get(GameCommon.GetDollNameStringKey(targetAnimalKey)));
            }
            else
            {
                _hud.SetMissionIconSprite(SpriteCache.Get(RandomBoxIconSpritePath));
                _hud.SetMissionTitle(LanguageManager.Instance.Get("randombox_panel_name"));
            }

            _hud.UpdateMissionProgressText(PlayerContext.GetMissionProgress(), mission.collection_count);
        }
    }
}
