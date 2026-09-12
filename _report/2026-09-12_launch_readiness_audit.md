# Pick 출시 준비 추가 점검

작성: 2026-09-12
범위: D:/pick 현재 작업 트리, 한국 Android 소프트 런칭.
게임 소스/프리팹/테이블은 이번 점검에서 변경하지 않았다. 보고서와 회귀 검사 로그만 작성했다.

## 결론

소규모 외부 테스트는 가능하지만, 광고 보상 누락과 결제 복구 문제를 해결하기 전에는 유료 유입 확대나 결제 활성 상태의 공개 출시를 권하지 않는다.
이전 팝업/카메라/텍스처 검증은 시각/리소스 검증이다. 광고 SDK 콜백 순서, 강제 종료, 다중 기기 저장 충돌을 보장하는 검증은 아니었다.

## P1: 출시 전에 수정할 문제

### 1. 광고 보상이 닫힘 이벤트보다 늦게 도착하면 지급 콜백이 소실됨

- 근거: Assets/Script/FunRabbit/Ads/LevelPlayAds.cs:174, 185, 225.
- OnAdClosed 다음 한 프레임에 보상이 아직 없으면 FailReward를 호출한다.
- FailReward는 _onRewardGranted와 _onRewardFailed를 모두 null로 만든다.
- 이후 OnAdRewarded가 들어와도 GrantReward에서 실행할 지급 콜백이 없다.
- Unity 공식 문서는 OnAdRewarded와 OnAdClosed가 비동기이며 닫힘 이후 보상도 처리해야 한다고 명시한다.
- 확인 방법: 실제 소스에서 해당 세 메서드를 추출한 독립 C# 하네스. 구형 PowerShell 컴파일러를 위해 null 조건 호출 문법만 동등한 if-null 검사로 변환했다. Unity/광고 서버는 호출하지 않았다.
- 결과:
  - 보상 → 닫힘: rewards=1, failures=0.
  - 닫힘 → 한 프레임 경과 → 보상: rewards=0, failures=1.
- 수정 방향: 닫힘과 보상 확정을 별도 상태로 관리. 늦은 보상도 해당 광고 요청에 한 번만 귀속되도록 처리. 다음 광고 요청과 콜백을 섞지 않도록 요청 식별/중복 방어 필요.

