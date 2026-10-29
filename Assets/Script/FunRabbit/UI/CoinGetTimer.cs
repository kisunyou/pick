using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 15분 카운트다운 → "받기" → 버튼 클릭 시 코인 지급 후 다시 15분 카운트다운을 반복하는 컨트롤.
    // 표시(coinTimerText)와 입력(getCoinTimerButton)은 UIHud의 것을 사용하고,
    // 코루틴 구동만 UIHud(MonoBehaviour)에 위임한다.
    // 코인 비행 연출(도착 분할 지급 포함)은 UIBottomBar가 담당한다 - 여기서는 출발점만 넘긴다.
    public class CoinGetTimer
    {
        private const float Duration = 900f;        // 타이머 길이(초) = 15분
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
        private readonly GameObject _claimableEffect;

        private readonly RectTransform _coinFlyStart;    // 코인 비행 연출 출발 지점 (cointimer)

        private readonly Slider _slider;                 // 남은 시간을 표시하는 슬라이더
        private readonly Vector3 _timerTextBaseScale;    // 타이머 텍스트 원래 스케일(펄스 복귀 기준)

        private Sequence _claimPulseSeq;                 // "받기" 상태 텍스트 펄스 루프

        private Coroutine _coroutine;
        private bool _claimable; // 카운트다운이 끝나 "받기" 입력을 기다리는 상태인지
        private System.DateTime _endTimeUtc; // 카운트다운 종료(받기 가능) 목표 시각 (UTC, 절대 시간)

        public CoinGetTimer(MonoBehaviour runner, TextMeshProUGUI timerText, Button claimButton,
            RectTransform coinFlyStart = null, Slider slider = null, GameObject claimableEffect = null)
        {
            _runner = runner;
            _timerText = timerText;
            _claimButton = claimButton;
            _claimableEffect = claimableEffect;
            _slider = slider;
            _coinFlyStart = coinFlyStart;
            SetButtonInteractable(false);

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

        // 새 15분 사이클 시작 (첫 실행 / "받기" 이후). 종료 목표 시각을 저장한다.
        public void Begin()
        {
            if (_runner == null)
                return;

            // 기기 시계가 아니라 조작 방어용 신뢰 시각(CoinTimerClock) 기준으로 종료 시각을 잡는다
            _endTimeUtc = CoinTimerClock.UtcNow.AddSeconds(Duration);
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

            double remaining = GetRemainingSecondsAndRebase();
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
            SetButtonInteractable(false);
            if (_claimButton != null)
                _claimButton.onClick.RemoveListener(OnClickClaim);
        }

        private IEnumerator CountdownCoroutine()
        {
            // 남은 시간을 저장된 종료 시각(_endTimeUtc)과 현재 신뢰 시각의 차이로 매 초 계산한다.
            // (게임 일시정지/프레임 변동과 무관하게 실제 시간 기준으로 감소. 시계 조작 방어는 CoinTimerClock 참고)
            while (true)
            {
                double remainingSec = GetRemainingSecondsAndRebase();
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
            if (_claimableEffect != null && _claimableEffect.activeSelf != interactable)
                _claimableEffect.SetActive(interactable);
        }

        private void OnClickClaim()
        {
            // "받기" 상태가 아닐 때(카운트다운 중)의 클릭은 무시
            if (!_claimable)
                return;

            _claimable = false; // 같은 프레임 중복 클릭 방지

            // 지급(코인 저장)과 다음 15분 사이클 시작(종료 시각 저장)을 한 번의 저장으로 묶어 먼저 확정한다.
            // (이전: 새 종료 시각은 즉시 저장, 400코인은 하단 바 비행 연출 도착 시 지급 → 연출 중 종료 시 유실)
            PlayerContext.RunAtomic(() =>
            {
                PlayerContext.AddCoinAmount(RewardCoinAmount);
                Begin(); // 다시 15분 타이머 시작
            });

            // 연출은 표시 전용 - 같은 프레임에 호출해 숫자는 코인 도착에 맞춰 올라간다
            if (UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(_coinFlyStart, RewardCoinAmount);
        }

        // 외부(테스트 등)에서 코인 획득 연출만 단독으로 재생하기 위한 진입점. (지급 없음)
        public void PlayCoinGetEffect()
        {
            if (UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(_coinFlyStart);
        }

        // 남은 초를 "mm:ss"(예: "15:00") 형식으로 표시
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
        // PlayerContext.RunAtomic 안이면 디스크 반영은 묶음 끝에서 한 번에 한다.
        private void SaveEndTime()
        {
            PlayerPrefs.SetString(KeyEndTime, _endTimeUtc.Ticks.ToString());
            CoinTimerClock.PersistLastTrusted();
            if (!PlayerContext.IsInSaveBatch)
                PlayerPrefs.Save();
        }

        // 남은 초를 [0, Duration] 범위로 계산한다. 종료 시각이 Duration 보다 멀리 있으면
        // (기기 시계를 과거로 되돌려 "1455:00" 처럼 길게 잠기는 경우) 지금부터 Duration 으로 다시 잡아 저장한다.
        private double GetRemainingSecondsAndRebase()
        {
            double remaining = ClampRemaining(ref _endTimeUtc, out bool rebased);
            if (rebased)
                SaveEndTime();
            return remaining;
        }

        private static double ClampRemaining(ref System.DateTime endTimeUtc, out bool rebased)
        {
            System.DateTime now = CoinTimerClock.UtcNow;
            double remaining = (endTimeUtc - now).TotalSeconds;
            rebased = false;
            if (remaining > Duration)
            {
                endTimeUtc = now.AddSeconds(Duration);
                remaining = Duration;
                rebased = true;
            }
            return System.Math.Max(0, remaining);
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

        // 저장된 종료 목표 시각 기준 남은 초 ([0, Duration], 0 = 이미 "받기" 가능 상태 - 신뢰 시각 기준).
        // 저장된 타이머가 없으면 false. 인스턴스 없이도 조회할 수 있도록 static -
        // 백그라운드 진입 시 로컬 알림 예약(CoinRewardNotificationScheduler)이 사용한다.
        public static bool TryGetRemainingSeconds(out double remainingSeconds)
        {
            remainingSeconds = 0;

            string s = PlayerPrefs.GetString(KeyEndTime, "");
            if (string.IsNullOrEmpty(s) || !long.TryParse(s, out long ticks))
                return false;

            var endTimeUtc = new System.DateTime(ticks, System.DateTimeKind.Utc);
            remainingSeconds = ClampRemaining(ref endTimeUtc, out bool rebased);
            if (rebased)
            {
                PlayerPrefs.SetString(KeyEndTime, endTimeUtc.Ticks.ToString());
                PlayerPrefs.Save();
            }
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

    // 코인 타이머 전용 "신뢰 시각" - 기기 시계 조작 방어 (서버 시간 없이 가능한 범위).
    // 기준점(앵커) = (그 순간의 UTC, 그 순간의 단조 시계). 신뢰 시각 = 앵커 UTC + (지금 단조 시계 - 앵커 단조 시계).
    // 단조 시계는 기기 시계를 바꿔도 영향을 받지 않으므로, 앵커가 유효한 동안 시계를 앞당기거나 되돌려도 경과 시간이 변하지 않는다.
    //  - Android 실기기: SystemClock.elapsedRealtime (부팅 후 경과 ms, 절전 포함). 앵커를 저장해 두므로
    //    같은 부팅 안에서는 앱을 껐다 켜도 시계 조작이 무시된다. 재부팅(boot_count 변경 또는 단조값 감소)이면 앵커를 새로 잡는다.
    //  - 그 외(에디터/iOS/PC): Time.realtimeSinceStartupAsDouble (프로세스 안에서만 유효). 백그라운드 복귀(포커스 획득) 시
    //    절전 시간을 반영하기 위해 앵커를 다시 잡는다 - 이 경우 백그라운드 중 시계를 앞당긴 것은 막지 못한다(한계).
    //  - 앵커를 새로 잡을 때 기기 시계가 마지막으로 관측한 신뢰 시각보다 과거면(역행) 마지막 신뢰 시각에서 이어 간다.
    //    → 시계를 되돌려도 타이머가 길게 잠기지 않는다 (남은 시간은 CoinGetTimer 에서 [0, Duration] 으로도 한 번 더 자른다).
    internal static class CoinTimerClock
    {
        private const string KeyAnchor = "CoinTimerClockAnchor";         // Android: "앵커UTC ticks|앵커 elapsedRealtime ms|boot_count"
        private const string KeyLastTrusted = "CoinTimerLastTrustedUtc";  // 마지막으로 관측한 신뢰 시각 (UTC ticks)

        private static bool _initialized;
        private static bool _anchorValid;
        private static long _anchorUtcTicks;
        private static double _anchorMonoMs;
        private static long _lastTrustedTicks;

        public static System.DateTime UtcNow
        {
            get
            {
                EnsureInitialized();

                double mono = GetMonotonicMs();
                if (!_anchorValid || mono < _anchorMonoMs)
                {
                    Reanchor(mono, 0);
                }

                long ticks = _anchorUtcTicks + (long)((mono - _anchorMonoMs) * System.TimeSpan.TicksPerMillisecond);
                if (ticks > _lastTrustedTicks)
                    _lastTrustedTicks = ticks;
                return new System.DateTime(ticks, System.DateTimeKind.Utc);
            }
        }

        // 마지막 신뢰 시각을 PlayerPrefs 에 기록한다 (디스크 반영은 호출자가 PlayerPrefs.Save 로)
        public static void PersistLastTrusted()
        {
            if (_lastTrustedTicks > 0)
                PlayerPrefs.SetString(KeyLastTrusted, _lastTrustedTicks.ToString());
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            _initialized = true;

            long.TryParse(PlayerPrefs.GetString(KeyLastTrusted, ""), out _lastTrustedTicks);

            if (UseAndroidElapsedRealtime)
                TryLoadAndroidAnchor();

            Application.focusChanged += OnFocusChanged;
        }

        // 앵커를 새로 잡는다. 기준 UTC = max(기기 UTC, 마지막 신뢰 시각, extraFloorTicks) - 역행 방지.
        private static void Reanchor(double mono, long extraFloorTicks)
        {
            long wall = System.DateTime.UtcNow.Ticks;
            long baseTicks = System.Math.Max(wall, System.Math.Max(_lastTrustedTicks, extraFloorTicks));

            _anchorUtcTicks = baseTicks;
            _anchorMonoMs = mono;
            _anchorValid = true;
            if (baseTicks > _lastTrustedTicks)
                _lastTrustedTicks = baseTicks;

            if (UseAndroidElapsedRealtime)
            {
                PlayerPrefs.SetString(KeyAnchor, $"{_anchorUtcTicks}|{(long)_anchorMonoMs}|{GetAndroidBootCount()}");
                PersistLastTrusted();
                if (!PlayerContext.IsInSaveBatch)
                    PlayerPrefs.Save();
            }
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                // 백그라운드 진입: 마지막 신뢰 시각을 남겨 둔다 (다음 실행의 역행 방지 기준)
                long current = UtcNow.Ticks;
                PersistLastTrusted();
                if (current > 0 && !PlayerContext.IsInSaveBatch)
                    PlayerPrefs.Save();
                return;
            }

            // 포커스 복귀: 프로세스 단조 시계는 절전 시간을 빠뜨릴 수 있어 앵커를 다시 잡는다
            // (Android 는 elapsedRealtime 이 절전을 포함하므로 그대로 둔다)
            if (!UseAndroidElapsedRealtime)
            {
                long current = UtcNow.Ticks;
                Reanchor(GetMonotonicMs(), current);
            }
        }

        // ── 단조 시계 ──

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool UseAndroidElapsedRealtime => _androidClockAvailable;
        private static bool _androidClockAvailable = true;
        private static AndroidJavaClass _systemClock;
        private static int? _bootCount;

        private static double GetMonotonicMs()
        {
            if (_androidClockAvailable)
            {
                try
                {
                    if (_systemClock == null)
                        _systemClock = new AndroidJavaClass("android.os.SystemClock");
                    return _systemClock.CallStatic<long>("elapsedRealtime");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CoinTimerClock] elapsedRealtime 사용 불가 - 프로세스 시계로 대체: {e.Message}");
                    _androidClockAvailable = false;
                    _anchorValid = false;
                }
            }
            return Time.realtimeSinceStartupAsDouble * 1000.0;
        }

        // 부팅 횟수 (Settings.Global.BOOT_COUNT, API 24+). 조회 불가면 -1.
        private static int GetAndroidBootCount()
        {
            if (_bootCount.HasValue)
                return _bootCount.Value;

            _bootCount = -1;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var global = new AndroidJavaClass("android.provider.Settings$Global"))
                {
                    _bootCount = global.CallStatic<int>("getInt", resolver, "boot_count", -1);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CoinTimerClock] boot_count 조회 실패: {e.Message}");
            }
            return _bootCount.Value;
        }

        // 저장된 앵커가 같은 부팅의 것이면 그대로 이어 쓴다 (앱 재시작 후에도 시계 조작 무시)
        private static void TryLoadAndroidAnchor()
        {
            string[] parts = PlayerPrefs.GetString(KeyAnchor, "").Split('|');
            if (parts.Length != 3 ||
                !long.TryParse(parts[0], out long anchorTicks) ||
                !long.TryParse(parts[1], out long anchorMono) ||
                !int.TryParse(parts[2], out int anchorBoot))
                return;

            int boot = GetAndroidBootCount();
            if (boot >= 0 && anchorBoot >= 0 && boot != anchorBoot)
                return; // 재부팅 - 새 앵커

            double mono = GetMonotonicMs();
            if (!_androidClockAvailable || mono < anchorMono)
                return; // 재부팅(단조값 감소) 또는 시계 사용 불가 - 새 앵커

            _anchorUtcTicks = anchorTicks;
            _anchorMonoMs = anchorMono;
            _anchorValid = true;
        }
#else
        private static bool UseAndroidElapsedRealtime => false;

        private static double GetMonotonicMs() => Time.realtimeSinceStartupAsDouble * 1000.0;

        private static int GetAndroidBootCount() => -1;

        private static void TryLoadAndroidAnchor() { }
#endif
    }
}
