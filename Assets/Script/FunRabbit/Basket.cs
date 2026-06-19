using UnityEngine;

namespace FunRabbit
{
    public class Basket : MonoBehaviour
    {
        [SerializeField] BoxCollider _collider;

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
            // TODO
            GameQuestManager.Instance.CheckMissionAdd(dollCollider.gameObject.name);
            Destroy(dollCollider.gameObject);

        }
    }
}