using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 데미지 숫자를 표시하는 플로팅 텍스트. SetWorldPosition으로 3D 위치(ally/보스 등)를 HUD 좌표로
    // 변환해 배치한다 - UIActorHPGage와 동일하게 BossCamera.TryConvertWorldToHudPoint를 사용한다.
    public class UIDamageText : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playTimerText;

        private RectTransform _rectTransform;

        // BattleActorDamageControl이 이동/페이드 연출(DOTween)을 재생하기 위해 참조한다.
        public TextMeshProUGUI Text => playTimerText;
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = (RectTransform)transform);

        // delta: 버프로 증감된 양(예: +3/-3). 0이면 그냥 damage만 표시하고, 아니면 "damage(+3)"/"damage(-3)" 형식으로 표시한다.
        public void SetDamage(int damage, int delta = 0)
        {
            if (playTimerText == null)
                return;

            if (delta == 0)
            {
                playTimerText.text = damage.ToString();
                return;
            }

            string sign = delta > 0 ? "+" : string.Empty;
            playTimerText.text = $"{damage}({sign}{delta})";
        }

        // 공격력 강화/방어력 강화 버프 적용 시 데미지 숫자 크기를 키우거나 줄이기 위해 쓴다. (기본 1 = 원래 크기)
        public void SetScale(float scale)
        {
            RectTransform.localScale = Vector3.one * scale;
        }

        // worldPosition(3D)을 HUD 좌표로 변환해 이 위치에 배치한다.
        public void SetWorldPosition(Vector3 worldPosition)
        {
            if (BossCamera.TryConvertWorldToHudPoint(worldPosition, out Vector3 hudPoint))
                RectTransform.position = hudPoint;
        }
    }
}
