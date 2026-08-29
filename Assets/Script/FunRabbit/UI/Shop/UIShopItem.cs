using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FunRabbit
{
    // 상점 패널의 상품 1칸 (UIShopPanel 프리팹의 UIShopItem 오브젝트에 부착).
    // 표시 텍스트는 UIShopControl 이 다국어 문자열로 채우고, 클릭은 UIShopControl 에 위임한다.
    public class UIShopItem : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] TextMeshProUGUI goldText;   // Text_Gold - "10,000\n코인" 형태로 표시
        [SerializeField] TextMeshProUGUI costText;   // Text_Cost - 현지 통화 가격 (못 받아 오면 "N/A")
        [SerializeField] int coinAmount;             // 이 상품이 지급하는 코인 수 (프리팹에서 10000 / 50000)
        [SerializeField] string productId;           // ShopCatalog 키 = 스토어 제품 ID (takepick_coin_10000 / takepick_coin_50000)

        public int CoinAmount => coinAmount;
        public string ProductId => productId;

        // 현재 화면에 표시 중인 Text_Gold 문자열 (구매 확인 팝업 본문에 그대로 사용)
        public string GoldText => goldText != null ? goldText.text : string.Empty;

        public void SetGoldText(string text)
        {
            if (goldText != null)
                goldText.text = text;
        }

        public void SetCostText(string text)
        {
            if (costText != null)
                costText.text = text;
        }

        public void SetOnClick(UnityAction onClick)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener(onClick);
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null)
                button.interactable = interactable;
        }
    }
}
