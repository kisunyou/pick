using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 화면 상단에 잠깐 떴다 사라지는 토스트 메시지.
    // - 크기는 프리팹의 HorizontalLayoutGroup + ContentSizeFitter가 텍스트에 맞춰 조절한다
    // - 표시 후 DisplayDuration 유지 → FadeDuration 동안 알파 페이드아웃 → 자동 닫힘
    [UIOption(
        Path = "UI2/Prefabs/UITopMessage",
        Layer = UILayer.System,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UITopMessage : BaseUIView<UITopMessage>
    {
        const float DisplayDuration = 3f;   // 노출 유지 시간(초)
        const float FadeDuration = 1f;      // 페이드아웃 시간(초)
        const float AppearDuration = 0.35f; // 등장 스케일 연출 시간(초)
        const float AppearStartScale = 0.5f; // 등장 시작 스케일 (여기서 1.0으로 튕기며 커진다)

        [SerializeField] TextMeshProUGUI _messageText;
        [SerializeField] CanvasGroup _canvasGroup;

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

            // 크기 조절은 프리팹의 HorizontalLayoutGroup + ContentSizeFitter 담당.
            // 표시 첫 프레임부터 올바른 크기가 되도록 레이아웃을 즉시 갱신한다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_messageText.rectTransform.parent as RectTransform);

            PlayShowAndFadeOut();
        }

        // 등장(튕기는 스케일) → DisplayDuration 대기 → FadeDuration 동안 알파 0 → 닫기
        private void PlayShowAndFadeOut()
        {
            _fadeSequence?.Kill();
            _canvasGroup.alpha = 1f;

            // 메시지 박스(레이아웃 루트)가 살짝 튕기며 커지는 등장 연출 (OutBack = 오버슈트 후 복귀)
            Transform messageBox = _messageText.rectTransform.parent;
            messageBox.localScale = Vector3.one * AppearStartScale;

            _fadeSequence = DOTween.Sequence()
                .Append(messageBox.DOScale(1f, AppearDuration).SetEase(Ease.OutBack))
                .AppendInterval(DisplayDuration)
                .Append(_canvasGroup.DOFade(0f, FadeDuration))
                .OnComplete(() => Close());
        }
    }
}
