using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UILoading",
        Layer = UILayer.System,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UILoading : BaseUIView<UILoading>
    {
        [SerializeField] TextMeshProUGUI _loadText;

        [SerializeField] GameObject _touchToStart;
        [SerializeField] TextMeshProUGUI _touchToStartText;
        [SerializeField] Button _touchToStartBtn;

        System.Action OnTouchToStart { get; set; }

        Coroutine _loadingDotCoroutine;
        Tween _touchToStartTween;
        bool _isTouchToStartBtnClicked = false;

        public UILoadingControl Control { get; private set; } = new UILoadingControl();

        protected override void Awake()
        {
            base.Awake();
        }

        public override void OnOpen()
        {
            _touchToStart.SetActive(false);
            _isTouchToStartBtnClicked = false;
            _touchToStartBtn.onClick.AddListener(() => { OnTouchToStartBtn(); });

            // 열리면 로딩 표시를 시작하고, 게임 흐름 연동은 Control에 위임
            ShowLoadingText();
            Control.Initialize(this);
        }

        public override void OnClose()
        {
            StopLoadingDotAnim();
            _touchToStartTween?.Kill();

            Control.Deinitialize();
        }

        public void ShowLoadingText()
        {
            _loadText.gameObject.SetActive(true);
            StartLoadingDotAnim();
        }

        public void ShowTouchToStartBtn()
        {
            StopLoadingDotAnim();
            _loadText.gameObject.SetActive(false);
            _touchToStart.SetActive(true);

            PlayTouchToStartBlink(0.8f);
        }

        private void OnTouchToStartBtn()
        {
            if (_isTouchToStartBtnClicked)
                return;

            _isTouchToStartBtnClicked = true;

            // 더 빠르게 깜빡임
            _touchToStartTween?.Kill();
            PlayTouchToStartBlink(0.15f);

            // 1.5초 후 콜백 호출
            DOVirtual.DelayedCall(1.5f, () =>
            {
                _touchToStartTween?.Kill();
                OnTouchToStart?.Invoke();
            });
        }

        // === Loading Dot Animation ===

        private void StartLoadingDotAnim()
        {
            StopLoadingDotAnim();
            _loadingDotCoroutine = StartCoroutine(LoadingDotCoroutine());
        }

        private void StopLoadingDotAnim()
        {
            if (_loadingDotCoroutine != null)
            {
                StopCoroutine(_loadingDotCoroutine);
                _loadingDotCoroutine = null;
            }
        }

        private IEnumerator LoadingDotCoroutine()
        {
            string[] dots = { ".", "..", "..." };
            int index = 0;

            while (true)
            {
                _loadText.text = "Now Loading" + dots[index];
                index = (index + 1) % dots.Length;
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void PlayTouchToStartBlink(float interval)
        {
            _touchToStartTween?.Kill();
            _touchToStartText.alpha = 1f;

            _touchToStartTween = _touchToStartText
                .DOFade(0f, interval)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine);
        }

        // 외부에서 콜백 등록
        public void SetOnTouchToStart(System.Action onTouchToStart)
        {
            OnTouchToStart = onTouchToStart;
        }
    }

    // GameMain.OnStageLoaded 신호에 따라 로딩 UI를 갱신하는 컨트롤
    public class UILoadingControl
    {
        private UILoading _loading;

        public void Initialize(UILoading loading)
        {
            _loading = loading;

            // 터치 투 스타트 → 로딩 UI 닫기
            _loading.SetOnTouchToStart(OnTouchToStart);

            GameMain.SubscribeStageLoaded(OnStageLoadComplete);
        }

        public void Deinitialize()
        {
            GameMain.UnsubscribeStageLoaded(OnStageLoadComplete);

            _loading = null;
        }

        // 스테이지 로드 완료 → 터치 투 스타트 버튼 노출
        private void OnStageLoadComplete()
        {
            if (_loading != null)
                _loading.ShowTouchToStartBtn();
        }

        // 터치 투 스타트 → 로딩 UI 닫기
        private void OnTouchToStart()
        {
            if (_loading != null)
                _loading.Close();
        }
    }
}