using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 코인 받기 타이머 관련 HUD UI(타이머 텍스트 / 받기 버튼 / 코인 비행 연출)를 담당하는 컴포넌트.
    // UIHud에서 코인 타이머 로직만 분리해 한곳에서 관리한다.
    public class UICoinTimerHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI coinTimerText;
        [SerializeField] Button getCoinTimerButton;
        [SerializeField] Slider coinTimerSlider; // 남은 시간 표시 슬라이더 (coinTImerSlider)

        [Header("코인 획득 연출")]
        [SerializeField] RectTransform coinFlyStart;    // 코인이 출발하는 지점
        // (도착점/템플릿은 UIBottomBar 프리팹이 보유 - 연출 재생도 UIBottomBar가 담당)

        // 5분마다 코인을 받는 타이머 (coinTimerText/getCoinTimerButton 사용)
        private CoinGetTimer _coinGetTimer;

        private void Start()
        {
            // 코인 받기 타이머 (5분 카운트다운 → "받기" → 클릭 시 코인 지급 후 재시작)
            // Resume: 재실행 시 저장된 상태 복원 (없으면 새로 시작)
            _coinGetTimer = new CoinGetTimer(this, coinTimerText, getCoinTimerButton,
                coinFlyStart, coinTimerSlider);
            _coinGetTimer.Resume();
        }

        // 외부(테스트/버튼 등)에서 코인 획득 연출만 단독으로 재생하기 위한 진입점.
        public void OnTestPlayCoinGetEffect()
        {
            _coinGetTimer?.PlayCoinGetEffect();
        }

        private void OnDestroy()
        {
            _coinGetTimer?.Dispose();
        }
    }
}
