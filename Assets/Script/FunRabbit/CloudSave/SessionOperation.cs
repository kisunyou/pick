using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // Account restore must suspend physics, reward animations and input together.
    // Nested operations retain the original time scale and only resume tweens
    // which this guard actually paused.
    public static class SessionOperation
    {
        static int _count;
        static int _dimCount;
        static float _timeScale;
        static GameObject _screen;
        static Image _blocker;
        static List<Tween> _pausedTweens;
        static GameObject _retry;
        static TMP_FontAsset OperationFont => Resources.Load<TMP_FontAsset>("Font/teenyfont_20250801a") ?? TMP_Settings.defaultFontAsset;
        public static bool IsBusy => _count > 0;

        public static IDisposable Begin(bool dimBackground = true)
        {
            if (_count++ == 0)
            {
                _timeScale = Time.timeScale;
                Time.timeScale = 0;
                if (UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                _pausedTweens = DOTween.PlayingTweens();
                if (_pausedTweens != null)
                    foreach (Tween tween in _pausedTweens) tween.Pause();

                _screen = new GameObject("Account operation", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(_screen);
                var canvas = _screen.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
                var scaler = _screen.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                var panel = new GameObject("Block input", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(_screen.transform, false);
                var rect = panel.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                _blocker = panel.GetComponent<Image>();
                _blocker.raycastTarget = true;
                var label = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(panel.transform, false);
                var labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(.1f, .4f); labelRect.anchorMax = new Vector2(.9f, .6f);
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                var text = label.GetComponent<TextMeshProUGUI>();
                text.font = OperationFont;
                text.fontSize = 36; text.alignment = TextAlignmentOptions.Center;
                text.text = string.Empty;
                text.raycastTarget = false;
            }
            if (dimBackground) _dimCount++;
            RefreshBackground();
            return new Lease(dimBackground);
        }

        static void RefreshBackground()
        {
            // A transparent Image still blocks input while the initial loading screen remains visible.
            if (_blocker != null)
                _blocker.color = new Color(0, 0, 0, _dimCount > 0 || _retry != null ? .65f : 0f);
        }

        public static void ShowRetry(Action retry, string bodyKey = "account_sync_retry_body", string buttonKey = "account_sync_retry")
        {
            HideRetry();
            if (_screen == null) return;
            _screen.GetComponentInChildren<TextMeshProUGUI>().text = LanguageManager.Instance.Get(bodyKey);
            _retry = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button));
            RefreshBackground();
            _retry.transform.SetParent(_screen.transform, false);
            var rect = _retry.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.25f, .28f); rect.anchorMax = new Vector2(.75f, .35f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _retry.GetComponent<Image>().color = new Color(.15f, .45f, .7f);
            _retry.GetComponent<Button>().onClick.AddListener(() => { HideRetry(); retry?.Invoke(); });
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(_retry.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = label.GetComponent<TextMeshProUGUI>();
            text.font = OperationFont; text.fontSize = 32;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            text.text = LanguageManager.Instance.Get(buttonKey);
        }

        public static void HideRetry()
        {
            if (_retry != null) UnityEngine.Object.Destroy(_retry);
            _retry = null;
            RefreshBackground();
            if (_screen != null)
                _screen.GetComponentInChildren<TextMeshProUGUI>().text = string.Empty;
        }

        sealed class Lease : IDisposable
        {
            bool _disposed;
            readonly bool _dimBackground;

            public Lease(bool dimBackground) { _dimBackground = dimBackground; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_dimBackground) _dimCount--;
                if (--_count != 0)
                {
                    RefreshBackground();
                    return;
                }
                UnityEngine.Object.Destroy(_screen);
                _screen = null;
                _blocker = null;
                _retry = null;
                Time.timeScale = _timeScale;
                if (_pausedTweens != null)
                    foreach (Tween tween in _pausedTweens)
                        if (tween != null && tween.IsActive()) tween.Play();
                _pausedTweens = null;
            }
        }
    }
}
