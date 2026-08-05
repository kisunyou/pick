using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 버프 아이콘 1개 (BattleActorBuffManager가 buffType으로 찾아 SetCount를 호출한다).
    // 개수가 0이면 스스로를 숨긴다. 첫 표시/증가 시에는 스케일 애니 없이 바로 반영하고,
    // 그 외(동일/감소)에는 DOTween 펀치 스케일 애니로 변경을 강조한다.
    public class UIBuffIcon : MonoBehaviour
    {
        [SerializeField] private BuffType buffType;
        [SerializeField] private TextMeshProUGUI itemCountText;

        private const float PunchScale = 0.3f;
        private const float PunchDuration = 0.2f;

        private int? _previousCount;

        public BuffType BuffType => buffType;

        public void SetCount(int count)
        {
            bool isFirstSet = !_previousCount.HasValue;
            bool isIncrease = _previousCount.HasValue && count > _previousCount.Value;
            _previousCount = count;

            gameObject.SetActive(count > 0);
            if (count <= 0)
                return;

            if (itemCountText != null)
                itemCountText.text = count.ToString();

            if (!isFirstSet && !isIncrease)
                PlayCountScaleAnim();
        }

        private void PlayCountScaleAnim()
        {
            if (itemCountText == null)
                return;

            Transform textTransform = itemCountText.transform;
            textTransform.DOKill();
            textTransform.localScale = Vector3.one;
            textTransform.DOPunchScale(Vector3.one * PunchScale, PunchDuration, 1, 0f);
        }
    }
}
