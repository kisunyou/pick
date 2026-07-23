using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FunRabbit
{
    // 하단 메뉴(상점/인벤토리/카드/클랜 등) 버튼 1개를 담당하는 재사용 컴포넌트.
    public class UIMenuButton : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI notiCountText;
        [SerializeField] GameObject notiObject;

        public void SetButton(bool interactable, UnityAction onClick)
        {
            if (button == null)
                return;

            button.interactable = interactable;

            button.onClick.RemoveListener(onClick);
            if (onClick != null)
                button.onClick.AddListener(onClick);
        }

        public void SetTitleText(string title)
        {
            if (titleText != null)
                titleText.text = title;
        }

        public void SetNotiCountText(int count)
        {
            if (notiCountText != null)
                notiCountText.text = count.ToString();
        }

        public void SetActiveNotiObject(bool isActive)
        {
            if (notiObject != null)
                notiObject.SetActive(isActive);
        }
    }
}
