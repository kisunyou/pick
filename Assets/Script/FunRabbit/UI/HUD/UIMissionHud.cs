using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 미션 관련 HUD UI(제목 / 아이콘 / 진행 슬라이더)를 담당하는 컴포넌트.
    // UIHud에서 미션 표시 로직만 분리해 한곳에서 관리한다.
    public class UIMissionHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI missionTitleText;
        [SerializeField] Transform missionIconTransform;
        [SerializeField] Slider missionSlider;
        [SerializeField] TextMeshProUGUI missionSliderText;

        // 현재 로드된 미션 아이콘 프리팹 경로 (중복 로드 체크용)
        private string _currentMissionIconPath;

        // 미션 진행도 갱신 시 아이콘 강조 연출 설정
        private const float IconPunchScale = 1.3f;      // 원래 크기 대비 커지는 배율
        private const float IconPunchDuration = 0.15f;  // 커졌다 / 작아지는 각 구간 시간

        private Vector3 _iconBaseScale = Vector3.one;   // 아이콘의 원래 스케일 (복구 기준)
        private Tween _iconPunchTween;

        private void Awake()
        {
            if (missionIconTransform != null)
                _iconBaseScale = missionIconTransform.localScale;
        }

        public void SetMissionTitle(string title)
        {
            if (missionTitleText != null)
                missionTitleText.text = title;
        }

        public void UpdateMissionProgressText(int current, int total)
        {
            missionSliderText.text = $"{current} / {total}";
            missionSlider.value = total > 0 ? (float)current / total : 0f;

            PlayIconPunch();
        }

        // 미션 진행도가 갱신될 때 아이콘을 살짝 커졌다가 원래 크기로 되돌려 강조한다.
        private void PlayIconPunch()
        {
            if (missionIconTransform == null)
                return;

            // 연속 호출 시 스케일이 누적/왜곡되지 않도록 기존 연출을 정리하고 원래 스케일에서 다시 시작
            _iconPunchTween?.Kill();
            missionIconTransform.localScale = _iconBaseScale;

            // 원래 크기 → 커졌다가(IconPunchScale) → 다시 원래 크기 (제자리 펄스)
            _iconPunchTween = missionIconTransform
                .DOScale(_iconBaseScale * IconPunchScale, IconPunchDuration)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo);
        }

        public void SetMissionIcon(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("[UIMissionHud] SetMissionIcon: prefabPath가 비어있습니다.");
                return;
            }

            if (missionIconTransform == null)
            {
                Debug.LogError("[UIMissionHud] SetMissionIcon: missionIconTransform이 할당되지 않았습니다.");
                return;
            }

            // 이미 동일한 아이콘이 로드되어 있으면 스킵
            if (_currentMissionIconPath == prefabPath && missionIconTransform.childCount > 0)
                return;

            // 기존 자식 제거
            for (int i = missionIconTransform.childCount - 1; i >= 0; i--)
            {
                Destroy(missionIconTransform.GetChild(i).gameObject);
            }
            _currentMissionIconPath = null;

            // 프리팹 로드
            GameObject prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[UIMissionHud] SetMissionIcon: 프리팹 로드 실패: {prefabPath}");
                return;
            }

            Instantiate(prefab, missionIconTransform);
            _currentMissionIconPath = prefabPath;
        }

        private void OnDestroy()
        {
            _iconPunchTween?.Kill();
        }
    }
}
