using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 상점 패널. UIBottomBar 의 shopButton 으로 열린다 (Contents 레이어 - 열리면 하단 mainMenu 가 숨겨진다).
    // 뷰(이 클래스)는 참조/표시만 담당하고, 로직은 UIShopControl 이 담당한다 (UIRandomboxPanel 과 같은 구조).
    [UIOption(
        Path = "UI2/Prefabs/UIShopPanel",
        Layer = UILayer.Contents,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIShopPanel : BaseUIView<UIShopPanel>
    {
        [SerializeField] Button closeButton;
        [SerializeField] UIShopItem[] shopItems;   // 프리팹의 UIShopItem 2개 (10,000 / 50,000 코인)

        public UIShopControl Control { get; private set; } = new UIShopControl();

        public UIShopItem[] ShopItems => shopItems;

        // 상점을 연다 (이미 열려 있으면 그대로). Contents 레이어의 다른 패널(도감/랜덤박스 등)은 닫는다.
        // 하단바 상점 버튼과 코인 부족 팝업의 "구매하러 가기" 가 같은 진입점을 쓴다.
        public static UIShopPanel OpenExclusive()
        {
            UIShopPanel opened = Get();
            if (opened != null)
                return opened;

            UIManager.Instance.CloseAllInLayer(UILayer.Contents);
            return CreateOrGet();
        }

        void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            Control.Initialize(this);
        }

        protected override void OnDestroy()
        {
            Control.Deinitialize();
            base.OnDestroy();
        }
    }

    // UIShopPanel 의 로직(상품 텍스트 다국어 표시 / 현지 가격 표시 / 클릭 → ShopManager 결제 요청)을 담당하는 컨트롤
    public class UIShopControl
    {
        // stringData.json 키
        const string ItemCoinFormatKey = "shop_item_coin_format";        // "{0}\n코인"  ({0} = 콤마 포함 코인 수)

        private UIShopPanel _panel;

        public void Initialize(UIShopPanel panel)
        {
            _panel = panel;

            UIShopItem[] items = _panel.ShopItems;
            if (items != null)
            {
                foreach (UIShopItem item in items)
                {
                    if (item == null)
                        continue;

                    UIShopItem captured = item;
                    captured.SetOnClick(() => OnClickItem(captured));
                }
            }

            RefreshItemTexts();
            LanguageManager.Instance.OnLanguageChanged += RefreshItemTexts;
            // 스토어 가격 수신/갱신 시 Text_Cost 를 다시 그린다 (패널을 스토어 연결 전에 열었을 때 N/A → 가격)
            ShopManager.Instance.OnProductsUpdated += RefreshItemTexts;
        }

        public void Deinitialize()
        {
            if (LanguageManager.IsCheckInstance())
                LanguageManager.Instance.OnLanguageChanged -= RefreshItemTexts;
            if (ShopManager.IsCheckInstance())
                ShopManager.Instance.OnProductsUpdated -= RefreshItemTexts;

            _panel = null;
        }

        // 각 상품의 Text_Gold 를 현재 언어로, Text_Cost 를 스토어 현지 통화 가격(없으면 "N/A")으로 채운다.
        // 예) 10000 → "10,000\n코인" / "₩1,200"
        public void RefreshItemTexts()
        {
            if (_panel == null || _panel.ShopItems == null)
                return;

            foreach (UIShopItem item in _panel.ShopItems)
            {
                if (item == null)
                    continue;

                item.SetGoldText(LanguageManager.Instance.Get(ItemCoinFormatKey, FormatCoin(item.CoinAmount)));
                item.SetCostText(ShopManager.Instance.GetPriceText(item.ProductId));
            }
        }

        // 세 자릿수마다 콤마 (UIBottomBar 코인 표시와 같은 형식)
        private static string FormatCoin(long amount)
        {
            return amount.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // 상품 클릭: 확인 팝업 없이 곧바로 ShopManager 에 결제 요청 (스토어 결제창이 확인 역할).
        // 성공(코인 지급 + 안내 팝업)/실패(사유 팝업)는 ShopManager 가 처리한다.
        private void OnClickItem(UIShopItem item)
        {
            if (_panel == null || item == null)
                return;

            Debug.Log($"[UIShopControl] 구매 요청: {item.ProductId} ({item.CoinAmount} 코인)");
            ShopManager.Instance.Purchase(item.ProductId);
        }
    }
}
