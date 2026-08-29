using System;
using System.Collections.Generic;

namespace FunRabbit
{
    // 결제 실패 사유 (스토어별 사유를 게임 공통 사유로 정규화 - ShopManager 가 메시지 키로 변환)
    public enum ShopPurchaseFailure
    {
        StoreNotReady,       // 스토어 미연결 / 상품 정보 미수신
        ProductUnavailable,  // 상품 없음 / 구매 불가
        InProgress,          // 다른 결제 진행 중
        UserCancelled,       // 사용자가 결제창을 닫음 (메시지 없이 종료)
        PaymentDeclined,     // 결제 수단 거절
        NotSupported,        // 이 플랫폼에서 결제 미지원 (iOS 미구현 등)
        Unknown,
    }

    // 플랫폼별 스토어 구현체 (AndroidShopStore = Google Play, IosShopStore = App Store).
    // ShopManager 가 하나를 골라 Initialize 하고, 이벤트로 결과를 받아 지급/메시지를 처리한다.
    public interface IShopStore
    {
        // 스토어 연결 + 상품 정보 수신 완료 여부 (false 면 가격은 N/A, 구매는 StoreNotReady 실패)
        bool IsReady { get; }

        // 상품 정보(가격)가 갱신됨 - UI 가 가격 텍스트를 다시 그리는 신호
        event Action OnProductsUpdated;

        // 결제 성공 (스토어 확정 전, 지급 시점). ShopManager 가 코인 지급 후 스토어 확정은 구현체가 이어서 처리
        event Action<ShopProduct> OnPurchaseSucceeded;

        // 결제 실패. product 는 알 수 없으면 null, details 는 스토어 원문(로그용)
        event Action<ShopProduct, ShopPurchaseFailure, string> OnPurchaseFailed;

        void Initialize(IReadOnlyList<ShopProduct> products);

        // 현지 통화 가격 문자열 (예: "₩1,200", "$0.99"). 못 받아 왔으면 null
        string GetLocalizedPrice(ShopProduct product);

        void Purchase(ShopProduct product);
    }
}
