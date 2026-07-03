using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 5분 카운트다운 → "받기" → 버튼 클릭 시 코인 지급 후 다시 5분 카운트다운을 반복하는 컨트롤.
    // 표시(coinTimerText)와 입력(getCoinTimerButton)은 UIHud의 것을 사용하고,
    // 코루틴 구동만 UIHud(MonoBehaviour)에 위임한다.
    public class CoinGetTimer
    {
        private const float Duration = 300f;        // 타이머 길이(초) = 5분
        private const long RewardCoinAmount = 100;  // "받기" 시 지급할 코인
        private const string ClaimLabel = "받기";   // 0:00 도달 후 표시할 문구

        // 코인 획득 연출 설정 (UIMissionClearPanel의 코인 비행 연출과 동일한 방식)
        private const int CoinFlyCount = 8;              // 한 번에 날아가는 코인 개수
        private const float CoinFlyDuration = 0.6f;      // 코인 1개 비행 시간(초)
        private const float CoinFlyInterval = 0.08f;     // 코인 발사 간격(초)
        private const float CoinCurveHeightRatio = 0.4f; // 베지어 곡선 높이(비행 거리 대비 비율)
        private const float CoinBounceScale = 1.3f;      // 도착 지점이 커지는 배율(이후 원래 크기로 복귀)
        private const float CoinBounceDuration = 0.15f;  // 도착 지점 바운스 각 구간 시간(초)

        // "받기" 가능 상태 텍스트 펄스 연출 설정
        private const float ClaimPulseScale = 1.15f;     // 텍스트가 커지는 배율
        private const float ClaimPulseDuration = 0.35f;  // 커졌다 / 작아지는 각 구간 시간(초)
        private const float ClaimPulseInterval = 0.4f;   // 한 번 펄스 후 다음 펄스까지 간격(초)

        private readonly MonoBehaviour _runner;
        private readonly TextMeshProUGUI _timerText;
        private readonly Button _claimButton;

        private readonly RectTransform _coinFlyStart;    // 출발 지점 (cointimer)
        private readonly RectTransform _coinFlyTarget;   // 도착 지점 (coinImage)
        private readonly RectTransform _coinFlyTemplate; // 날아가는 코인 템플릿 (effectCoin)
        private readonly Vector3 _targetBaseScale;       // 도착 지점 원래 스케일(바운스 복귀 기준)

        private readonly Slider _slider;                 // 남은 시간을 표시하는 슬라이더
        private readonly Vector3 _timerTextBaseScale;    // 타이머 텍스트 원래 스케일(펄스 복귀 기준)

        private Sequence _coinSeq;
        private Tween _coinBounceTween;
        private Sequence _claimPulseSeq;                 // "받기" 상태 텍스트 펄스 루프

        private Coroutine _coroutine;
        private bool _claimable; // 카운트다운이 끝나 "받기" 입력을 기다리는 상태인지

        public CoinGetTimer(MonoBehaviour runner, TextMeshProUGUI timerText, Button claimButton,
            RectTransform coinFlyStart = null, RectTransform coinFlyTarget = null, RectTransform coinFlyTemplate = null,
            Slider slider = null)
        {
            _runner = runner;
            _timerText = timerText;
            _claimButton = claimButton;
            _slider = slider;

            _coinFlyStart = coinFlyStart;
            _coinFlyTarget = coinFlyTarget;
            _coinFlyTemplate = coinFlyTemplate;

            if (_coinFlyTarget != null)
                _targetBaseScale = _coinFlyTarget.localScale;

            if (_timerText != null)
                _timerTextBaseScale = _timerText.transform.localScale;

            // 템플릿은 복제 원본이므로 화면에 보이지 않게 숨겨둔다.
            if (_coinFlyTemplate != null)
                _coinFlyTemplate.gameObject.SetActive(false);

            if (_claimButton != null)
                _claimButton.onClick.AddListener(OnClickClaim);
        }

        // 타이머 시작 (이미 동작 중이면 처음(05:00)부터 다시 시작)
        public void Begin()
        {
            if (_runner == null)
                return;

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
            Stop();
            _coinSeq?.Kill();
            _coinBounceTween?.Kill();
            _claimPulseSeq?.Kill();
            if (_claimButton != null)
                _claimButton.onClick.RemoveListener(OnClickClaim);
        }

        private IEnumerator CountdownCoroutine()
        {
            int remaining = Mathf.CeilToInt(Duration);

            // 05:00 → 00:01 까지 매 초 갱신
            while (remaining > 0)
            {
                UpdateTimerText(remaining);
                UpdateSlider(remaining);
                yield return new WaitForSeconds(1f);
                remaining--;
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
                _timerText.text = ClaimLabel;

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

            PlayerContext.AddCoinAmount(RewardCoinAmount);
            PlayCoinGetEffect(); // 코인이 출발점에서 도착점으로 날아가는 연출
            Begin(); // 다시 5분 타이머 시작
        }

        // 코인 획득 연출: 여러 개의 코인이 출발점(cointimer)에서 도착점(coinImage)으로
        // 베지어 곡선을 그리며 날아가고, 도착할 때마다 도착 지점이 젤리처럼 바운스한다.
        // 외부(UIHud 등)에서도 연출만 단독으로 재생할 수 있도록 public으로 노출.
        public void PlayCoinGetEffect()
        {
            if (_coinFlyStart == null || _coinFlyTarget == null || _coinFlyTemplate == null)
                return;

            _coinSeq?.Kill();
            _coinSeq = DOTween.Sequence().SetUpdate(true);
            for (int i = 0; i < CoinFlyCount; i++)
            {
                _coinSeq.Insert(i * CoinFlyInterval, CreateCoinFly());
            }
        }

        // 코인 1개 비행 트윈 생성 (2차 베지어 곡선)
        private Tween CreateCoinFly()
        {
            RectTransform coin = Object.Instantiate(_coinFlyTemplate, _coinFlyTemplate.parent);
            coin.SetAsLastSibling();
            coin.gameObject.SetActive(true);

            Vector3 p0 = _coinFlyStart.position;                  // 시작(월드)
            Vector3 p2 = _coinFlyTarget.position;                 // 도착(월드)
            Vector3 mid = (p0 + p2) * 0.5f;
            float distance = Vector3.Distance(p0, p2);
            Vector3 p1 = mid + Vector3.up * (distance * CoinCurveHeightRatio); // 위로 볼록한 제어점

            coin.position = p0;

            float t = 0f;
            return DOTween.To(() => t, x =>
                {
                    t = x;
                    float u = 1f - t;
                    coin.position = u * u * p0 + 2f * u * t * p1 + t * t * p2; // 2차 베지어
                }, 1f, CoinFlyDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (coin != null)
                        Object.Destroy(coin.gameObject);
                    BounceTargetCoin();
                });
        }

        // 도착 지점 이미지가 커졌다가 다시 원래 크기로 돌아오는 연출
        private void BounceTargetCoin()
        {
            if (_coinFlyTarget == null)
                return;

            _coinBounceTween?.Kill();
            _coinFlyTarget.localScale = _targetBaseScale;
            _coinBounceTween = _coinFlyTarget
                .DOScale(_targetBaseScale * CoinBounceScale, CoinBounceDuration)
                .SetLoops(2, LoopType.Yoyo)   // 커짐 → 원래 크기 복귀
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
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
