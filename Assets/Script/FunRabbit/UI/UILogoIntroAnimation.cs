using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    public sealed class UILogoIntroAnimation : MonoBehaviour
    {
        [SerializeField, Min(.01f)] private float duration = .95f;
        private Vector3 _baseScale;
        private bool _capturedBaseScale;
        private bool _playing;
        private float _elapsed;
        private Image _image;
        private System.Action _onComplete;

        private void OnEnable() => Hide();
        private void OnDisable() => Hide();

        public void Hide()
        {
            Stop();
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_image != null) _image.enabled = visible;
        }

        private void Update()
        {
            // Account synchronization pauses DOTween globally; the loading logo must keep animating.
            Advance(Time.unscaledDeltaTime);
        }

        public void Play(System.Action onComplete = null)
        {
            if (!_capturedBaseScale)
            {
                _baseScale = transform.localScale;
                _capturedBaseScale = true;
            }
            _elapsed = 0f;
            _onComplete = onComplete;
            _playing = true;
            transform.localScale = Vector3.Scale(_baseScale, EvaluateScale(0f));
            SetVisible(true);
        }

        public void Stop()
        {
            _playing = false;
            _onComplete = null;
            if (_capturedBaseScale) transform.localScale = _baseScale;
        }

        public void Advance(float deltaTime)
        {
            if (!_playing || deltaTime <= 0f || float.IsNaN(deltaTime)) return;
            _elapsed += deltaTime;
            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(.01f, duration));
            transform.localScale = Vector3.Scale(_baseScale, EvaluateScale(progress));
            if (progress >= 1f)
            {
                var completed = _onComplete;
                Stop();
                completed?.Invoke();
            }
        }

        public static Vector3 EvaluateScale(float progress)
        {
            float t = Mathf.Clamp01(progress);
            if (t <= .30f)
                return Vector3.LerpUnclamped(Vector3.one * .7f, Vector3.one * 1.4f,
                    1f - Mathf.Pow(1f - t / .30f, 3f));
            if (t <= .53f)
                return Blend(Vector3.one * 1.4f, new Vector3(1.10f, .89f, 1f), (t - .30f) / .23f);
            if (t <= .70f)
                return Blend(new Vector3(1.10f, .89f, 1f), new Vector3(.96f, 1.06f, 1f), (t - .53f) / .17f);
            if (t <= .85f)
                return Blend(new Vector3(.96f, 1.06f, 1f), new Vector3(1.025f, .98f, 1f), (t - .70f) / .15f);
            return Blend(new Vector3(1.025f, .98f, 1f), Vector3.one, (t - .85f) / .15f);
        }

        private static Vector3 Blend(Vector3 from, Vector3 to, float t) =>
            Vector3.LerpUnclamped(from, to, Mathf.SmoothStep(0f, 1f, t));
    }
}
