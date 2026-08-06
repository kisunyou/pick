using TMPro;
using UnityEngine;

namespace FunRabbit
{
    // 프리팹에 고정 텍스트로 박혀있던 라벨(버튼 문구, 타이틀 등)을 다국어로 표시한다.
    // key에 해당하는 문자열을 LanguageManager에서 가져와 표시하고, 언어가 바뀌면 자동으로 갱신한다.
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        private TMP_Text _text;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Apply();
            LanguageManager.Instance.OnLanguageChanged += Apply;
        }

        private void OnDisable()
        {
            if (LanguageManager.IsCheckInstance())
                LanguageManager.Instance.OnLanguageChanged -= Apply;
        }

        private void Apply()
        {
            if (_text != null)
                _text.text = LanguageManager.Instance.Get(key);
        }
    }
}
