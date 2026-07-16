using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class Basket : MonoBehaviour
    {
        [SerializeField] BoxCollider _collider;
        // 미션과 무관한 인형 1개가 채우는 랜덤박스 진행 게이지 양 (1이 되면 랜덤박스 1개)
        [SerializeField] float _randomBoxProgressPerDoll = 0.2f;

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
            if (other.gameObject.layer == LayerMask.NameToLayer("doll"))
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

            // 찾으면 Actor가 붙은 오브젝트 이름을, 없으면 콜라이더 오브젝트 이름을 사용한다.
            string dollName = actor != null ? actor.gameObject.name : dollCollider.gameObject.name;

            //Vector3 startPos = GameCommon.Convert3dTo2dCoord(dollCollider.transform.position);
            string iconPath = actor.Data.GetIconPrefabFullPath();

            // 카운트는 즉시 올리지 않고, 트레일이 도착하는 시점(onArrive)에 증가시킨다.
            if (GameQuestManager.Instance.IsMissionTarget(dollName))
            {
                // 미션 대상: 미션 아이콘 자리로 날아가 도착하면 미션 카운트 증가
                UIHud.CreateOrGet().GetDollTrailHud.PlayGetDollTrail(iconPath,
                    () => GameQuestManager.Instance.AddMission());
            }
            else
            {
                // 미션 무관: 랜덤박스 자리로 날아가 도착하면 진행 게이지 누적 (1이 차면 박스 +1)
                UIHud.CreateOrGet().GetDollTrailHud.PlayGetRandomBoxTrail(iconPath,
                    () => PlayerContext.AddRandomBoxProgressValue(_randomBoxProgressPerDoll));
            }

            // Actor 루트를 통째로 파괴한다. (자식 콜라이더만 남지 않도록)
            Destroy(actor != null ? actor.gameObject : dollCollider.gameObject);
        }
    }
}