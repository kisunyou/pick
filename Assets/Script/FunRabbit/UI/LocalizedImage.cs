using System;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [RequireComponent(typeof(Image))]
    public sealed class LocalizedImage : MonoBehaviour
    {
        [Serializable]
        public sealed class Variant
        {
            public LanguageType language;
            public Sprite sprite;
            public bool overrideAnchors;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
        }

        [SerializeField] private Variant[] variants;
        [SerializeField] private LanguageType fallbackLanguage = LanguageType.ENG;
        private Image _image;
        private LanguageManager _languageManager;

        private void OnEnable()
        {
            _languageManager = LanguageManager.Instance;
            _languageManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (_languageManager != null) _languageManager.OnLanguageChanged -= Refresh;
            _languageManager = null;
        }

        private void Refresh()
        {
            if (_languageManager != null) ApplyLanguage(_languageManager.CurrentLanguage);
        }

        public bool ApplyLanguage(LanguageType language)
        {
            Variant selected = Find(language) ?? Find(fallbackLanguage);
            if (selected == null && variants != null)
                foreach (Variant variant in variants)
                    if (variant != null && variant.sprite != null) { selected = variant; break; }
            if (selected == null) return false;
            if (_image == null) _image = GetComponent<Image>();
            _image.sprite = selected.sprite;
            if (selected.overrideAnchors)
            {
                _image.rectTransform.anchorMin = selected.anchorMin;
                _image.rectTransform.anchorMax = selected.anchorMax;
                _image.rectTransform.offsetMin = _image.rectTransform.offsetMax = Vector2.zero;
            }
            return true;
        }

        private Variant Find(LanguageType language)
        {
            if (variants != null)
                foreach (Variant variant in variants)
                    if (variant != null && variant.language == language && variant.sprite != null) return variant;
            return null;
        }
    }
}
