using TMPro;
using UnityEngine;
using DG.Tweening;

namespace FunRabbit
{
    // 화면 상단에 잠깐 떴다 사라지는 토스트 메시지.
    // - 배경 이미지(루트 Image)는 messageText 길이에 맞춰 가변으로 늘어난다
    // - 표시 후 DisplayDuration 유지 → FadeDuration 동안 알파 페이드아웃 → 자동 닫힘
    [UIOption(
        Path = "UI2/Prefabs/UITopMessage",
        Layer = UILayer.System,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UITopMessage : BaseUIView<UITopMessage>
    {
        const float DisplayDuration = 3f; // 노출 유지 시간(초)
        const float FadeDuration = 1f;    // 페이드아웃 시간(초)

        [SerializeField] TextMeshProUGUI _messageText;
        [SerializeField] CanvasGroup _canvasGroup;

        // 텍스트 주변 배경 여백 (가로, 세로 합계)
        [SerializeField] Vector2 _backgroundPadding = new Vector2(80f, 50f);

        Sequence _fadeSequence;

        // 표시 진입점 - 이미 떠 있으면 내용 교체 + 타이머 재시작
        public static void ShowMessage(string message)
        {
            UITopMessage view = CreateOrGet();
            if (view != null)
                view.SetMessage(message);
        }

        public override void OnClose()
        {
            _fadeSequence?.Kill();
        }

        public void SetMessage(string message)
        {
            _messageText.text = message;

            // 텍스트 크기에 맞춰 배경 이미지를 가변으로 늘린다
            Vector2 textSize = _messageText.GetPreferredValues(message);
            _messageText.rectTransform.sizeDelta = textSize;
            ((RectTransform)transform).sizeDelta = textSize + _backgroundPadding;

            PlayShowAndFadeOut();
        }

        // 표시 → DisplayDuration 대기 → FadeDuration 동안 알파 0 → 닫기
        private void PlayShowAndFadeOut()
        {
            _fadeSequence?.Kill();
            _canvasGroup.alpha = 1f;

            _fadeSequence = DOTween.Sequence()
                .AppendInterval(DisplayDuration)
                .Append(_canvasGroup.DOFade(0f, FadeDuration))
                .OnComplete(() => Close());
        }
    }
}
