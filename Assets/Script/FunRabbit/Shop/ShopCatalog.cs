using System.Collections.Generic;

namespace FunRabbit
{
    // 상점 상품 1건. 스토어 제품 ID 는 플랫폼별로 다를 수 있어 Android / iOS 를 따로 둔다.
    public class ShopProduct
    {
        public string Key;               // 내부 키 (프리팹 UIShopItem.productId 와 일치) - Android 제품 ID 와 같게 둔다
        public string AndroidProductId;  // Google Play Console 제품 ID
        public string IosProductId;      // App Store Connect 제품 ID (미정 - 일단 Android 와 동일)
        public long CoinAmount;          // 결제 성공 시 지급할 코인 수

        // 현재 빌드 플랫폼의 스토어 제품 ID
        public string GetStoreProductId()
        {
#if UNITY_IOS
            return IosProductId;
#else
            return AndroidProductId;
#endif
        }
    }

    // 상점 상품 카탈로그. 스토어 콘솔(Google Play / App Store)에 등록된 제품과 1:1 로 맞춘다.
    public static class ShopCatalog
    {
        public const string CoinSmall = "takepick_coin_10000";
        public const string CoinLarge = "takepick_coin_50000";

        public static readonly IReadOnlyList<ShopProduct> Products = new[]
        {
            new ShopProduct { Key = CoinSmall, AndroidProductId = "takepick_coin_10000", IosProductId = "takepick_coin_10000", CoinAmount = 10000 },
            new ShopProduct { Key = CoinLarge, AndroidProductId = "takepick_coin_50000", IosProductId = "takepick_coin_50000", CoinAmount = 50000 },
        };

        public static ShopProduct Find(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            foreach (ShopProduct product in Products)
            {
                if (product.Key == key)
                    return product;
            }
            return null;
        }

        // 스토어가 돌려준 제품 ID(현재 플랫폼 기준) → 상품
        public static ShopProduct FindByStoreProductId(string storeProductId)
        {
            if (string.IsNullOrEmpty(storeProductId))
                return null;

            foreach (ShopProduct product in Products)
            {
                if (product.GetStoreProductId() == storeProductId)
                    return product;
            }
            return null;
        }
    }
}
