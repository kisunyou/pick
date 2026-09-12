# 승인된 플레이 계측 추가 - 2026-09-12

## 승인과 목적지
사용자가 뽑기 시도 ID, 스테이지, 인형 종류, 성공/실패, 플레이 시간, 코인 잔액/사용량을 기존 Pick Firebase Analytics 프로젝트로 전송하도록 명시적으로 승인했다.
Assets/google-services.json에서 기존 프로젝트 ID pick-ddf42를 확인했다. Firebase 설정 파일이나 프로젝트/계정은 변경하지 않았다.

## 신규 이벤트
| 이벤트 | 데이터 / 의미 |
|---|---|
| crane_attempt_start | attempt_id, stage, result=started, play_seconds=0, collected_count=0, coins_before, coins_after |
| crane_attempt_end | 같은 attempt_id와 시작 stage, result=success/failure/interrupted, play_seconds, collected_count, coins_before, coins_after |
| doll_collected | 유료 뽑기 중 중복 처리되지 않은 바구니 진입: attempt_id, stage, animal, play_seconds, coins |
| first_doll_collected | 위 데이터로 기기당 한 번. 랜덤박스는 제외하며, 계측 도입 후 첫 인형 획득을 의미한다. |
| stage_start | 실제 플레이 시작 또는 다른 단계 진입: stage, coins |
| currency_earned | 실제 증가량 amount, 변경 후 balance, stage, 활성 뽑기의 attempt_id |
| currency_spent | 실제 차감 성공 시 amount(양수), 변경 후 balance, stage, 활성 뽑기의 attempt_id |

- 뽑기 시작은 100코인 차감 성공 뒤에 기록한다. 비용 이벤트는 시작 전에 발생하므로 해당 currency_spent의 attempt_id는 빈 값일 수 있다. 시도 시작의 coins_before/coins_after도 함께 기록한다.
- 결과의 coins_after에는 진행 중 다른 코인 보상도 반영될 수 있으므로, 시작/종료 잔액 차이를 전체 소비량으로 해석하지 않는다.
- 여러 인형을 획득하면 collected_count가 증가한다. 여러 콜라이더의 같은 인형은 한 번만 센다. 랜덤박스도 게임 규칙대로 뽑기 성공에 포함한다.
- 플레이 시간은 활성 시도의 프레임 시간을 누적한다. 백그라운드 및 Time.timeScale=0 구간은 제외하고 복귀 첫 프레임의 시간 급증도 제외한다.
- 정상 종료 또는 씬 비활성화 시 중단을 기록한다. OS의 즉시 프로세스 종료에서는 end 이벤트가 보장되지 않으므로, start만 있고 end가 없는 시도도 이탈 분석에서 별도로 취급해야 한다.
- 초기화/클라우드 저장 복원의 직접 잔액 설정은 신규 획득으로 기록하지 않는다.
- 구매/광고 보상의 중복 집계를 막는 영수증 키는 로컬 대기열/PlayerPrefs에서만 사용한다. 해당 키를 이벤트 이름이나 파라미터로 전송하지 않는다.
- 계정 ID, 이메일, 구매 거래 ID, 광고 수익, FPS 등은 새 계측 필드에 추가하지 않았다. 기존 SDK 자동 수집 동작은 변경하지 않았다.
- 기존 click_play(설치당 한 번), clear_stage 등의 이벤트는 유지한다. 반복 뽑기 분석에는 새로운 attempt 이벤트를 사용한다.

## 파일과 연결
- CraneAttemptMetrics.cs: Unity/SDK와 분리된 시도 상태와 중복/중단 처리.
- GameplayAnalytics.cs: 승인된 필드만 Firebase 파라미터로 변환하고 기존 FireBaseAnalyticsManager로 전달.
- AnalyticsEventQueue.cs: 이벤트 이름과 로컬 일회성 키 분리. SDK로는 로컬 키를 보내지 않는다.
- UIHud.cs: 유효한 READY 상태에서 비용 차감 성공 후 시작.
- Crane.cs, Basket.cs: 결과 및 인형 획득 연결.
- GameMain.cs: 프레임 시간, 앱 일시정지/복귀/종료 연결.
- GameQuestManager.cs, CloudSaveManager.cs: 실제 단계 진입과 복원 시 중단 처리.
- PlayerContext.cs: 실제 코인 변경과 신규 보상만 계측.

## 검증
- Tools/Release Safety/Run gameplay analytics checks: 13개 통과.
- Tools/Release Safety/Run launch readiness checks: 기존 20개 통과.
- Tools/Release Safety/Run regression checks: 기존 11개 통과.
- 총 44개. 마지막 Unity 콘솔 오류 없음.
- 가짜 전송 함수를 사용한 오프라인 검사이며, 실제 Firebase로 테스트 이벤트를 보내지 않았다.
- 에디터 회귀 검사에서 게임용 객체를 생성하던 경로는 Application.isPlaying 조건으로 제한했다. 이 검사에서 생성된 임시 객체만 정리했으며 사용자 씬 파일은 저장/되돌리지 않았다.

## 남은 확인
실제 플레이에서 Firebase 수신 및 운영 대시보드 표시를 확인해야 한다. 이번 작업에서 실측 지표나 실제 수신 성공을 주장하지 않는다.
개인정보처리방침/계정 삭제 URL 연결은 별도 확인 항목으로 남아 있다.
