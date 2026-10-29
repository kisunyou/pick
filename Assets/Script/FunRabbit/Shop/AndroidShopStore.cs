using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace FunRabbit
{
    // Google Play 인앱 결제 (Unity IAP 5.x StoreController 기반).
    // - 흐름: UGS 초기화 → StoreController.Connect → OnStoreConnected 에서 FetchProducts → 가격 수신(IsReady)
    //         → PurchaseProduct → OnPurchasePending(영수증 서명 검증 → 지급) → ConfirmPurchase(소비 확정) → OnPurchaseConfirmed
    //         (검증 실패 주문은 지급·확정하지 않는다 - Google 이 미확정 주문을 3일 뒤 자동 환불)
    // - 미확정(pending) 주문은 다음 실행의 Connect 시 다시 OnPurchasePending 으로 들어오므로 지급 누락이 없다.
    // - 에디터에서는 Unity IAP 의 FakeStore 가 대신 동작한다 (가격 "$0.01", 구매 확인 다이얼로그).
    // - 스토어 설정: Assets/Resources/BillingMode.json = GooglePlay, AndroidManifest 의 com.android.vending.BILLING
    public class AndroidShopStore : IShopStore
    {
        StoreController _store;
        IReadOnlyList<ShopProduct> _products;
        bool _productsFetched;
        readonly HashSet<string> _processingOrders = new HashSet<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
        // 이번 실행에서 영수증 검증 실패로 이미 실패 팝업을 띄운 주문 (재전달 시 팝업 반복 방지, 메모리 전용)
        readonly HashSet<string> _rejectedOrders = new HashSet<string>();
#endif
        CrossPlatformValidator _receiptValidator;   // 영수증 검증기 (첫 검증 시 생성, Android 실기기 전용)

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
            _store.ProcessPendingOrdersOnPurchasesFetched(true);
            _store.OnPurchasesFetched += _ => { };
            _store.OnPurchasesFetchFailed += failure => Debug.LogWarning("[AndroidShopStore] Purchase restore failed; will retry on reconnect.");

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
            _store.FetchPurchases();
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

            string transactionId = order?.Info?.TransactionID;
            var cart = order?.CartOrdered?.Items();
            // Do not acknowledge malformed or unsupported carts without delivery.
            if (string.IsNullOrWhiteSpace(transactionId) || cart == null || cart.Count != 1 || cart[0].Quantity <= 0)
            {
                Debug.LogError("[AndroidShopStore] Missing transaction ID or unsupported cart; confirmation deferred.");
                return;
            }

            Product storeProduct = GetFirstProduct(order);
            ShopProduct product = ShopCatalog.FindByStoreProductId(storeProduct?.definition?.id);
            if (product == null)
            {
                // 카탈로그에 없는 제품 - 지급 방법을 모르므로 확정하지 않고 남겨둔다 (다음 실행에 재전달됨)
                Debug.LogError($"[AndroidShopStore] 카탈로그에 없는 주문: {storeProduct?.definition?.id} - 확정 보류");
                return;
            }

            if (_processingOrders.Contains(transactionId)) return;

            // 영수증 로컬 서명 검증 - 코인 지급(ProcessPendingPurchase) 전에 한다.
            // 재시작 후 미확정 주문이 다시 들어오는 경로(ProcessPendingOrdersOnPurchasesFetched)도 이 함수를 거치므로 같은 검증을 받는다.
            // 에디터(FakeStore)·비 Android 에서는 Security 어셈블리가 스텁(NotImplementedException)이라 건너뛴다.
#if UNITY_ANDROID && !UNITY_EDITOR
            ReceiptCheck check = ValidateReceipt(order, product, transactionId, out string receiptError);
            if (check == ReceiptCheck.NotYetPurchased)
            {
                // 보류(승인 대기) 상태 영수증 - 결제 완료 후 다시 OnPurchasePending 으로 들어온다
                Debug.LogWarning($"[AndroidShopStore] 결제 완료 전 상태의 영수증 - 지급·확정 보류: {product.Key} ({receiptError})");
                return;
            }
            if (check == ReceiptCheck.Invalid)
            {
                // 지급하지 않고, 확정(ConfirmPurchase)도 하지 않는다 → 미확정 주문은 Google 이 3일 뒤 자동 환불한다.
                // 같은 세션에서 재전달(FetchPurchases 재호출 등)될 때마다 팝업이 반복되지 않게 1회만 알린다.
                Debug.LogError($"[AndroidShopStore] 영수증 검증 실패 - 지급·확정 안 함: {product.Key} / {receiptError}");
                if (_rejectedOrders.Add(transactionId))
                    OnPurchaseFailed?.Invoke(product, ShopPurchaseFailure.ReceiptInvalid, receiptError);
                return;
            }
#endif

            if (!_processingOrders.Add(transactionId)) return;
            ShopManager.Instance.ProcessPendingPurchase("GooglePlay:" + transactionId, product, cart[0].Quantity, success =>
            {
                _processingOrders.Remove(transactionId);
                if (!success) return;
                // Both wallet and transaction record have reached the cloud.
                // Replayed callbacks only re-confirm; they never add coins again.
                _store.ConfirmPurchase(order);
                OnPurchaseSucceeded?.Invoke(product);
            });
        }

        // ── 영수증 검증 ─────────────────────────────────────────────

        enum ReceiptCheck
        {
            Valid,
            Invalid,            // 서명/패키지/상품/토큰 불일치 - 지급·확정 안 함
            NotYetPurchased,    // 결제 완료 전(보류) 영수증 - 지급·확정 보류 (실패 아님)
        }

        // Google Play 영수증 로컬 서명 검증 (Unity IAP CrossPlatformValidator + GooglePlayTangle 공개키).
        // - Validate 가 검사: RSA 서명(Play Console 라이선스 키), 영수증 packageName == Application.identifier
        // - 여기서 추가 검사: 영수증 productId == 이 주문의 상품, purchaseToken == 원장 키(TransactionID),
        //   purchaseState == Purchased
        // - 공개키 파일: Assets/Scripts/UnityPurchasing/generated/GooglePlayTangle.cs
        //   (Unity 메뉴 Services > In-App Purchasing > Receipt Validation Obfuscator 로 같은 위치에 재생성 가능)
        // - 로컬 검증은 위조 영수증 주입은 막지만 기기 루팅 후 코드 변조까지 막지는 못한다 (완전한 방어는 서버 검증)
        // 호출은 Android 실기기에서만 한다 (에디터/기타 플랫폼의 Security 어셈블리는 스텁).
        ReceiptCheck ValidateReceipt(PendingOrder order, ShopProduct product, string transactionId, out string error)
        {
            error = null;
            try
            {
                if (!GooglePlayTangle.IsPopulated)
                {
                    error = "GooglePlayTangle not populated";
                    return ReceiptCheck.Invalid;
                }

                if (_receiptValidator == null)
                    _receiptValidator = new CrossPlatformValidator(GooglePlayTangle.Data(), Application.identifier);

                IPurchaseReceipt[] receipts = _receiptValidator.Validate(order.Info.Receipt);
                string expectedProductId = product.GetStoreProductId();

                GooglePlayReceipt matched = null;
                if (receipts != null)
                {
                    foreach (IPurchaseReceipt receipt in receipts)
                    {
                        if (receipt is GooglePlayReceipt google && google.productID == expectedProductId)
                        {
                            matched = google;
                            break;
                        }
                    }
                }

                if (matched == null)
                {
                    error = $"product mismatch (expected {expectedProductId})";
                    return ReceiptCheck.Invalid;
                }

                // 원장(CoinWallet) 중복 지급 방지 키가 서명된 영수증의 토큰과 같아야 한다 (다른 토큰으로 재사용 방지)
                if (matched.purchaseToken != transactionId)
                {
                    error = "purchase token mismatch";
                    return ReceiptCheck.Invalid;
                }

                if (matched.purchaseState == GooglePurchaseState.Deferred)
                {
                    error = "purchase state Deferred";
                    return ReceiptCheck.NotYetPurchased;
                }

                if (matched.purchaseState != GooglePurchaseState.Purchased)
                {
                    error = $"purchase state {matched.purchaseState}";
                    return ReceiptCheck.Invalid;
                }

                return ReceiptCheck.Valid;
            }
            catch (IAPSecurityException e)
            {
                // InvalidSignatureException / InvalidBundleIdException / InvalidReceiptDataException 등
                error = $"{e.GetType().Name}: {e.Message}";
                return ReceiptCheck.Invalid;
            }
            catch (Exception e)
            {
                // 검증 자체가 불가능한 경우도 지급하지 않는다 (fail-closed). 주문은 미확정으로 남아 다음 실행에 재검증된다.
                error = $"validation error {e.GetType().Name}: {e.Message}";
                return ReceiptCheck.Invalid;
            }
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
