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
        const string NeedGoogleTitleKey = "popup_need_google_title";           // "구글 로그인 전환"
        const string NeedGoogleBodyKey = "popup_need_google_body";             // "인앱 상품을 구매 하려면\n구글 로그인 전환 필요합니다\n하시겠습니까?"
        const string GoogleLinkingTitleKey = "popup_google_linking_title";     // "계정 연동"
        const string GoogleLinkingBodyKey = "popup_google_linking_body";       // "구글 계정으로 연동 중입니다."
        const string GoogleLinkFailKey = "message_google_link_fail";           // "구글 계정 연동에 실패했습니다."
        const string PurchaseConfirmTitleKey = "popup_purchase_confirm_title"; // "구매 확인"
        const string PurchaseConfirmBodyKey = "popup_purchase_confirm_body";   // "구매 하시겠습니까?"

        // 연동 완료 콜백이 어떤 이유로든 안 오는 경우의 진행 팝업 안전장치 (초)
        const float GoogleLinkingTimeoutSeconds = 60f;

        IShopStore _store;
        readonly System.Collections.Generic.HashSet<string> _pendingTransactions = new System.Collections.Generic.HashSet<string>();
        float _purchaseRequestedAt = -1;
        string _purchaseOwner;
        bool _delivering;
        public bool IsAccountChangeBlocked => _delivering ||
            (_purchaseRequestedAt >= 0 && Time.realtimeSinceStartup - _purchaseRequestedAt < 120f);
        public bool HasPendingPurchase => _pendingTransactions.Count > 0 ||
            (_purchaseRequestedAt >= 0 && Time.realtimeSinceStartup - _purchaseRequestedAt < 120f);

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
            if (HasPendingPurchase || SessionOperation.IsBusy)
                return;
            // 게스트 로그인 상태에서는 인앱 구매 전에 구글 계정 전환을 요구한다
            // (구매 내역이 앱 삭제 시 소멸하는 게스트 계정에 묶이지 않도록)
            if (FireBaseAuthManager.IsCheckInstance() && FireBaseAuthManager.Instance.IsAnonymousUser)
            {
                ShowGoogleLoginRequiredPopup(productKey);
                return;
            }

            if (!FireBaseAuthManager.IsCheckInstance() || !FireBaseAuthManager.Instance.IsLoggedIn ||
                !CloudSaveManager.Instance.CanPurchase)
            {
                ShowPopup(LanguageManager.Instance.Get(FailTitleKey), LanguageManager.Instance.Get("purchase_sync_required"));
                return;
            }

            ShopProduct product = ShopCatalog.Find(productKey);
            if (product == null)
            {
                Debug.LogError($"[ShopManager] 카탈로그에 없는 상품: {productKey}");
                HandlePurchaseFailed(null, ShopPurchaseFailure.ProductUnavailable, "unknown product key");
                return;
            }

            string requestedOwner = PlayerPrefs.GetString("PurchaseRequestedOwner_" + product.Key, "");
            if (!string.IsNullOrEmpty(requestedOwner) && requestedOwner != FireBaseAuthManager.Instance.UserId)
            {
                ShowPopup(LanguageManager.Instance.Get(FailTitleKey), LanguageManager.Instance.Get("purchase_other_account"));
                return;
            }
            FireBaseAnalyticsManager.Instance.LogEvent("purchase_try", "product_id", product.Key);
            _purchaseRequestedAt = Time.realtimeSinceStartup;
            _purchaseOwner = FireBaseAuthManager.Instance.UserId;
            PlayerPrefs.SetString("PurchaseRequestedOwner_" + product.Key, _purchaseOwner);
            PlayerPrefs.Save();
            _store.Purchase(product);
        }

        public void ProcessPendingPurchase(string transactionId, ShopProduct product, int quantity, Action<bool> complete)
        {
            if (!_pendingTransactions.Add(transactionId)) return;
            StartCoroutine(DeliverPendingPurchase(transactionId, product, quantity, complete));
        }

        System.Collections.IEnumerator DeliverPendingPurchase(string transactionId, ShopProduct product, int quantity, Action<bool> complete)
        {
            // Pending orders can arrive before login and boot restore. Never grant
            // them into the temporary local player being replaced by that restore.
            while (!CloudSaveManager.Instance.CanPurchase || !FireBaseAuthManager.Instance.IsLoggedIn
#if !UNITY_EDITOR
                || FireBaseAuthManager.Instance.IsAnonymousUser
#endif
                )
                yield return new WaitForSecondsRealtime(1f);

            string uid = FireBaseAuthManager.Instance.UserId;
            string ownerKey = "PurchaseOwner_" + transactionId;
            string owner = PlayerPrefs.GetString(ownerKey,
                PlayerPrefs.GetString("PurchaseRequestedOwner_" + product.Key, uid));
            if (owner != uid)
            {
                _pendingTransactions.Remove(transactionId);
                _purchaseRequestedAt = -1;
                ShowPopup(LanguageManager.Instance.Get(FailTitleKey), LanguageManager.Instance.Get("purchase_other_account"));
                complete?.Invoke(false);
                yield break;
            }
            PlayerPrefs.SetString(ownerKey, uid);
            // Transfer the request's ownership to its stable transaction key.
            // A completed purchase must not bind future purchases of this product.
            string requestOwnerKey = "PurchaseRequestedOwner_" + product.Key;
            if (PlayerPrefs.GetString(requestOwnerKey, "") == uid)
                PlayerPrefs.DeleteKey(requestOwnerKey);
            PlayerPrefs.Save();

            using (SessionOperation.Begin())
            {
                _delivering = true;
                bool granted = false;
                try { granted = PlayerContext.GrantPurchaseOnce(transactionId, checked(product.CoinAmount * quantity)); }
                catch (Exception e)
                {
                    Debug.LogError($"[ShopManager] Cannot persist purchase: {e.Message}");
                    _pendingTransactions.Remove(transactionId);
                    _purchaseRequestedAt = -1;
                    _delivering = false;
                    complete?.Invoke(false);
                    yield break;
                }
                bool saved = false;
                while (!saved)
                {
                    if (FireBaseAuthManager.Instance.UserId != uid)
                    {
                        _pendingTransactions.Remove(transactionId);
                        _purchaseRequestedAt = -1;
                        _delivering = false;
                        complete?.Invoke(false);
                        yield break;
                    }
                    bool done = false;
                    CloudSaveManager.Instance.SaveNow(ok => { saved = ok; done = true; });
                    while (!done) yield return null;
                    if (saved) break;
                    bool retry = false;
                    bool conflict = CloudSaveManager.Instance.HasSaveConflict;
                    SessionOperation.ShowRetry(() => retry = true,
                        conflict ? "purchase_save_conflict" : "account_sync_retry_body",
                        conflict ? "purchase_resync" : "account_sync_retry");
                    while (!retry) yield return null;
                    if (CloudSaveManager.Instance.HasSaveConflict)
                    {
                        bool restored = false;
                        bool restoreDone = false;
                        CloudSaveManager.Instance.RecoverPurchaseConflict(ok => { restored = ok; restoreDone = true; });
                        while (!restoreDone) yield return null;
                        if (restored && FireBaseAuthManager.Instance.UserId == uid)
                        {
                            // The cloud wallet may already include this order. Regrant only if its ledger does not.
                            try
                            {
                                bool reapplied = PlayerContext.GrantPurchaseOnce(transactionId, checked(product.CoinAmount * quantity));
                                granted |= reapplied;
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("[ShopManager] Purchase recovery failed: " + e.Message);
                                _pendingTransactions.Remove(transactionId);
                                _purchaseRequestedAt = -1;
                                _delivering = false;
                                complete?.Invoke(false);
                                yield break;
                            }
                        }
                    }
                }
                SessionOperation.HideRetry();
                _pendingTransactions.Remove(transactionId);
                _purchaseRequestedAt = -1;
                _delivering = false;
                if (granted) FireBaseAnalyticsManager.Instance.LogEvent("purchase_complete", "product_id", product.Key);
            }
            complete?.Invoke(true);
        }

        // 게스트 상태 구매 시도 - 구글 로그인 전환 안내 팝업 (확인: 전환 진행 / 취소: 닫기).
        // 확인 시: "연동 중" 모달 팝업(조작 불가) → 전환 완료 대기 → 성공하면 원래 목적인
        // 구매 확인 팝업을 이어서 띄운다. 실패/취소하면 실패 메시지 후 팝업만 닫는다.
        private void ShowGoogleLoginRequiredPopup(string productKey)
        {
            UIPopup popup = UIPopup.CreateOrGet();
            if (popup == null)
                return;

            popup.Set(
                LanguageManager.Instance.Get(NeedGoogleTitleKey),
                LanguageManager.Instance.Get(NeedGoogleBodyKey),
                () => StartCoroutine(GoogleLinkingRoutine(productKey)));
        }

        // "구글 계정으로 연동 중입니다." 모달(버튼/X 없음, 딤 배경 터치 무시) 표시 → 전환 완료 대기
        private System.Collections.IEnumerator GoogleLinkingRoutine(string productKey)
        {
            // UIPopup은 Single 재사용이라, 확인을 누른 전환 안내 팝업이 닫힌 다음 프레임에 연다
            yield return null;

            UIPopup linkingPopup = UIPopup.CreateOrGet();
            if (linkingPopup != null)
                linkingPopup.Set(
                    LanguageManager.Instance.Get(GoogleLinkingTitleKey),
                    LanguageManager.Instance.Get(GoogleLinkingBodyKey),
                    null, showCoinIcon: false, showButtons: false);

            bool done = false;
            bool linked = false;
            FireBaseAuthManager.Instance.UpgradeGuestToGoogle(success =>
            {
                done = true;
                linked = success;
            });

            // 완료 대기 (콜백 유실 대비 타임아웃 안전장치 - 초과 시 실패로 처리)
            float timeout = GoogleLinkingTimeoutSeconds;
            while (!done && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            // 결과 메시지를 먼저 알리고 진행 팝업을 닫는다
            UITopMessage.ShowMessage(LanguageManager.Instance.Get(
                linked ? "login_message_google" : GoogleLinkFailKey));

            if (linkingPopup != null)
                linkingPopup.Close();

            if (!linked)
                yield break;

            // 원래 목적인 구매 확인 팝업으로 이어간다 (같은 UIPopup 재사용이라 한 프레임 뒤)
            yield return null;
            ShowPurchaseConfirmPopup(productKey);
        }

        // 구매 확인 팝업 - 확인 시 원래 시도했던 상품의 구매를 다시 진행한다 (이제 구글 계정이라 스토어로 직행)
        private void ShowPurchaseConfirmPopup(string productKey)
        {
            if (ShopCatalog.Find(productKey) == null)
                return;

            UIPopup popup = UIPopup.CreateOrGet();
            if (popup == null)
                return;

            popup.Set(
                LanguageManager.Instance.Get(PurchaseConfirmTitleKey),
                LanguageManager.Instance.Get(PurchaseConfirmBodyKey),
                () => Purchase(productKey));
        }

        // ── 스토어 이벤트 ───────────────────────────────────────────

        void HandleProductsUpdated()
        {
            OnProductsUpdated?.Invoke();
        }

        // 결제 성공: 코인 즉시 지급(연출 없이 - 지급 누락 방지) → 성공 팝업(코인 아이콘)
        void HandlePurchaseSucceeded(ShopProduct product)
        {
            _purchaseRequestedAt = -1;
            Debug.Log($"[ShopManager] 구매 성공: {product.Key} → 코인 {product.CoinAmount} 지급");

            ShowPopup(
                LanguageManager.Instance.Get(SuccessTitleKey),
                LanguageManager.Instance.Get(SuccessBodyKey, FormatCoin(product.CoinAmount)),
                showCoinIcon: true);
        }

        // 결제 실패: 사용자가 직접 취소한 경우는 조용히 종료, 그 외는 사유와 함께 실패 팝업
        void HandlePurchaseFailed(ShopProduct product, ShopPurchaseFailure reason, string details)
        {
            _purchaseRequestedAt = -1;
            if (product != null && (reason == ShopPurchaseFailure.UserCancelled ||
                reason == ShopPurchaseFailure.PaymentDeclined || reason == ShopPurchaseFailure.ProductUnavailable ||
                reason == ShopPurchaseFailure.StoreNotReady || reason == ShopPurchaseFailure.NotSupported))
            {
                string key = "PurchaseRequestedOwner_" + product.Key;
                if (PlayerPrefs.GetString(key, "") == _purchaseOwner)
                {
                    PlayerPrefs.DeleteKey(key);
                    PlayerPrefs.Save();
                }
            }
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
