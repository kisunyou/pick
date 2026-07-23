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

        Action _onRewardGranted;
        Action _onRewardFailed;
        bool _rewardGrantedThisShow;

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

            LevelPlay.Init(appKey);
        }

        void OnInitSuccess(LevelPlayConfiguration configuration)
        {
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
            Debug.LogError($"[LevelPlayAds] 초기화 실패: {error.ErrorCode} {error.ErrorMessage}");
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

        void SetupRewardedAd()
        {
            string adUnitId = ResolveRewardedAdUnitId();
            if (string.IsNullOrEmpty(adUnitId))
                return;

            _rewardedAd = new LevelPlayRewardedAd(adUnitId);
            _rewardedAd.OnAdLoaded += _ => Debug.Log("[LevelPlayAds] Rewarded 로드 완료");
            _rewardedAd.OnAdLoadFailed += error => Debug.LogWarning($"[LevelPlayAds] Rewarded 로드 실패: {error.ErrorMessage}");
            _rewardedAd.OnAdRewarded += (info, reward) => GrantReward();
            _rewardedAd.OnAdDisplayFailed += (info, error) => FailReward();
            _rewardedAd.OnAdClosed += _ =>
            {
                // 에디터 Mock은 OnAdClosed를 OnAdRewarded보다 먼저 발생시킨다 - 바로 판정하면
                // 뒤이어 오는 진짜 보상 이벤트가 이미 지워진 콜백을 호출하게 되므로, 한 프레임 유예 후 판정한다.
                StartCoroutine(FailRewardIfNotGrantedNextFrame());

                LoadRewardedAd(); // 다음 시청을 위해 바로 프리로드
            };
        }

        IEnumerator FailRewardIfNotGrantedNextFrame()
        {
            yield return null;

            if (!_rewardGrantedThisShow)
                FailReward();
        }

        public bool IsRewardedAdReady() => _rewardedAd != null && _rewardedAd.IsAdReady();

        public void LoadRewardedAd()
        {
            _rewardedAd?.LoadAd();
        }

        // onRewarded: 끝까지 시청해 보상을 지급해야 할 때 호출.
        // onFailed: 광고가 준비되지 않았거나, 중도 이탈/표시 실패로 보상을 주면 안 될 때 호출.
        public void ShowRewardedAd(Action onRewarded, Action onFailed = null)
        {
            if (!IsRewardedAdReady())
            {
                Debug.LogWarning("[LevelPlayAds] Rewarded 광고가 준비되지 않았습니다.");
                onFailed?.Invoke();
                return;
            }

            _onRewardGranted = onRewarded;
            _onRewardFailed = onFailed;
            _rewardGrantedThisShow = false;
            _rewardedAd.ShowAd();
        }

        void GrantReward()
        {
            _rewardGrantedThisShow = true;
            _onRewardGranted?.Invoke();
            _onRewardGranted = null;
            _onRewardFailed = null;
        }

        void FailReward()
        {
            _onRewardFailed?.Invoke();
            _onRewardGranted = null;
            _onRewardFailed = null;
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
