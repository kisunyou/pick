using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // ally 대기열(스택) UI. 슬롯이 꽉 차서 대기 중인 ally를 왼쪽부터(입력 순서대로) 아이콘으로 보여준다.
    // itemTemplate은 프리팹에 미리 배치된 항목 하나를 숨겨둔 뒤 복사해서 쓰는 템플릿이다.
    // ActorBattleSystem이 대기열에 넣을 때 AddItem, 대기열에서 꺼내 스폰할 때 RemoveOldestItem을 호출해
    // Queue<string>(_pendingQueue)와 항상 같은 순서로 짝을 맞춘다.
    public class UIAllyStackActors : MonoBehaviour
    {
        [SerializeField] private UIAllyStackActorItem itemTemplate;

        private readonly Queue<UIAllyStackActorItem> _items = new Queue<UIAllyStackActorItem>();

        private void Awake()
        {
            if (itemTemplate != null)
                itemTemplate.gameObject.SetActive(false);
        }

        // 대기열에 새로 들어온 ally를 오른쪽 끝에 아이콘으로 추가한다. HorizontalLayoutGroup이 sibling
        // 순서대로 왼쪽부터 배치하므로, 새 항목을 마지막 자식으로 넣으면 입력 순서대로 왼쪽부터 쌓인다.
        public void AddItem(string iconPath)
        {
            if (itemTemplate == null)
                return;

            UIAllyStackActorItem item = Instantiate(itemTemplate, transform);
            item.gameObject.SetActive(true);
            item.transform.SetAsLastSibling();
            item.Set(iconPath);

            _items.Enqueue(item);
        }

        // 대기열에서 가장 먼저 들어온(왼쪽) 항목을 하나 제거한다.
        public void RemoveOldestItem()
        {
            if (_items.Count == 0)
                return;

            UIAllyStackActorItem item = _items.Dequeue();
            if (item != null)
                Destroy(item.gameObject);
        }
    }
}
