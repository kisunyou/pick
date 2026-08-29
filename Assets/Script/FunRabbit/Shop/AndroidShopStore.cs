using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace FunRabbit
{
    // Google Play 인앱 결제 (Unity IAP 5.x StoreController 기반).
    // - 흐름: UGS 초기화 → StoreController.Connect → OnStoreConnected 에서 FetchProducts → 가격 수신(IsReady)
    //         → PurchaseProduct → OnPurchasePending(지급) → ConfirmPurchase(소비 확정) → OnPurchaseConfirmed
    // - 미확정(pending) 주문은 다음 실행의 Connect 시 다시 OnPurchasePending 으로 들어오므로 지급 누락이 없다.
    // - 에디터에서는 Unity IAP 의 FakeStore 가 대신 동작한다 (가격 "$0.01", 구매 확인 다이얼로그).
    // - 스토어 설정: Assets/Resources/BillingMode.json = GooglePlay, AndroidManifest 의 com.android.vending.BILLING
    public class AndroidShopStore : IShopStore
    {
        StoreController _store;
        IReadOnlyList<ShopProduct> _products;
        bool _productsFetched;

        // 결제 진행 중 판정: 마지막 요청 시각 기준 PurchaseInFlightTimeout 이내면 중복 요청을 막는다.
        // 하드 플래그가 아닌 이유 - 스토어 콜백(취소 등)이 유실되면 플래그가 영구히 남아 상점이 잠기기 때문
        // (에디터 FakeStore 의 Cancel 이 OnPurchaseFailed 로 전달되지 않는 사례 확인, 2026-08-29).
        // 실제 스토어도 중복 결제는 ExistingPurchasePending 으로 스스로 거절하므로 이 판정은 UX 용 보조 장치다.
        const float PurchaseInFlightTimeout = 10f;
        float _purchaseStartedAt = -1f;     // Time.realtimeSinceStartup, -1 = 진행 중 아님

        bool IsPurchaseInFlight => _purchaseStartedAt >= 0f && Time.realtimeSinceStartup - _purchaseStartedAt < PurchaseInFlightTimeout;
        void ClearInFlight() => _purchaseStartedAt = -1f;

        public bool IsReady => _store != null && _productsFetched;

        public event Action OnProductsUpdated;
        public event Action<ShopProduct> OnPurchaseSucceeded;
        public event Action<ShopProduct, ShopPurchaseFailure, string> OnPurchaseFailed;

        public void Initialize(IReadOnlyList<ShopProduct> products)
        {
            _products = products;
            InitializeAsync();
        }

        async void InitializeAsync()
        {
            // Unity IAP 는 Unity Gaming Services 위에서 동작한다 - 미초기화 상태면 먼저 초기화 (실패해도 Connect 는 시도)
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    await UnityServices.InitializeAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AndroidShopStore] Unity Gaming Services 초기화 실패: {e.Message}");
            }

            _store = UnityIAPServices.StoreController();
            _store.OnStoreConnected += OnStoreConnected;
            _store.OnStoreDisconnected += OnStoreDisconnected;
            _store.OnProductsFetched += OnProductsFetched;
            _store.OnProductsFetchFailed += OnProductsFetchFailed;
            _store.OnPurchasePending += OnPurchasePending;
            _store.OnPurchaseConfirmed += OnPurchaseConfirmed;
            _store.OnPurchaseFailed += OnStorePurchaseFailed;
            _store.OnPurchaseDeferred += OnPurchaseDeferred;

            try
            {
                Debug.Log("[AndroidShopStore] 스토어 연결 시도");
                await _store.Connect();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidShopStore] 스토어 연결 예외: {e.Message}");
            }
        }

        // ── 연결 / 상품 ──────────────────────────────────────────────

        void OnStoreConnected()
        {
            Debug.Log("[AndroidShopStore] 스토어 연결됨 - 상품 정보 요청");

            var definitions = new List<ProductDefinition>(_products.Count);
            foreach (ShopProduct product in _products)
                definitions.Add(new ProductDefinition(product.GetStoreProductId(), ProductType.Consumable));

            _store.FetchProducts(definitions);
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription description)
        {
            Debug.LogWarning($"[AndroidShopStore] 스토어 연결 끊김: {description.message} (retryable={description.isRetryable})");
            _productsFetched = false;
            OnProductsUpdated?.Invoke();   // 가격 → N/A
        }

        void OnProductsFetched(List<Product> products)
        {
            _productsFetched = true;
            foreach (Product product in products)
                Debug.Log($"[AndroidShopStore] 상품 수신: {product.definition?.id} = {product.metadata?.localizedPriceString} ({product.metadata?.isoCurrencyCode}), available={product.availableToPurchase}");

            OnProductsUpdated?.Invoke();
        }

        void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            Debug.LogWarning($"[AndroidShopStore] 상품 정보 수신 실패 ({failure.FailedFetchProducts.Count}개): {failure.FailureReason}");
            OnProductsUpdated?.Invoke();   // 가격 → N/A 유지
        }

        public string GetLocalizedPrice(ShopProduct product)
        {
            if (!IsReady || product == null)
                return null;

            Product storeProduct = _store.GetProductById(product.GetStoreProductId());
            if (storeProduct == null || !storeProduct.availableToPurchase)
                return null;

            string price = storeProduct.metadata?.localizedPriceString;
            return string.IsNullOrEmpty(price) ? null : price;
        }

        // ── 구매 ────────────────────────────────────────────────────

        public void Purchase(ShopProduct product)
        {
            if (product == null)
            {
                OnPurchaseFailed?.Invoke(null, ShopPurchaseFailure.ProductUnavailable, "product null");
                return;
            }

            if (!IsReady)
            {
                OnPurchaseFailed?.Invoke(product, ShopPurchaseFailure.StoreNotReady, "store not ready");
                return;
            }

            if (IsPurchaseInFlight)
            {
                OnPurchaseFailed?.Invoke(product, ShopPurchaseFailure.InProgress, "another purchase in progress");
                return;
            }

            Product storeProduct = _store.GetProductById(product.GetStoreProductId());
            if (storeProduct == null || !storeProduct.availableToPurchase)
            {
                OnPurchaseFailed?.Invoke(product, ShopPurchaseFailure.ProductUnavailable, "product unavailable");
                return;
            }

            _purchaseStartedAt = Time.realtimeSinceStartup;
            Debug.Log($"[AndroidShopStore] 구매 요청: {storeProduct.definition?.id}");
            _store.PurchaseProduct(storeProduct);
        }

        // 결제 승인됨(미확정) - 여기서 지급하고 스토어에 소비 확정을 보낸다.
        // 앱 재시작 후 미확정 주문이 다시 들어오는 경로도 동일 (지급 누락 방지)
        void OnPurchasePending(PendingOrder order)
        {
            ClearInFlight();

            Product storeProduct = GetFirstProduct(order);
            ShopProduct product = ShopCatalog.FindByStoreProductId(storeProduct?.definition?.id);
            if (product == null)
            {
                // 카탈로그에 없는 제품 - 지급 방법을 모르므로 확정하지 않고 남겨둔다 (다음 실행에 재전달됨)
                Debug.LogError($"[AndroidShopStore] 카탈로그에 없는 주문: {storeProduct?.definition?.id} - 확정 보류");
                return;
            }

            Debug.Log($"[AndroidShopStore] 결제 승인: {product.Key} → 지급 후 확정");
            OnPurchaseSucceeded?.Invoke(product);
            _store.ConfirmPurchase(order);
        }

        void OnPurchaseConfirmed(Order order)
        {
            ClearInFlight();
            Product storeProduct = GetFirstProduct(order);
            if (order is FailedOrder failed)
                Debug.LogError($"[AndroidShopStore] 확정 실패: {storeProduct?.definition?.id} / {failed.FailureReason} / {failed.Details}");
            else
                Debug.Log($"[AndroidShopStore] 확정 완료: {storeProduct?.definition?.id}");
        }

        // 결제 보류 (예: 보호자 승인 대기) - 승인되면 나중에 OnPurchasePending 으로 들어온다
        void OnPurchaseDeferred(DeferredOrder order)
        {
            ClearInFlight();
            Product storeProduct = GetFirstProduct(order);
            Debug.Log($"[AndroidShopStore] 결제 보류(승인 대기): {storeProduct?.definition?.id}");
        }

        void OnStorePurchaseFailed(FailedOrder order)
        {
            ClearInFlight();

            Product storeProduct = GetFirstProduct(order);
            ShopProduct product = ShopCatalog.FindByStoreProductId(storeProduct?.definition?.id);
            Debug.LogWarning($"[AndroidShopStore] 결제 실패: {storeProduct?.definition?.id} / {order.FailureReason} / {order.Details}");

            OnPurchaseFailed?.Invoke(product, ToFailure(order.FailureReason), order.Details);
        }

        static ShopPurchaseFailure ToFailure(PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case PurchaseFailureReason.UserCancelled:
                case PurchaseFailureReason.OrderCancelled:
                    return ShopPurchaseFailure.UserCancelled;
                case PurchaseFailureReason.PaymentDeclined:
                    return ShopPurchaseFailure.PaymentDeclined;
                case PurchaseFailureReason.ProductUnavailable:
                case PurchaseFailureReason.PurchasingUnavailable:
                    return ShopPurchaseFailure.ProductUnavailable;
                case PurchaseFailureReason.StoreNotConnected:
                    return ShopPurchaseFailure.StoreNotReady;
                case PurchaseFailureReason.ExistingPurchasePending:
                    return ShopPurchaseFailure.InProgress;
                case PurchaseFailureReason.NotSupported:
                    return ShopPurchaseFailure.NotSupported;
                default:
                    return ShopPurchaseFailure.Unknown;
            }
        }

        static Product GetFirstProduct(Order order)
        {
            IReadOnlyList<CartItem> items = order?.CartOrdered?.Items();
            return items != null && items.Count > 0 ? items[0].Product : null;
        }
    }
}
