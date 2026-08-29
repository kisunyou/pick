using System;
using UnityEngine;

namespace FunRabbit
{
    // 인앱 결제 매니저. 빌드 플랫폼에 맞는 스토어(IShopStore)를 골라 초기화하고,
    // 결제 결과를 코인 지급 + 결과 팝업으로 처리한다. UI 는 GetPriceText / Purchase / OnProductsUpdated 만 쓴다.
    // GameMain.Start()에서 MakeInstance()로 깨워진다 (LevelPlayAds 와 동일 패턴).
    public class ShopManager : Singleton<ShopManager>
    {
        // 가격을 못 받아 왔을 때 표시 - stringData 키 (현재 전 언어 "N/A", 언어별로 바꾸려면 테이블만 수정)
        const string PriceUnavailableKey = "shop_price_unavailable";
        public static string PriceUnavailableText => LanguageManager.Instance.Get(PriceUnavailableKey);

        const string SuccessTitleKey = "shop_popup_purchase_success_title";
        const string SuccessBodyKey = "shop_popup_purchase_success_body";      // "코인 {0}개가 지급되었습니다."
        const string FailTitleKey = "shop_popup_purchase_fail_title";
        const string FailBodyKey = "shop_popup_purchase_fail_body";            // "구매를 완료할 수 없습니다.\n({0})"

        IShopStore _store;

        // 상품 가격 정보가 갱신됨 (UIShopControl 이 구독해 Text_Cost 를 다시 그린다)
        public event Action OnProductsUpdated;

        public bool IsStoreReady => _store != null && _store.IsReady;

        protected override void Awake()
        {
            base.Awake();
            CreateStore();
        }

        void CreateStore()
        {
#if UNITY_IOS
            _store = new IosShopStore();
#else
            _store = new AndroidShopStore();   // Android 실기기 = Google Play, 에디터 = Unity IAP FakeStore
#endif
            _store.OnProductsUpdated += HandleProductsUpdated;
            _store.OnPurchaseSucceeded += HandlePurchaseSucceeded;
            _store.OnPurchaseFailed += HandlePurchaseFailed;
            _store.Initialize(ShopCatalog.Products);
        }

        protected override void OnDestroy()
        {
            if (_store != null)
            {
                _store.OnProductsUpdated -= HandleProductsUpdated;
                _store.OnPurchaseSucceeded -= HandlePurchaseSucceeded;
                _store.OnPurchaseFailed -= HandlePurchaseFailed;
            }
            base.OnDestroy();
        }

        // ── UI 진입점 ───────────────────────────────────────────────

        // 현지 통화 가격 문자열. 스토어 미연결/상품 미수신이면 shop_price_unavailable ("N/A")
        public string GetPriceText(string productKey)
        {
            ShopProduct product = ShopCatalog.Find(productKey);
            string price = product != null && _store != null ? _store.GetLocalizedPrice(product) : null;
            return string.IsNullOrEmpty(price) ? PriceUnavailableText : price;
        }

        // 구매 요청 - 결과는 팝업(성공: 코인 지급 안내 / 실패: 사유)으로 표시된다
        public void Purchase(string productKey)
        {
            ShopProduct product = ShopCatalog.Find(productKey);
            if (product == null)
            {
                Debug.LogError($"[ShopManager] 카탈로그에 없는 상품: {productKey}");
                HandlePurchaseFailed(null, ShopPurchaseFailure.ProductUnavailable, "unknown product key");
                return;
            }

            FireBaseAnalyticsManager.Instance.LogEvent("purchase_try", "product_id", product.Key);
            _store.Purchase(product);
        }

        // ── 스토어 이벤트 ───────────────────────────────────────────

        void HandleProductsUpdated()
        {
            OnProductsUpdated?.Invoke();
        }

        // 결제 성공: 코인 즉시 지급(연출 없이 - 지급 누락 방지) → 성공 팝업(코인 아이콘)
        void HandlePurchaseSucceeded(ShopProduct product)
        {
            PlayerContext.AddCoinAmount(product.CoinAmount);
            FireBaseAnalyticsManager.Instance.LogEvent("purchase_complete", "product_id", product.Key);
            Debug.Log($"[ShopManager] 구매 성공: {product.Key} → 코인 {product.CoinAmount} 지급");

            ShowPopup(
                LanguageManager.Instance.Get(SuccessTitleKey),
                LanguageManager.Instance.Get(SuccessBodyKey, FormatCoin(product.CoinAmount)),
                showCoinIcon: true);
        }

        // 결제 실패: 사용자가 직접 취소한 경우는 조용히 종료, 그 외는 사유와 함께 실패 팝업
        void HandlePurchaseFailed(ShopProduct product, ShopPurchaseFailure reason, string details)
        {
            FireBaseAnalyticsManager.Instance.LogEvent("purchase_fail", "reason", reason.ToString());
            Debug.LogWarning($"[ShopManager] 구매 실패: {product?.Key} / {reason} / {details}");

            if (reason == ShopPurchaseFailure.UserCancelled)
                return;

            ShowPopup(
                LanguageManager.Instance.Get(FailTitleKey),
                LanguageManager.Instance.Get(FailBodyKey, LanguageManager.Instance.Get(GetFailureReasonKey(reason))));
        }

        static string GetFailureReasonKey(ShopPurchaseFailure reason)
        {
            switch (reason)
            {
                case ShopPurchaseFailure.StoreNotReady: return "shop_fail_store_not_ready";
                case ShopPurchaseFailure.ProductUnavailable: return "shop_fail_product_unavailable";
                case ShopPurchaseFailure.InProgress: return "shop_fail_in_progress";
                case ShopPurchaseFailure.PaymentDeclined: return "shop_fail_payment_declined";
                case ShopPurchaseFailure.NotSupported: return "shop_fail_not_supported";
                default: return "shop_fail_unknown";
            }
        }

        static void ShowPopup(string title, string body, bool showCoinIcon = false)
        {
            if (!UIManager.IsCheckInstance())
                return;

            UIPopup popup = UIPopup.CreateOrGet();
            if (popup != null)
                popup.Set(title, body, null, showCoinIcon);
        }

        // 세 자릿수마다 콤마 (UIBottomBar 코인 표시와 같은 형식)
        static string FormatCoin(long amount)
        {
            return amount.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
