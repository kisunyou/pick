using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        private readonly MonoBehaviour _runner;
        private readonly TextMeshProUGUI _timerText;
        private readonly Button _claimButton;

        private Coroutine _coroutine;
        private bool _claimable; // 카운트다운이 끝나 "받기" 입력을 기다리는 상태인지

        public CoinGetTimer(MonoBehaviour runner, TextMeshProUGUI timerText, Button claimButton)
        {
            _runner = runner;
            _timerText = timerText;
            _claimButton = claimButton;

            if (_claimButton != null)
                _claimButton.onClick.AddListener(OnClickClaim);
        }

        // 타이머 시작 (이미 동작 중이면 처음(05:00)부터 다시 시작)
        public void Begin()
        {
            if (_runner == null)
                return;

            Stop();
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
                yield return new WaitForSeconds(1f);
                remaining--;
            }

            // 0:00 도달: "00:00"을 한 번 보여준 뒤 "받기" 대기 상태로 진입
            UpdateTimerText(0);
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
            Begin(); // 다시 5분 타이머 시작
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
    }
}
