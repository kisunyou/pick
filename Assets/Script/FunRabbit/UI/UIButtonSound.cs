using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // 버튼 클릭 공통 효과음(select) 일괄 바인딩 유틸.
    //
    // BaseUIView가 Awake/Show 시점에 BindAll을 호출하므로, 모든 UI 뷰 계층 아래의
    // 버튼에는 클릭음이 자동으로 붙는다 (개별 버튼/뷰에서 할 일 없음).
    // 뷰가 열린 뒤 런타임에 동적으로 생성한 버튼만 UIButtonSound.Bind()를 직접 호출하면 된다.
    public static class UIButtonSound
    {
        const string SelectSoundName = "game_sounds/ui/select";

        // root 아래 모든 버튼(비활성 포함)에 클릭 효과음을 바인딩한다. (이미 바인딩된 버튼은 무시)
        public static void BindAll(GameObject root)
        {
            if (root == null)
                return;

            foreach (var button in root.GetComponentsInChildren<Button>(true))
                Bind(button);
        }

        // 버튼 하나에 클릭 효과음을 바인딩한다. (마커 컴포넌트로 중복 바인딩 방지)
        public static void Bind(Button button)
        {
            if (button == null || button.GetComponent<UIButtonSoundBinder>() != null)
                return;

            button.gameObject.AddComponent<UIButtonSoundBinder>();
            button.onClick.AddListener(PlaySelectSound);
        }

        private static void PlaySelectSound()
        {
            var audio = AudioManager.Instance;
            if (audio != null)
                audio.PlaySfx(SelectSoundName);
        }
    }

    // 바인딩 여부 표시용 마커 (UIButtonSound.Bind가 부착한다 - 직접 붙일 필요 없음)
    public sealed class UIButtonSoundBinder : MonoBehaviour { }
}
