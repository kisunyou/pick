using UnityEngine;

namespace FunRabbit
{
    [DisallowMultipleComponent]
    public sealed class UITopbarSafeArea : MonoBehaviour
    {
        RectTransform _rect;
        Vector2 _offsetMin, _offsetMax;
        Vector2 _lastScreen, _lastParent;
        Rect _lastSafe;
        bool _dirty = true;

        void OnEnable() => _dirty = true;
        void OnRectTransformDimensionsChange() => _dirty = true;

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            var screen = new Vector2(Screen.width, Screen.height);
            if (!_dirty && screen == _lastScreen && parent.rect.size == _lastParent && Screen.safeArea == _lastSafe) return;
            ApplyScreenLayout(screen, Screen.safeArea);
            _lastScreen = screen;
            _lastParent = parent.rect.size;
            _lastSafe = Screen.safeArea;
            _dirty = false;
        }

        public void ApplyScreenLayout(Vector2 screen, Rect safe)
        {
            var parent = transform.parent as RectTransform;
            if (parent == null || screen.x <= 0 || screen.y <= 0) return;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(Vector2.zero, screen);
            Vector2 size = parent.rect.size;
            ApplyInsets(Mathf.Clamp(safe.xMin, 0, screen.x) * size.x / screen.x,
                Mathf.Clamp(screen.y - safe.yMax, 0, screen.y) * size.y / screen.y,
                Mathf.Clamp(screen.x - safe.xMax, 0, screen.x) * size.x / screen.x);
        }

        public void ApplyInsets(float left, float top, float right)
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
                _offsetMin = _rect.offsetMin;
                _offsetMax = _rect.offsetMax;
            }
            // Reuse the authored collection-header position; never accumulate safe-area offsets.
            _rect.offsetMin = _offsetMin + new Vector2(left, -top);
            _rect.offsetMax = _offsetMax + new Vector2(-right, -top);
        }
    }
}
