using System;
using System.Collections;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace FunRabbit
{
    // Unity LevelPlay(ironSource) 광고 매니저.
    // - SDK 초기화 + Rewarded/Interstitial/Banner 로드·표시를 담당한다.
    // - GameMain.Start()에서 MakeInstance()로 깨워진다 (AudioManager/CollectionManager와 동일 패턴).
    public class LevelPlayAds : Singleton<LevelPlayAds>
    {
        [Header("App Key")]
        [SerializeField] string androidAppKey = "2742ead15";
        [SerializeField] string iosAppKey;

        [Header("Ad Unit Id (비워두면 해당 광고 타입은 사용 안 함)")]
        [SerializeField] string androidRewardedAdUnitId = "mdboh973mkghysnk"; // 광고보상500
        [SerializeField] string iosRewardedAdUnitId;
        [SerializeField] string androidInterstitialAdUnitId;
        [SerializeField] string iosInterstitialAdUnitId;
        [SerializeField] string androidBannerAdUnitId;
        [SerializeField] string iosBannerAdUnitId;

        [Header("테스트")]
        // true면 초기화 성공 직후 어댑터 디버그 로그를 켜고 Test Suite를 자동으로 띄운다
        [SerializeField] bool testMode;

        public bool IsInitialized { get; private set; }

        LevelPlayRewardedAd _rewardedAd;
        LevelPlayInterstitialAd _interstitialAd;
        LevelPlayBannerAd _bannerAd;

        readonly System.Collections.Generic.Dictionary<string, RewardedAdRequest> _requests =
            new System.Collections.Generic.Dictionary<string, RewardedAdRequest>();
        RewardedAdRequest _activeRequest;
        RewardedAdRequest _unidentifiedClosedRequest;
        string _loadedRewardKey;
        bool _initializing, _rewardLoading;
        int _initAttempts, _loadAttempts;
        float _nextInitAt, _nextLoadAt, _loadDeadline;
        public bool RewardLoadFailed { get; private set; }
        public static float RetryDelay(int attempt) => Mathf.Min(60f, 2f * Mathf.Pow(2f, Mathf.Clamp(attempt - 1, 0, 5)));

        void Update()
        {
            if (!IsInitialized) { TryInitialize(); return; }
            if (_rewardLoading && Time.realtimeSinceStartup > _loadDeadline)
            {
                _rewardLoading = false;
                RewardLoadFailed = true;
                _nextLoadAt = Time.realtimeSinceStartup + RetryDelay(++_loadAttempts);
            }
            if (!_rewardLoading && Time.realtimeSinceStartup >= _nextLoadAt &&
                (_activeRequest == null || _activeRequest.IsClosed) &&
                _rewardedAd != null && !_rewardedAd.IsAdReady())
                LoadRewardedAd();
        }

        void TryInitialize()
        {
            if (IsInitialized || _initializing || Time.realtimeSinceStartup < _nextInitAt) return;
            string key = ResolveAppKey();
            if (string.IsNullOrEmpty(key)) return;
            _initializing = true;
            try { LevelPlay.Init(key); }
            catch (Exception e)
            {
                _initializing = false;
                _nextInitAt = Time.realtimeSinceStartup + RetryDelay(++_initAttempts);
                Debug.LogWarning("[LevelPlayAds] Initialization retry: " + e.Message);
            }
        }

        public void EnsureRewardedAdLoaded()
        {
            if (!IsInitialized) { TryInitialize(); return; }
            if (!_rewardLoading && Time.realtimeSinceStartup >= _nextLoadAt &&
                (_activeRequest == null || _activeRequest.IsClosed) && _rewardedAd != null && !_rewardedAd.IsAdReady())
                LoadRewardedAd();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) EnsureRewardedAdLoaded();
        }


        string ResolveAppKey()
        {
#if UNITY_IOS
            return iosAppKey;
#else
            return androidAppKey;
#endif
        }

        string ResolveRewardedAdUnitId()
        {
#if UNITY_IOS
            return iosRewardedAdUnitId;
#else
            return androidRewardedAdUnitId;
#endif
        }

        string ResolveInterstitialAdUnitId()
        {
#if UNITY_IOS
            return iosInterstitialAdUnitId;
#else
            return androidInterstitialAdUnitId;
#endif
        }

        string ResolveBannerAdUnitId()
        {
#if UNITY_IOS
            return iosBannerAdUnitId;
#else
            return androidBannerAdUnitId;
#endif
        }

        // ===== 초기화 =====

        void Start()
        {
            string appKey = ResolveAppKey();
            if (string.IsNullOrEmpty(appKey))
            {
                Debug.LogError("[LevelPlayAds] App Key가 비어있습니다. Inspector에서 설정하세요.");
                return;
            }

            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += OnInitFailed;

            if (testMode)
                LevelPlay.SetAdaptersDebug(true);

            TryInitialize();
        }

        void OnInitSuccess(LevelPlayConfiguration configuration)
        {
            if (IsInitialized) return;
            _initializing = false;
            _initAttempts = 0;
            IsInitialized = true;
            Debug.Log("[LevelPlayAds] 초기화 성공");

            SetupRewardedAd();
            SetupInterstitialAd();
            SetupBannerAd();

            LoadRewardedAd();
            LoadInterstitialAd();

            if (testMode)
                LaunchTestSuite();
        }

        void OnInitFailed(LevelPlayInitError error)
        {
            IsInitialized = false;
            _initializing = false;
            _nextInitAt = Time.realtimeSinceStartup + RetryDelay(++_initAttempts);
            Debug.LogWarning($"[LevelPlayAds] 초기화 실패: {error.ErrorCode} {error.ErrorMessage}");
        }

        protected override void OnDestroy()
        {
            LevelPlay.OnInitSuccess -= OnInitSuccess;
            LevelPlay.OnInitFailed -= OnInitFailed;

            _rewardedAd?.DestroyAd();
            _interstitialAd?.DestroyAd();
            _bannerAd?.DestroyAd();

            base.OnDestroy();
        }

        // ===== 테스트용 함수 =====

        // ironSource Test Suite 실행 (초기화 후에만 동작) - 실제 유저에게 보여줄 광고가 아니라
        // 연동 상태(어댑터/키/유닛ID)를 직접 눈으로 확인하기 위한 디버그 화면.
        public void LaunchTestSuite()
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[LevelPlayAds] 초기화 전에는 Test Suite를 열 수 없습니다.");
                return;
            }

            LevelPlay.LaunchTestSuite();
        }

        // App Key/매니페스트 등 SDK 연동 상태를 점검해 콘솔에 결과를 출력한다.
        public void ValidateIntegration()
        {
            LevelPlay.ValidateIntegration();
        }

        // 어댑터 디버그 로그 on/off (테스트 중 상세 로그가 필요할 때 수동으로도 토글 가능)
        public void SetAdaptersDebug(bool enable)
        {
            LevelPlay.SetAdaptersDebug(enable);
        }

        // 세 광고 타입의 로드 상태를 한 번에 로그로 출력한다 (수동 QA용).
        public void LogAdStatus()
        {
            Debug.Log($"[LevelPlayAds] Rewarded Ready={IsRewardedAdReady()} / Interstitial Ready={IsInterstitialAdReady()}");
        }

        // ===== 실제 사용 함수 - Rewarded =====

        static string RewardKey(LevelPlayAdInfo info) =>
            !string.IsNullOrEmpty(info?.AuctionId) ? info.AuctionId : info?.AdId;

        void SetupRewardedAd()
        {
            string adUnitId = ResolveRewardedAdUnitId();
            if (string.IsNullOrEmpty(adUnitId) || _rewardedAd != null) return;
            _rewardedAd = new LevelPlayRewardedAd(adUnitId);
            _rewardedAd.OnAdLoaded += info =>
            {
                _rewardLoading = false; RewardLoadFailed = false; _loadAttempts = 0;
                _loadedRewardKey = RewardKey(info);
            };
            _rewardedAd.OnAdLoadFailed += error =>
            {
                _rewardLoading = false; RewardLoadFailed = true;
                _nextLoadAt = Time.realtimeSinceStartup + RetryDelay(++_loadAttempts);
                Debug.LogWarning("[LevelPlayAds] Rewarded load failed: " + error.ErrorMessage);
            };
            _rewardedAd.OnAdDisplayed += info => BindActiveRequest(info);
            _rewardedAd.OnAdRewarded += (info, reward) =>
            {
                RewardedAdRequest request = FindRequest(info);
                if (request == null) { Debug.LogWarning("[LevelPlayAds] Unmatched reward callback."); return; }
                request.Reward();
            };
            _rewardedAd.OnAdDisplayFailed += (info, error) =>
            {
                (FindRequest(info) ?? _activeRequest)?.Fail();
                _nextLoadAt = Time.realtimeSinceStartup + RetryDelay(++_loadAttempts);
                RewardLoadFailed = true;
            };
            _rewardedAd.OnAdClosed += info =>
            {
                // Keep the request addressable: OnAdRewarded may arrive after this event or after another ad.
                RewardedAdRequest request = FindRequest(info);
                if (request == null || request.IsClosed) return;
                if (string.IsNullOrEmpty(RewardKey(info)) && !request.IsRewarded)
                    _unidentifiedClosedRequest = request;
                request.Close();
                if (ReferenceEquals(request, _activeRequest))
                {
                    _loadedRewardKey = null;
                    LoadRewardedAd();
                }
            };
        }

        void BindActiveRequest(LevelPlayAdInfo info)
        {
            string key = RewardKey(info);
            if (_activeRequest == null || _activeRequest.IsClosed || string.IsNullOrEmpty(key)) return;
            if (!_requests.ContainsKey(key)) _requests.Add(key, _activeRequest);
        }

        RewardedAdRequest FindRequest(LevelPlayAdInfo info)
        {
            string key = RewardKey(info);
            if (!string.IsNullOrEmpty(key) && _requests.TryGetValue(key, out var request)) return request;
            if (!string.IsNullOrEmpty(key)) return null;
            if (_unidentifiedClosedRequest != null)
                return _unidentifiedClosedRequest;
            // A missing ID is usable only while one unambiguous presentation is active.
            if (_activeRequest != null && !_activeRequest.IsClosed)
            {
                BindActiveRequest(info);
                return _activeRequest;
            }
            return null;
        }

        public bool IsRewardedAdReady() => _rewardedAd != null && _rewardedAd.IsAdReady() &&
            (_unidentifiedClosedRequest == null || _unidentifiedClosedRequest.IsRewarded || _unidentifiedClosedRequest.IsFailed) &&
            (_activeRequest == null || _activeRequest.IsClosed) &&
            (string.IsNullOrEmpty(_loadedRewardKey) || !_requests.ContainsKey(_loadedRewardKey));

        public void LoadRewardedAd()
        {
            if (_rewardedAd == null || _rewardLoading) return;
            _rewardLoading = true;
            _loadDeadline = Time.realtimeSinceStartup + 30f;
            try { _rewardedAd.LoadAd(); }
            catch (Exception e)
            {
                _rewardLoading = false; RewardLoadFailed = true;
                _nextLoadAt = Time.realtimeSinceStartup + RetryDelay(++_loadAttempts);
                Debug.LogWarning("[LevelPlayAds] Rewarded load retry: " + e.Message);
            }
        }

        public void ShowRewardedAd(Action onRewarded, Action onFailed = null, Action onClosed = null)
        {
            if (!IsRewardedAdReady())
            {
                EnsureRewardedAdLoaded();
                onFailed?.Invoke();
                return;
            }
            _activeRequest = new RewardedAdRequest(onRewarded, onFailed, onClosed);
            if (!string.IsNullOrEmpty(_loadedRewardKey)) _requests[_loadedRewardKey] = _activeRequest;
            try { _rewardedAd.ShowAd(); }
            catch (Exception e)
            {
                Debug.LogWarning("[LevelPlayAds] Rewarded show failed: " + e.Message);
                _activeRequest.Fail();
            }
        }

        // ===== 실제 사용 함수 - Interstitial =====

        void SetupInterstitialAd()
        {
            string adUnitId = ResolveInterstitialAdUnitId();
            if (string.IsNullOrEmpty(adUnitId))
                return;

            _interstitialAd = new LevelPlayInterstitialAd(adUnitId);
            _interstitialAd.OnAdLoaded += _ => Debug.Log("[LevelPlayAds] Interstitial 로드 완료");
            _interstitialAd.OnAdLoadFailed += error => Debug.LogWarning($"[LevelPlayAds] Interstitial 로드 실패: {error.ErrorMessage}");
            _interstitialAd.OnAdClosed += _ => LoadInterstitialAd(); // 닫히면 다음 노출을 위해 바로 프리로드
        }

        public bool IsInterstitialAdReady() => _interstitialAd != null && _interstitialAd.IsAdReady();

        public void LoadInterstitialAd()
        {
            _interstitialAd?.LoadAd();
        }

        public void ShowInterstitialAd()
        {
            if (!IsInterstitialAdReady())
            {
                Debug.LogWarning("[LevelPlayAds] Interstitial 광고가 준비되지 않았습니다.");
                return;
            }

            _interstitialAd.ShowAd();
        }

        // ===== 실제 사용 함수 - Banner =====

        void SetupBannerAd()
        {
            string adUnitId = ResolveBannerAdUnitId();
            if (string.IsNullOrEmpty(adUnitId))
                return;

            _bannerAd = new LevelPlayBannerAd(adUnitId);
            _bannerAd.OnAdLoaded += _ => Debug.Log("[LevelPlayAds] Banner 로드 완료");
            _bannerAd.OnAdLoadFailed += error => Debug.LogWarning($"[LevelPlayAds] Banner 로드 실패: {error.ErrorMessage}");
        }

        public void LoadBannerAd() => _bannerAd?.LoadAd();
        public void ShowBannerAd() => _bannerAd?.ShowAd();
        public void HideBannerAd() => _bannerAd?.HideAd();
    }
}
