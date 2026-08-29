using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 10분 카운트다운 → "받기" → 버튼 클릭 시 코인 지급 후 다시 10분 카운트다운을 반복하는 컨트롤.
    // 표시(coinTimerText)와 입력(getCoinTimerButton)은 UIHud의 것을 사용하고,
    // 코루틴 구동만 UIHud(MonoBehaviour)에 위임한다.
    // 코인 비행 연출(도착 분할 지급 포함)은 UIBottomBar가 담당한다 - 여기서는 출발점만 넘긴다.
    public class CoinGetTimer
    {
        private const float Duration = 600f;        // 타이머 길이(초) = 10분
        private const long RewardCoinAmount = 400;  // "받기" 시 지급할 코인
        private const string ClaimLabelKey = "coin_timer_get"; // 0:00 도달 후 표시할 문구 (LanguageManager 키)
        private const string KeyEndTime = "CoinTimerEndTimeUtc"; // 종료 목표 시각(UTC ticks) 저장 키

        // "받기" 가능 상태 텍스트 펄스 연출 설정
        private const float ClaimPulseScale = 1.15f;     // 텍스트가 커지는 배율
        private const float ClaimPulseDuration = 0.35f;  // 커졌다 / 작아지는 각 구간 시간(초)
        private const float ClaimPulseInterval = 0.4f;   // 한 번 펄스 후 다음 펄스까지 간격(초)

        private readonly MonoBehaviour _runner;
        private readonly TextMeshProUGUI _timerText;
        private readonly Button _claimButton;

        private readonly RectTransform _coinFlyStart;    // 코인 비행 연출 출발 지점 (cointimer)

        private readonly Slider _slider;                 // 남은 시간을 표시하는 슬라이더
        private readonly Vector3 _timerTextBaseScale;    // 타이머 텍스트 원래 스케일(펄스 복귀 기준)

        private Sequence _claimPulseSeq;                 // "받기" 상태 텍스트 펄스 루프

        private Coroutine _coroutine;
        private bool _claimable; // 카운트다운이 끝나 "받기" 입력을 기다리는 상태인지
        private System.DateTime _endTimeUtc; // 카운트다운 종료(받기 가능) 목표 시각 (UTC, 절대 시간)

        public CoinGetTimer(MonoBehaviour runner, TextMeshProUGUI timerText, Button claimButton,
            RectTransform coinFlyStart = null, Slider slider = null)
        {
            _runner = runner;
            _timerText = timerText;
            _claimButton = claimButton;
            _slider = slider;
            _coinFlyStart = coinFlyStart;

            if (_timerText != null)
                _timerTextBaseScale = _timerText.transform.localScale;

            if (_claimButton != null)
                _claimButton.onClick.AddListener(OnClickClaim);

            // "받기" 라벨은 다국어 문자열이라, 받기 대기 중 언어가 바뀌면 다시 그려야 한다 (Dispose 에서 해제)
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        }

        // 언어 변경: 받기 대기 상태면 라벨을 새 언어로 갱신 (카운트다운 중엔 숫자라 갱신 불필요)
        private void OnLanguageChanged()
        {
            if (_claimable && _timerText != null)
                _timerText.text = LanguageManager.Instance.Get(ClaimLabelKey);
        }

        // 새 5분 사이클 시작 (첫 실행 / "받기" 이후). 종료 목표 시각을 저장한다.
        public void Begin()
        {
            if (_runner == null)
                return;

            _endTimeUtc = System.DateTime.UtcNow.AddSeconds(Duration);
            SaveEndTime();
            StartCountdown();
        }

        // 저장된 상태를 복원한다. (앱 재실행 시 호출)
        // 앱이 꺼져 있던 실제 시간도 반영되며, 저장된 상태가 없으면 새로 시작한다.
        public void Resume()
        {
            if (_runner == null)
                return;

            if (!TryLoadEndTime(out _endTimeUtc))
            {
                Begin(); // 저장된 상태 없음 → 새 사이클
                return;
            }

            double remaining = (_endTimeUtc - System.DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0)
            {
                // 꺼져 있는 동안 이미 완료됨 → 바로 "받기" 상태로 복원
                Stop();
                StopClaimTextPulse();
                UpdateTimerText(0);
                UpdateSlider(0);
                EnterClaimableState();
            }
            else
            {
                // 남은 시간까지 이어서 카운트다운
                StartCountdown();
            }
        }

        private void StartCountdown()
        {
            Stop();
            StopClaimTextPulse();         // "받기" 상태 텍스트 펄스 종료
            _claimable = false;
            SetButtonInteractable(false); // 카운트다운 중에는 버튼 비활성(회색)
            _coroutine = _runner.StartCoroutine(CountdownCoroutine());
        }

        public void Stop()
        {
            if (_coroutine != null && _runner != null)
            {
                _runner.StopCoroutine(_coroutine);
                _coroutine = null;
            }
        }

        public void Dispose()
        {
            if (LanguageManager.IsCheckInstance())
                LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;

            Stop();
            _claimPulseSeq?.Kill();
            if (_claimButton != null)
                _claimButton.onClick.RemoveListener(OnClickClaim);
        }

        private IEnumerator CountdownCoroutine()
        {
            // 남은 시간을 저장된 종료 시각(_endTimeUtc)과 현재 시각의 차이로 매 초 계산한다.
            // (게임 일시정지/프레임 변동과 무관하게 실제 시간 기준으로 정확히 감소)
            while (true)
            {
                double remainingSec = (_endTimeUtc - System.DateTime.UtcNow).TotalSeconds;
                if (remainingSec <= 0)
                    break;

                int remaining = Mathf.CeilToInt((float)remainingSec);
                UpdateTimerText(remaining);
                UpdateSlider(remaining);
                yield return new WaitForSeconds(1f);
            }

            // 0:00 도달: "00:00"을 한 번 보여준 뒤 "받기" 대기 상태로 진입
            UpdateTimerText(0);
            UpdateSlider(0);
            EnterClaimableState();
        }

        // 카운트다운 종료: "받기" 표시 후 버튼 입력 전까지 정지
        private void EnterClaimableState()
        {
            _coroutine = null;
            _claimable = true;

            if (_timerText != null)
                _timerText.text = LanguageManager.Instance.Get(ClaimLabelKey);

            SetButtonInteractable(true); // "받기" 상태에서 버튼 활성화
            StartClaimTextPulse();       // "받기" 텍스트 강조 펄스 시작
        }

        private void SetButtonInteractable(bool interactable)
        {
            if (_claimButton != null)
                _claimButton.interactable = interactable;
        }

        private void OnClickClaim()
        {
            // "받기" 상태가 아닐 때(카운트다운 중)의 클릭은 무시
            if (!_claimable)
                return;

            // 코인 비행 연출 + 도착 분할 지급은 UIBottomBar가 담당한다.
            // (하단 바가 없는 예외 상황에서는 전액 즉시 지급으로 폴백)
            if (UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(_coinFlyStart, RewardCoinAmount);
            else
                PlayerContext.AddCoinAmount(RewardCoinAmount);

            Begin(); // 다시 5분 타이머 시작
        }

        // 외부(테스트 등)에서 코인 획득 연출만 단독으로 재생하기 위한 진입점. (지급 없음)
        public void PlayCoinGetEffect()
        {
            if (UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(_coinFlyStart);
        }

        // 남은 초를 "mm:ss"(예: "05:00") 형식으로 표시
        private void UpdateTimerText(int totalSeconds)
        {
            if (_timerText == null)
                return;

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            _timerText.text = $"{minutes:00}:{seconds:00}";
        }

        // 남은 시간에 따라 슬라이더를 갱신한다. (시간이 지날수록 0 → 1로 차오름, 받기 직전 가득 참)
        private void UpdateSlider(int remaining)
        {
            if (_slider == null)
                return;

            // minValue/maxValue 설정과 무관하게 0~1로 다루기 위해 normalizedValue 사용
            _slider.normalizedValue = 1f - Mathf.Clamp01(remaining / Duration);
        }

        // 종료 목표 시각(UTC ticks)을 저장한다. (재실행 후 복원용)
        private void SaveEndTime()
        {
            PlayerPrefs.SetString(KeyEndTime, _endTimeUtc.Ticks.ToString());
            PlayerPrefs.Save();
        }

        // 저장된 종료 목표 시각을 불러온다. 저장이 없거나 파싱 실패면 false.
        private bool TryLoadEndTime(out System.DateTime endTimeUtc)
        {
            endTimeUtc = default;

            string s = PlayerPrefs.GetString(KeyEndTime, "");
            if (string.IsNullOrEmpty(s) || !long.TryParse(s, out long ticks))
                return false;

            endTimeUtc = new System.DateTime(ticks, System.DateTimeKind.Utc);
            return true;
        }

        // 저장된 종료 목표 시각 기준 남은 초 (음수 = 이미 "받기" 가능 상태).
        // 저장된 타이머가 없으면 false. 인스턴스 없이도 조회할 수 있도록 static -
        // 백그라운드 진입 시 로컬 알림 예약(CoinRewardNotificationScheduler)이 사용한다.
        public static bool TryGetRemainingSeconds(out double remainingSeconds)
        {
            remainingSeconds = 0;

            string s = PlayerPrefs.GetString(KeyEndTime, "");
            if (string.IsNullOrEmpty(s) || !long.TryParse(s, out long ticks))
                return false;

            var endTimeUtc = new System.DateTime(ticks, System.DateTimeKind.Utc);
            remainingSeconds = (endTimeUtc - System.DateTime.UtcNow).TotalSeconds;
            return true;
        }

        // "받기" 가능 상태에서 텍스트를 일정 간격으로 커졌다 작아지게 반복해 주목도를 높인다.
        private void StartClaimTextPulse()
        {
            if (_timerText == null)
                return;

            StopClaimTextPulse();

            Transform t = _timerText.transform;
            t.localScale = _timerTextBaseScale;

            // 커짐 → 원래 크기 → 잠깐 정지 를 무한 반복
            _claimPulseSeq = DOTween.Sequence()
                .Append(t.DOScale(_timerTextBaseScale * ClaimPulseScale, ClaimPulseDuration).SetEase(Ease.OutQuad))
                .Append(t.DOScale(_timerTextBaseScale, ClaimPulseDuration).SetEase(Ease.InQuad))
                .AppendInterval(ClaimPulseInterval)
                .SetLoops(-1);
        }

        private void StopClaimTextPulse()
        {
            _claimPulseSeq?.Kill();
            _claimPulseSeq = null;

            if (_timerText != null)
                _timerText.transform.localScale = _timerTextBaseScale;
        }
    }
}
