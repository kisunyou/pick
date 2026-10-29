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

        [SerializeField] Button _guestLoginBtn;
        [SerializeField] Button _googleLoginBtn;

        System.Action OnTouchToStart { get; set; }
        System.Action OnGuestLogin { get; set; }
        System.Action OnGoogleLogin { get; set; }

        Coroutine _loadingDotCoroutine;
        Tween _touchToStartTween;
        Tween _startDelayTween;
        UILogoIntroAnimation _logoIntro;
        UILoadingStartInput _startInput;
        bool _introRequested;
        bool _startInputReady;
        bool _isTouchToStartBtnClicked = false;

        public UILoadingControl Control { get; private set; } = new UILoadingControl();

        protected override void Awake()
        {
            base.Awake();
            ResetStartPresentation();
        }

        public override void OnOpen()
        {
            _touchToStart.SetActive(false);
            HideLoginButtons();
            _isTouchToStartBtnClicked = false;
            _touchToStartBtn.onClick.RemoveListener(OnTouchToStartBtn);
            _guestLoginBtn.onClick.RemoveListener(OnGuestLoginBtn);
            _googleLoginBtn.onClick.RemoveListener(OnGoogleLoginBtn);
            if (_startInput == null) _startInput = _touchToStartBtn.GetComponent<UILoadingStartInput>();
            if (_startInput == null) _startInput = _touchToStartBtn.gameObject.AddComponent<UILoadingStartInput>();
            _startInput.Bind(OnTouchToStartBtn);
            _guestLoginBtn.onClick.AddListener(OnGuestLoginBtn);
            _googleLoginBtn.onClick.AddListener(OnGoogleLoginBtn);

            // 열리면 로딩 표시를 시작하고, 게임 흐름 연동은 Control에 위임
            ShowLoadingText();
            Control.Initialize(this);
        }

        public override void OnClose()
        {
            StopLoadingDotAnim();
            ResetStartPresentation();

            Control.Deinitialize();
        }

        private void OnDisable()
        {
            StopLoadingDotAnim();
            ResetStartPresentation();
        }

        private void OnGuestLoginBtn() => OnGuestLogin?.Invoke();
        private void OnGoogleLoginBtn() => OnGoogleLogin?.Invoke();

        private void ResetStartPresentation()
        {
            _startInputReady = false;
            _introRequested = false;
            if (_startInput != null) _startInput.SetReady(false);
            _isTouchToStartBtnClicked = false;
            _startDelayTween?.Kill();
            _startDelayTween = null;
            _touchToStartTween?.Kill();
            _touchToStartTween = null;
            if (_touchToStartBtn != null) _touchToStartBtn.interactable = false;
            if (_touchToStart != null) _touchToStart.SetActive(false);
            if (_logoIntro == null) _logoIntro = GetComponentInChildren<UILogoIntroAnimation>(true);
            if (_logoIntro != null) _logoIntro.Hide();
        }

        public void ShowLoadingText()
        {
            ResetStartPresentation();
            _loadText.gameObject.SetActive(true);
            StartLoadingDotAnim();
        }

        public void ShowTouchToStartBtn()
        {
            if (_introRequested || !isActiveAndEnabled) return;
            ResetStartPresentation();
            _introRequested = true;
            StopLoadingDotAnim();
            _loadText.gameObject.SetActive(false);
            HideLoginButtons();
            // Loading/auth readiness starts the intro, not gameplay or the start prompt.
            if (_logoIntro != null) _logoIntro.Play(EnableStartInput);
            else EnableStartInput();
        }

        private void EnableStartInput()
        {
            if (!_introRequested || !isActiveAndEnabled) return;
            _touchToStart.SetActive(true);
            _startInputReady = true;
            _touchToStartBtn.interactable = true;
            if (_startInput != null) _startInput.SetReady(true);
            PlayTouchToStartBlink(0.8f);
        }

        // 미로그인 상태 - 게스트/Google 로그인 버튼을 노출한다 (touchToStart 대신)
        public void ShowLoginButtons()
        {
            ResetStartPresentation();
            StopLoadingDotAnim();
            _loadText.gameObject.SetActive(false);
            _touchToStart.SetActive(false);

            _guestLoginBtn.gameObject.SetActive(true);
            _googleLoginBtn.gameObject.SetActive(true);
            SetLoginButtonsInteractable(true);
        }

        public void HideLoginButtons()
        {
            _guestLoginBtn.gameObject.SetActive(false);
            _googleLoginBtn.gameObject.SetActive(false);
        }

        // 로그인 시도 중 중복 클릭 방지용
        public void SetLoginButtonsInteractable(bool interactable)
        {
            _guestLoginBtn.interactable = interactable;
            _googleLoginBtn.interactable = interactable;
        }

        private bool TryAcceptStartInput()
        {
            if (!_startInputReady || _isTouchToStartBtnClicked || !isActiveAndEnabled)
                return false;
            _startInputReady = false;
            _isTouchToStartBtnClicked = true;
            _touchToStartBtn.interactable = false;
            if (_startInput != null) _startInput.SetReady(false);
            return true;
        }

        private void OnTouchToStartBtn()
        {
            if (!TryAcceptStartInput()) return;

            FireBaseAnalyticsManager.Instance.LogEventOnce("touch_start");

            // 더 빠르게 깜빡임
            _touchToStartTween?.Kill();
            PlayTouchToStartBlink(0.15f);

            // 1.5초 후 콜백 호출
            _startDelayTween = DOVirtual.DelayedCall(1.5f, () =>
            {
                _startDelayTween = null;
                if (!_isTouchToStartBtnClicked || !isActiveAndEnabled) return;
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
                _loadText.text = LanguageManager.Instance.Get("loading_now_loading") + dots[index];
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

        public void SetOnLoginButtons(System.Action onGuestLogin, System.Action onGoogleLogin)
        {
            OnGuestLogin = onGuestLogin;
            OnGoogleLogin = onGoogleLogin;
        }
    }

    // GameMain.OnStageLoaded 신호에 따라 로딩 UI를 갱신하는 컨트롤
    public class UILoadingControl
    {
        // Firebase Auth 초기화 대기 한도(초) - 넘으면 로그인 게이트 없이 기존 흐름으로 진행
        const float AuthInitTimeout = 8f;

        // 로그인 이력이 있을 때 저장된 유저 자동 복원을 추가로 기다리는 한도(초)
        // (에디터/PC는 저장 유저 로드가 비동기라 초기화 직후 CurrentUser가 잠시 null일 수 있다)
        const float AuthRestoreTimeout = 3f;

        // 클라우드 세이브 동기화 대기 한도(초) - 넘으면 로컬 데이터로 진행 (게임 진입을 막지 않음)
        const float CloudSyncTimeout = 12f;

        private UILoading _loading;
        private Coroutine _authGateCoroutine;

        public void Initialize(UILoading loading)
        {
            _loading = loading;

            // 터치 투 스타트 → 로딩 UI 닫기
            _loading.SetOnTouchToStart(OnTouchToStart);
            _loading.SetOnLoginButtons(OnGuestLoginBtn, OnGoogleLoginBtn);

            GameMain.SubscribeStageLoaded(OnStageLoadComplete);
        }

        public void Deinitialize()
        {
            GameMain.UnsubscribeStageLoaded(OnStageLoadComplete);

            if (_authGateCoroutine != null && _loading != null)
                _loading.StopCoroutine(_authGateCoroutine);
            _authGateCoroutine = null;

            _loading = null;
        }

        // 스테이지 로드 완료 → 로그인 상태에 따라 로그인 버튼 or 터치 투 스타트 노출
        private void OnStageLoadComplete()
        {
            if (_loading != null)
                _authGateCoroutine = _loading.StartCoroutine(AuthGateCoroutine());
        }

        // Firebase Auth 초기화를 기다렸다가 분기한다.
        // - 로그인돼 있으면(게스트/Google 무관) → 기존처럼 터치 투 스타트
        // - 미로그인이면 → 로그인 버튼 노출
        // - 초기화 실패/타임아웃이면 → 게임 진입을 막지 않도록 터치 투 스타트로 진행
        private IEnumerator AuthGateCoroutine()
        {
            var auth = FireBaseAuthManager.Instance;

            float deadline = Time.realtimeSinceStartup + AuthInitTimeout;
            while (auth != null && !auth.IsInitializeDone && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (auth == null || !auth.IsInitialized)
            {
                _authGateCoroutine = null;
                if (_loading == null)
                    yield break;

                Debug.LogWarning("[UILoadingControl] Firebase Auth 초기화 실패/지연 - 로그인 없이 진행합니다.");
                _loading.ShowTouchToStartBtn();
                yield break;
            }

            // 로그인 이력이 있는데 아직 미로그인이면 저장 유저 자동 복원을 잠시 더 기다린다
            if (!auth.IsLoggedIn && auth.HasEverLoggedIn)
            {
                Debug.Log("[UILoadingControl] 로그인 이력 있음 - 저장된 유저 자동 복원 대기...");

                float restoreDeadline = Time.realtimeSinceStartup + AuthRestoreTimeout;
                while (!auth.IsLoggedIn && Time.realtimeSinceStartup < restoreDeadline)
                    yield return null;

                if (!auth.IsLoggedIn)
                    Debug.LogWarning("[UILoadingControl] 자동 복원 시간 초과 - 로그인 버튼을 다시 노출합니다.");
            }

            _authGateCoroutine = null;

            if (_loading == null)
                yield break;

            if (auth.IsLoggedIn)
            {
                // 클라우드 세이브 동기화 (로딩 표시 유지 상태에서 진행)
                yield return CloudSyncCoroutine();

                if (_loading == null)
                    yield break;

                // 자동 복원된 로그인도 매번 토스트로 알린다 (익명=게스트 / 그 외=구글)
                ShowLoginMessage(auth.IsAnonymousUser);
                _loading.ShowTouchToStartBtn();
            }
            else
            {
                _loading.ShowLoginButtons();
            }
        }

        // 클라우드 세이브 동기화를 기다린다. 한도를 넘거나 실패해도 그대로 진행한다(fail-open).
        private IEnumerator CloudSyncCoroutine()
        {
            var cloud = CloudSaveManager.Instance;
            if (cloud == null)
                yield break;

            bool done = false;
            cloud.SyncOnLogin(() => done = true);

            float deadline = Time.realtimeSinceStartup + CloudSyncTimeout;
            while (!done && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!done)
            {
                cloud.CancelLoginSync();
                Debug.LogWarning("[UILoadingControl] 클라우드 동기화 지연 - 로컬 데이터로 진행합니다.");
            }
        }

        private void OnGuestLoginBtn()
        {
            if (_loading == null)
                return;

            _loading.SetLoginButtonsInteractable(false);
            FireBaseAuthManager.Instance.SignInAnonymously(
                success => OnLoginResult(success, "login_message_guest"));
        }

        private void OnGoogleLoginBtn()
        {
            if (_loading == null)
                return;

            _loading.SetLoginButtonsInteractable(false);
            FireBaseAuthManager.Instance.SignInWithGoogle(
                success => OnLoginResult(success, "login_message_google"));
        }

        // 로그인 성공 → 클라우드 동기화 → 토스트 + 터치 투 스타트 / 실패 → 버튼 재활성화(재시도 가능)
        private void OnLoginResult(bool success, string messageKey)
        {
            if (_loading == null)
                return;

            if (success)
            {
                // 동기화 동안 로그인 버튼 대신 로딩 표시
                _loading.HideLoginButtons();
                _loading.ShowLoadingText();
                _loading.StartCoroutine(AfterLoginCoroutine(messageKey));
            }
            else
            {
                _loading.SetLoginButtonsInteractable(true);
            }
        }

        private IEnumerator AfterLoginCoroutine(string messageKey)
        {
            yield return CloudSyncCoroutine();

            if (_loading == null)
                yield break;

            UITopMessage.ShowMessage(LanguageManager.Instance.Get(messageKey));
            _loading.ShowTouchToStartBtn();
        }

        // 로그인 방식에 맞는 토스트 메시지 노출 (게스트=익명 / 그 외=구글)
        private static void ShowLoginMessage(bool isGuest)
        {
            string messageKey = isGuest ? "login_message_guest" : "login_message_google";
            UITopMessage.ShowMessage(LanguageManager.Instance.Get(messageKey));
        }

        // 터치 투 스타트 → 로딩 UI 닫기
        private void OnTouchToStart()
        {
            CloudSaveManager.Instance.MarkGameplayStarted();
            if (_loading != null)
                _loading.Close();
        }
    }
}