출처: [Unity LevelPlay 보상형 광고 문서](https://docs.unity.com/en-us/grow/levelplay/sdk/unity/rewarded-ad-integration-package)

### 2. 광고 시청 횟수는 저장되지만 코인은 연출 뒤에 지급됨

- 근거: Assets/Script/FunRabbit/GameMain.cs:261, 272; Assets/Script/FunRabbit/UI/HUD/UIBottomBar.cs:149, 166, 224; PlayerContext.cs:342.
- 광고 완료 시 AddWatchAdCount가 먼저 PlayerPrefs.Save를 수행한다.
- 코인 지급은 1초 대기 후 비행 연출의 각 도착 콜백에서 나뉘어 실행된다.
- 미지급 잔액은 _pendingCoinReward라는 메모리 필드에만 남는다.
- 첫 1초에 프로세스가 종료되면 시청 횟수만 차감되고 코인 지급 정보는 사라질 수 있다. 비행 중 종료라면 일부만 지급될 수 있다.
- OnDestroy 정산은 정상적인 UI 제거에는 도움이 되지만 OS 강제 종료/크래시에서는 보장되지 않는다.
- 이 항목은 코드 경로 분석이며 실제 기기의 강제 종료 재현은 아직 하지 않았다.
- 수정 방향: 완료 확정 시 코인과 시청 기록을 먼저 내구성 있게 저장하고 연출은 표시만 수행하거나, 미지급 보상 원장을 영속화하고 재실행 시 복구한다.
- 재검사: 완료 직후/1초 대기 중/코인 비행 중 프로세스 종료와 재시작. 항상 정확히 500코인, 중복 없음.

### 3. 다른 기기의 저장과 충돌하면 결제 재시도가 끝나지 않을 수 있음

- 근거: Assets/Script/FunRabbit/CloudSave/CloudSaveManager.cs:320, 263; Assets/Script/FunRabbit/Shop/ShopManager.cs:164, 179, 186.
- 미확정 업로드를 재확인하다가 다른 기기의 더 최신 저장을 발견하면 _synced=false가 된다.
- 구매 지급 루프는 SessionOperation으로 입력/시간을 막은 상태에서 SaveNow 성공을 기다린다.
- 재시도 버튼은 retry 플래그만 켜고 다시 SaveNow를 호출한다.
- SaveNow는 IsSynced=false이면 즉시 실패하므로, 같은 버튼을 계속 눌러도 재동기화되지 않는다.
- 코드 로그에는 재시작 요구가 있으나 사용자에게 제공되는 재시도 경로에는 복구 동작이 없다.
- 코드 경로 분석. 실제 Firebase 다중 기기 충돌은 이번에 만들지 않았다.
- 수정 방향: 일반 일시 오류와 저장 충돌을 구별하고, 충돌에서는 명시적 재동기화/재로그인 경로를 제공한다. 이미 로컬 지급된 구매 원장과 미확정 주문이 중복 지급되거나 사라지지 않도록 별도 검증해야 한다.

### 4. 광고 최초 로드 실패 후 코인 부족 팝업에서 복구할 경로가 없음

- 근거: Assets/Script/FunRabbit/Ads/LevelPlayAds.cs:111, 172; Assets/Script/FunRabbit/UI/UICoinShortPopup.cs:42.
- 광고 초기화/로드 실패 처리는 로그 기록뿐이다. 앱 차원의 지연 재시도/네트워크 복구 처리가 없다.
- 새 코인 부족 팝업은 ready=false면 광고 버튼을 비활성화하고 상태만 다시 읽는다.
- 초기 로드 실패 뒤에는 이 팝업에서 새 요청을 보낼 수 없어 준비 중 상태가 지속될 수 있다.
- 기존 하단 무료 광고 경로에서는 요청 실패 시 재로드 호출이 가능하지만, 코인 부족 팝업의 복구 동선은 아니다.
- 수정 방향: 제한된 지수 백오프 재시도, 연결 복구/팝업 재진입 시 재요청, 준비 중과 불러오기 실패 상태 구분. SDK 동작을 실기기로 확인해야 한다.

## P2: 소프트 런칭 데이터 품질과 진행 표시

### 5. 첫 이벤트 유실 및 반복 플레이 계측 부족

- 근거: Assets/Script/FunRabbit/MMPService/FireBaseAnalyticsManager.cs:49; Assets/Script/FunRabbit/UI/HUD/UIHud.cs:550.
- LogEventOnce는 전송 가능 여부를 확인하기 전에 완료 마커를 영구 저장한다.
- 초기화 전에 호출되면 LogEvent는 버려지는데 마커는 남아 이후에도 다시 전송되지 않는다.
- click_play는 설치당 한 번만 기록하므로 총 시도 수/실패율/코인 소진과 이탈 관계를 계산할 수 없다.
- 현재 FunRabbit 코드에서 crane_attempt, first_doll, currency_earned/spent, ad_impression 전용 이벤트는 찾지 못했다. SDK 자동 수집 이벤트와 별개로 게임 행동 분석이 부족하다.
- 수정 방향: 초기 이벤트 큐 또는 준비 후 마커 기록; 시도별 식별자, 단계, 성공 여부, 획득 수, 잔액, 첫 성공 시간, 광고 수익 이벤트 추가.
- 출시 판단 전에 Firebase DebugView/실제 설치 코호트에서 누락·중복·이벤트 속성을 확인한다.

### 6. 로비 진입 버튼의 스테이지 문구가 고정 문자열임

- 근거: Assets/Resources/UI2/Prefabs/UIHud.prefab:1215; Assets/Resources/Table/stringData.json의 hud_stage_placeholder; UIHud의 enterStageButton.
- 문구가 모든 언어에서 1스테이지를 나타내는 고정 로컬라이즈 키에 연결돼 있다.
- 현재 스테이지로 해당 라벨을 갱신하는 코드를 찾지 못했다.
- 이전 영상에서도 여러 변형 인형/다른 보스가 등장하는 상태에서 스테이지 1 문구가 보였다. 영상이 신규 상태인지 확인되지는 않았으므로 초기 난도 평가 근거로 쓰지는 않는다.
- 수정 방향: 실제 현재 스테이지와 변경 이벤트에 맞춰 번호를 갱신한다.

## 확인하지 못한 출시 필수 항목

### 개인정보 안내와 계정/데이터 삭제

현재 UISettingPanel과 FunRabbit 소스에서 개인정보 URL 열기, 계정 삭제 요청 진입점, Firebase 사용자 삭제 호출을 찾지 못했다.
이전 설명에는 관련 설정을 했다는 내용이 있어 서비스 전체가 미설정이라고 단정하지 않는다. Play Console 및 외부 페이지는 이번에 접근하지 않았다.
앱 내 계정 생성 기능이 정책 적용 대상이라면 앱 내 삭제 요청 경로와 앱 밖 웹 경로가 모두 필요하므로 실제 사용자 동선을 확인해야 한다.

출처: [Google Play 계정 삭제 요구사항](https://support.google.com/googleplay/android-developer/answer/13327111?hl=en)

추가 확인: 개인정보처리방침과 Data safety 선언이 Firebase/광고 SDK의 실제 수집에 맞는지, 대상 연령/아동 여부, 배포 국가에 따른 동의 및 SDK 설정.

### 실제 배포 산출물과 Android QA

- 생성된 Gradle launcher 설정과 병합 manifest에서 targetSdk 36을 확인했다. 현재 일반 Android 신규 앱/업데이트의 API 36 요구사항에는 맞는 방향이다.
- ProjectSettings의 Target SDK는 Automatic이므로 최종 제출 AAB도 확인해야 한다.
- 생성된 Gradle은 IL2CPP 경로이며 Billing dependency는 9.0.0이었다. 이것만으로 최종 산출물의 제출/실행 성공을 보장하지 않는다.
- build 폴더의 AAB는 2026-08-29 파일들이다. 확인한 validation APK는 2026-09-07 빌드다. 다른 경로의 새 산출물 존재/실제 Play Console 업로드 여부는 확인하지 않았다.
- 이번 점검에서는 새 AAB 빌드나 설치를 하지 않았다.

출처: [Google Play Target API 요구사항](https://support.google.com/googleplay/android-developer/answer/11926878?hl=en)

검증할 시나리오:
1. 최신 서명 AAB를 Play 내부 테스트로 설치. 게스트 첫 실행/Google 로그인/계정 전환.
2. 저사양 기기의 20~30분 연속 플레이, 발열/FPS, 가로세로 비율과 노치.
3. 광고 없음/네트워크 끊김/광고 중 복귀/지연된 보상/앱 강제 종료.
4. 실제 테스트 결제 성공·취소·승인 지연·미확정 주문 복구·중복 콜백.
5. 클라우드 일시 장애와 다른 기기 저장 충돌.
6. 네이티브 SDK를 포함한 16KB 메모리 페이지 호환성 등 최종 Play 사전 출시 보고서.
7. 설치 코호트 기준 D1/D7, 첫 3회 성공, 단계별 시도/코인 소모, 실제 광고 매출 데이터.

### 결제 보안과 운영

AndroidShopStore에서 거래 ID/상품/수량 검사는 확인했지만 서버 구매 토큰 검증 호출은 찾지 못했다.
중복 지급 방지 원장은 존재하나 영수증 진위 검증과 같은 것은 아니다. 외부 서버 구성이 별도로 있는지 확인해야 한다.
Firestore 보안 규칙, 계정별 쓰기 권한, 운영자 지급/환불/문의 처리 경로와 모니터링은 이번 접근 범위 밖이다.

## 수행한 검사

- 현재 코드/테이블/프리팹/Android 생성 설정 읽기.
- 기존 Tools/Release Safety/Run regression checks 실행: PASS 11개.
- 로그: Logs/release_safety_result.txt.
- 광고 콜백 순서 독립 하네스: 늦은 보상 누락 재현.
- 실제 광고·결제·Firebase 쓰기·유저 데이터 초기화는 하지 않음.
- 기존 11개 통과는 광고 지연/강제 종료/다중 기기 충돌까지 통과했다는 의미가 아님.

## 권장 순서

1. 광고 지급 콜백 보존과 보상 저장을 먼저 수정.
2. 결제 저장 충돌 복구 및 광고 재로드 경로 보완.
3. 첫 실행/시도별 계측과 실제 스테이지 문구 수정.
4. 삭제/개인정보 실제 동선과 최신 AAB 실기기 QA 확인.
5. 제한된 유저 유입으로 소프트 런칭, 데이터 확인 전 유입비 확대 보류.
