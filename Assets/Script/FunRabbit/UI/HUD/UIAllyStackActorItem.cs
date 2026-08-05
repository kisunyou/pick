using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // ally 대기열(스택) UI에 표시되는 아이콘 아이템 하나. Set(iconPath)으로 아이콘을 채운다.
    public class UIAllyStackActorItem : MonoBehaviour
    {
        [SerializeField] private Image iconImage;

        private const float SIZE_DIVIDER = 4f; // 원본 이미지 크기의 1/4로 표시

        public void Set(string iconPath)
        {
            Sprite sprite = SpriteCache.Get(iconPath);
            if (sprite == null)
                return;

            if (iconImage != null)
                iconImage.sprite = sprite;

            RectTransform rectTransform = (RectTransform)transform;
            rectTransform.sizeDelta = new Vector2(sprite.rect.width / SIZE_DIVIDER, sprite.rect.height / SIZE_DIVIDER);
        }
    }
}
