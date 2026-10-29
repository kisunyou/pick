using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FunRabbit
{
    [UIOption(Path = "UI2/Prefabs/UIMissionCompleteMessage", Layer = UILayer.Message,
        OpenMode = UIOpenMode.Single, isPool = false)]
    public sealed class UIMissionCompleteMessage : BaseUIView<UIMissionCompleteMessage>
    {
        [SerializeField] RectTransform banner;
        [SerializeField] CanvasGroup canvasGroup;
        [SerializeField] TextMeshProUGUI messageText;
        Sequence _sequence;

        public static void ShowMessage()
        {
            var view = CreateOrGet();
            if (view != null) view.PlayMessage(LanguageManager.Instance.Get("mission_complete_message"));
        }

        public void PlayMessage(string message)
        {
            _sequence?.Kill();
            messageText.text = message;
            canvasGroup.alpha = 1;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            var safeArea = banner.GetComponent<UITopbarSafeArea>();
            if (safeArea != null) safeArea.ApplyScreenLayout(new Vector2(Screen.width, Screen.height), Screen.safeArea);
            banner.localScale = Vector3.one * .7f;
            _sequence = DOTween.Sequence().SetUpdate(true)
                .Append(banner.DOScale(1f, .45f).SetEase(Ease.OutBack))
                .AppendInterval(1.5f)
                .Append(canvasGroup.DOFade(0, .3f))
                .OnComplete(Close);
        }

        public override void OnClose() => StopAnimation();
        void OnDisable() => StopAnimation();
        void StopAnimation()
        {
            _sequence?.Kill();
            _sequence = null;
            if (banner != null) banner.localScale = Vector3.one;
        }
    }
}
