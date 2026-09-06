using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class Basket : MonoBehaviour
    {
        [SerializeField] BoxCollider _collider;
        // 미션과 무관한 인형 1개가 채우는 랜덤박스 진행 게이지 양 (1이 되면 랜덤박스 1개)
        [SerializeField] float _randomBoxProgressPerDoll = 0.2f;

        // 인형 획득 시 인형 위치(3D)에 재생할 히트 버스트 이펙트 (Hit & Slashes 팩에서 독립시킨 사본)
        const string HitEffectPrefabName = "FX/hit-outer-1";

        // 인형 획득 시 울음소리에 뒤이어 재생할 효과음과 지연 시간
        const string AllyUpSoundName = "ally_up";
        const float AllyUpSoundDelay = 0.2f;

        // 황금 인형(GoldenDollBoxActor)을 뽑았을 때 합류하는 아군 수
        const int GoldenDollAllyCount = 3;

        // Actor 하나에 자식 콜라이더가 여러 개면 OnTriggerEnter가 여러 번 호출된다.
        // Destroy는 프레임 끝에 반영되므로, 같은 프레임 내 중복 호출을 막기 위해
        // 이미 처리한 Actor를 기록해 프리팹당 한 번만 처리한다.
        readonly HashSet<Actor> _processedActors = new HashSet<Actor>();

        private void Awake()
        {
            // OnTriggerEnter는 콜라이더가 트리거일 때만 호출된다.
            if (_collider != null)
            {
                _collider.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("ingame_doll"))
            {
                OnDollEnterBasket(other);
            }
        }

        private void OnDollEnterBasket(Collider dollCollider)
        {
            // 콜라이더 자신부터 상위 부모로 타고 올라가며 Actor 컴포넌트를 찾는다.
            Actor actor = dollCollider.GetComponentInParent<Actor>();

            // Actor를 찾았고 이미 처리한 프리팹이면 중복 호출이므로 무시한다.
            if (actor != null && !_processedActors.Add(actor))
            {
                return;
            }

            // 인형이 들어간 3D 위치에 획득 히트 버스트 이펙트를 재생한다. (월드 공간, 풀링)
            WorldFxPlayer.Instance.Play(HitEffectPrefabName, dollCollider.transform.position);

            // 획득 피드백 진동 (설정이 꺼져 있거나 모바일이 아니면 내부에서 무시)
            VibrationManager.Play();

            // 콤보: 이번 플레이 뽑기 성공 처리 (랜덤박스도 성공으로 콤보가 이어진다).
            // 아군 배수 계산보다 먼저 호출해서, 이 인형이 만든 콤보부터 배수가 적용되게 한다.
            UIComboHud comboHud = UIHud.CreateOrGet().ComboHud;
            if (comboHud != null)
                comboHud.OnDollCollected();

            if (actor is RandomBoxDollActor)
                OnRandomBoxCollected();
            else
                OnAllyDollCollected(actor);

            // Actor 루트를 통째로 파괴한다. (자식 콜라이더만 남지 않도록)
            Destroy(actor != null ? actor.gameObject : dollCollider.gameObject);
        }

        // 랜덤박스(doll_random_prefab) 획득: ally 합류 없이 전용 트레일 연출 후 보유 개수만 늘린다.
        private void OnRandomBoxCollected()
        {
            // randombox 미션 진행 카운트
            MissionSystem.Instance.OnRandomBoxCollected();

            UIHud hud = UIHud.CreateOrGet();
            hud.GetDollTrailHud.PlayGetRandomBoxTrailEffect(() => PlayerContext.AddRandomBox());
        }

        // 미션 동물 인형 획득: 울음소리 + ally 트레일 연출 후 ActorBattleSystem에 ally로 합류시킨다.
        private void OnAllyDollCollected(Actor actor)
        {
            // 뽑은 동물의 울음소리를 재생한다. (actor.json의 animalKey별 sound 필드 매핑)
            if (actor != null && actor.Context.Data != null)
                AudioManager.Instance.PlaySfx(GameActorData.GetSound(actor.Context.Data.animalKey));

            // 울음소리에 뒤이어 획득 효과음을 재생한다
            AudioManager.Instance.PlaySfxDelayed(AllyUpSoundName, AllyUpSoundDelay);

            // 황금 인형은 한 번에 아군 3마리가 합류하고, 콤보 보너스(3콤보 +1 / 4콤보 +2 / 5콤보 이상 +3)가 더해진다
            UIHud hud = UIHud.CreateOrGet();
            int comboBonus = hud.ComboHud != null ? hud.ComboHud.AllyBonusCount : 0;
            int allyCount = (actor is GoldenDollBoxActor ? GoldenDollAllyCount : 1) + comboBonus;

            // actor 미션 진행 카운트 - 뽑은 동물이 현재 미션 대상이면 합류하는 아군 수만큼 진행도를 올린다
            // (황금 인형/콤보 배수 포함 - 미션 카운트 = 실제 합류 아군 수)
            if (actor != null && actor.Context.Data != null)
                MissionSystem.Instance.OnDollCollected(actor.Context.Data.animalKey, allyCount);

            // 보스 액터가 없으면(올클리어 단계 등) 아군이 합류하지 않는다 - 트레일 연출 없이 사운드만 재생하고 끝낸다
            if (ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystemCheck) && !battleSystemCheck.HasBoss)
                return;

            // 트레일로 날릴 아이콘 프리팹 경로 (Actor가 없으면 null - PlayTrail이 로그 후 콜백만 보장)
            string iconPath = actor != null ? actor.Context.Data.GetIconPrefabFullPath() : null;

            // Actor는 호출 직후 파괴되므로, 트레일 도착(나중 시점) 콜백에서 쓸 ActorData(hp/attackPower)를
            // animalKey 기준으로 미리 조회해 캡처해둔다. (actor.Context.Data는 DollData - 정체성/경로 정보일 뿐 다른 타입)
            ActorData actorData = actor != null ? GameActorData.Get(actor.Context.Data.animalKey) : null;

            Transform trailTarget = hud.AllyStackActors != null ? hud.AllyStackActors.transform : null;

            hud.GetDollTrailHud.PlayGetDollTrail(iconPath, trailTarget, () =>
            {
                if (ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battleSystem))
                {
                    for (int i = 0; i < allyCount; i++)
                        battleSystem.AddAllyActor(actorData);
                }
            });
        }
    }
}