using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 늘어날 때(회복)는 whiteFillImage가 즉시 반영되고 fillImage가 뒤따라가며,
    // 줄어들 때(피격)는 반대로 fillImage가 즉시 반영되고 whiteFillImage가 뒤따라간다.
    // (뒤따라가는 쪽은 지연 후 선형으로 목표값까지 이동한다). 단, 최초 SetGage 호출은 따라갈
    // 이전 값이 없으므로 두 이미지 모두 지연 없이 바로 반영한다. whiteFillImage가 없으면
    // 2단 연출 없이 fillImage만 즉시 반영하는 단일 게이지로 동작한다.
    public class UICustumGage : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private Image whiteFillImage; // 2단 연출이 필요 없는 게이지도 있어 비어있을 수 있다

        [SerializeField] private float catchUpDelay = 0.5f; // 뒤따라가는 이미지가 움직이기 시작하기까지의 대기 시간(초)
        [SerializeField] private float catchUpSpeed = 0.5f; // 뒤따라가는 이미지의 초당 fillAmount 이동 속도(선형)

        private Image _catchUpImage; // 이번에 뒤따라가는 이미지 (증가면 fillImage, 감소면 whiteFillImage)
        private float _targetRatio;
        private float _delayTimer;
        private bool _isCatchingUp;
        private bool _hasSetOnce;
        private float _currentRatio;

        private void Update()
        {
            if (!_isCatchingUp || _catchUpImage == null)
                return;

            if (_delayTimer > 0f)
            {
                _delayTimer -= Time.deltaTime;
                return;
            }

            _catchUpImage.fillAmount = Mathf.MoveTowards(_catchUpImage.fillAmount, _targetRatio, catchUpSpeed * Time.deltaTime);

            if (Mathf.Approximately(_catchUpImage.fillAmount, _targetRatio))
                _isCatchingUp = false;
        }

        public void SetGage(float ratio)
        {
            if (whiteFillImage == null)
            {
                // 2단 연출용 이미지가 없으면 단일 게이지로 - 즉시 반영만 한다.
                if (fillImage != null)
                    fillImage.fillAmount = ratio;

                _currentRatio = ratio;
                _hasSetOnce = true;
                return;
            }

            if (!_hasSetOnce)
            {
                // 최초 설정은 따라갈 이전 값이 없으므로 두 이미지 모두 지연 없이 바로 반영한다.
                if (fillImage != null)
                    fillImage.fillAmount = ratio;
                whiteFillImage.fillAmount = ratio;

                _isCatchingUp = false;
                _hasSetOnce = true;
                _currentRatio = ratio;
                return;
            }

            if (ratio > _currentRatio)
                SnapAndCatchUp(snapImage: whiteFillImage, catchUpImage: fillImage, ratio); // 증가(회복)
            else if (ratio < _currentRatio)
                SnapAndCatchUp(snapImage: fillImage, catchUpImage: whiteFillImage, ratio); // 감소(피격)

            _currentRatio = ratio;
        }

        private void SnapAndCatchUp(Image snapImage, Image catchUpImage, float ratio)
        {
            if (snapImage != null)
                snapImage.fillAmount = ratio;

            _catchUpImage = catchUpImage;
            _targetRatio = ratio;
            _delayTimer = catchUpDelay;
            _isCatchingUp = true;
        }

//#if UNITY_EDITOR || DEVELOPMENT_BUILD
//        private string _debugRatioText = "0.5";

//        // SetGage 동작 확인용 디버그 도구 - 텍스트박스에 ratio를 입력하고 버튼을 누르면 SetGage를 호출한다.
//        private void OnGUI()
//        {
//            GUILayout.BeginArea(new Rect(10, 10, 200, 30));
//            GUILayout.BeginHorizontal();
//            _debugRatioText = GUILayout.TextField(_debugRatioText, GUILayout.Width(60));
//            if (GUILayout.Button("SetGage 테스트"))
//            {
//                if (float.TryParse(_debugRatioText, out float ratio))
//                    SetGage(ratio);
//            }
//            GUILayout.EndHorizontal();
//            GUILayout.EndArea();
//        }
//#endif
    }
}
