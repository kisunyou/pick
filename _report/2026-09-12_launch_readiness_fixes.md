# 출시 안정성 수정 - 2026-09-12

대상: D:/pick. 기존 미커밋 작업은 유지했고 커밋하지 않았다.

## 반영한 수정

1. 광고 닫힘과 지급을 분리했다. RewardedAdRequest는 닫힘 후에도 지급 가능하며 지급/실패/닫힘의 중복 콜백을 방어한다.
2. LevelPlayAds는 AuctionId(없으면 AdId)를 통해 요청을 찾아 이전 광고의 지연 보상이 새 광고에 잘못 귀속되지 않게 했다. 보상 식별 정보가 없는 애매한 콜백은 다른 요청에 임의로 지급하지 않는다.
3. 광고 완료 영수증은 계정별 AdRewardInbox에 먼저 기록한다. 코인·시청 날짜/횟수·처리한 보상 ID는 CoinWallet의 단일 JSON에 저장한다. 영수증을 다시 처리해도 중복 지급하지 않는다.
4. 보상 팝업/비행은 표시만 수행한다. 이미 확정된 금액을 애니메이션 도착 시 다시 지급하지 않는다.
5. 초기화/광고 로드 실패에 2, 4, 8초 등 최대 60초 간격의 재시도를 추가했다. 팝업 진입과 앱 복귀도 준비를 재확인한다. 로드 실패/재시도 문구를 추가했다.
6. 클라우드 저장 충돌을 별도로 표시한다. 사용자가 동기화를 선택하면 서버 상태를 명시적으로 불러오고, 미확정 구매를 서버 지갑의 거래 원장 기준으로 한 번만 다시 적용한다. 동기화 전후 계정 ID를 검사한다.
7. Firebase 준비 전의 기존 분석 이벤트는 메모리 대기열에 보관한다. 일회성 마커는 SDK에 이벤트를 넘긴 뒤 저장하며, 전달 예외에서는 이벤트를 보존한다.
8. 로비 진입 버튼의 번호를 현재 스테이지 및 언어 변경에 맞춰 갱신한다.

## 주요 파일

- Assets/Script/FunRabbit/Ads/RewardedAdRequest.cs
- Assets/Script/FunRabbit/Ads/AdRewardInbox.cs
- Assets/Script/FunRabbit/Ads/LevelPlayAds.cs
- Assets/Script/FunRabbit/Shop/CoinWallet.cs
- Assets/Script/FunRabbit/Shop/ShopManager.cs
- Assets/Script/FunRabbit/CloudSave/CloudSaveManager.cs
- Assets/Script/FunRabbit/CloudSave/SessionOperation.cs
- Assets/Script/FunRabbit/GameMain.cs
- Assets/Script/FunRabbit/PlayerContext.cs
- Assets/Script/FunRabbit/MMPService/AnalyticsEventQueue.cs
- Assets/Script/FunRabbit/MMPService/FireBaseAnalyticsManager.cs
- Assets/Script/FunRabbit/UI/HUD/UIHud.cs
- Assets/Script/FunRabbit/UI/UICoinShortPopup.cs
- Assets/Resources/Table/stringData.json

## 검증

- Tools/Release Safety/Run launch readiness checks: 20개 통과.
- Tools/Release Safety/Run regression checks: 기존 11개 통과.
- Logs/launch_readiness_result.txt 및 Logs/release_safety_result.txt.
- 지연/중복/재진입 광고 콜백, 서로 다른 요청 라우팅, 지갑 재직렬화/중복 영수증/날짜 변경/오버플로,
  서버 구매 원장 재적용, 이벤트 초기 대기/실패/용량 초과, 4개 언어 단계 표시를 검사했다.
- 검사에서 실제 광고, 구매, Firebase 요청 또는 플레이어 저장 변경을 수행하지 않았다.
- 실제 Firebase 충돌 복구 전체 흐름, 실제 Android 광고 완료/취소/프로세스 종료/복귀와 구매 처리는 실기기 QA가 필요하다.
- SDK의 보상 콜백 자체가 프로세스 종료 전에 도착하지 않은 경우까지 클라이언트만으로 복구한다는 의미는 아니다.
  수신해 저장한 영수증과 지갑 적용의 중복/유실을 방어한다.

## 남은 확인 사항

### 추가 플레이 계측: 승인 후 반영 완료

초기 권한 검토에서는 차단됐으나, 이후 사용자가 뽑기 ID·스테이지·인형·성공/실패·플레이 시간·코인 잔액/사용량 전송을 명시적으로 승인했다.
승인된 플레이 계측을 추가했다. 광고 수익이나 계정/거래 ID 등은 새 전송 필드에 넣지 않았다.
상세 이벤트와 검증 범위: [승인된 플레이 계측](2026-09-12_approved_gameplay_analytics.md).

### 개인정보/계정 삭제: 실제 URL 대기

개인정보처리방침 URL과 계정 삭제 요청 URL을 요청했으나 아직 받지 못했다.
가짜 주소나 작동하지 않는 삭제 버튼을 추가하지 않았다. 실제 운영 경로 확인 후 앱에 연결해야 한다.

### 배포 검증

이번 작업에서는 새 AAB를 빌드하거나 기기에 설치하지 않았다.
현재 배포 준비 완료 또는 실제 스토어 결제/광고 연동 완료로 간주하지 않는다.
