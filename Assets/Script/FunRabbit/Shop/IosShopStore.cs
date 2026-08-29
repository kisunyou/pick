using System;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // App Store 인앱 결제 - 아직 미구현 스텁.
    // 가격은 항상 없음(→ N/A), 구매 요청은 NotSupported 실패로 응답한다.
    // 구현 시 AndroidShopStore 와 같은 Unity IAP StoreController 흐름을 쓰되 Apple 확장
    // (StoreController.AppleStoreExtendedService / 영수증 검증 / RestoreTransactions)을 추가한다.
#pragma warning disable 67   // 미구현 스텁 - 이벤트 미사용 경고 억제
    public class IosShopStore : IShopStore
    {
        public bool IsReady => false;

        public event Action OnProductsUpdated;
        public event Action<ShopProduct> OnPurchaseSucceeded;
        public event Action<ShopProduct, ShopPurchaseFailure, string> OnPurchaseFailed;

        public void Initialize(IReadOnlyList<ShopProduct> products)
        {
            Debug.LogWarning("[IosShopStore] iOS 인앱 결제는 아직 구현되지 않았습니다 - 가격 N/A, 구매 불가");
            OnProductsUpdated?.Invoke();
        }

        public string GetLocalizedPrice(ShopProduct product)
        {
            return null;
        }

        public void Purchase(ShopProduct product)
        {
            OnPurchaseFailed?.Invoke(product, ShopPurchaseFailure.NotSupported, "iOS store not implemented");
        }
    }
#pragma warning restore 67
}
