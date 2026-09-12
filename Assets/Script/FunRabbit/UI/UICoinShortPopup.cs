using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(Path = "UI2/Prefabs/UICoinShortPopup", Layer = UILayer.Popup, OpenMode = UIOpenMode.Single, isPool = false)]
    public class UICoinShortPopup : BaseUIView<UICoinShortPopup>
    {
        [SerializeField] RectTransform panel;
        [SerializeField] TMP_Text titleText, descriptionText, adStatusText;
        [SerializeField] Button purchaseButton, adButton, cancelButton, closeButton, dimmedButton;
        bool _selected;
        float _refreshAt;

        protected override void Awake()
        {
            base.Awake();
            purchaseButton.onClick.AddListener(Purchase);
            adButton.onClick.AddListener(WatchAd);
            cancelButton.onClick.AddListener(Close);
            closeButton.onClick.AddListener(Close);
            dimmedButton.onClick.AddListener(Close);
        }

        public override void OnOpen()
        {
            _selected = false;
            if (LevelPlayAds.IsCheckInstance()) LevelPlayAds.Instance.EnsureRewardedAdLoaded();
            titleText.text = LanguageManager.Instance.Get("popup_coin_short_title");
            descriptionText.text = LanguageManager.Instance.Get("popup_coin_short_choices", UIHudControl.PLAY_COIN_COST);
            RefreshAd();
            FitPanel();
        }

        void Update()
        {
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + .5f;
            RefreshAd();
        }

        void RefreshAd()
        {
            int remaining = PlayerContext.GetRemainingWatchAdCount();
            bool ready = LevelPlayAds.IsCheckInstance() && LevelPlayAds.Instance.IsRewardedAdReady();
            adButton.interactable = !_selected && remaining > 0 && ready;
            bool failed = LevelPlayAds.IsCheckInstance() && LevelPlayAds.Instance.RewardLoadFailed;
            string key = remaining <= 0 ? "coin_ad_limit" : !ready ? (failed ? "coin_ad_retrying" : "coin_ad_loading") : "coin_ad_available";
            adStatusText.text = LanguageManager.Instance.Get(key, GameMain.WatchAdRewardCoinAmount, remaining);
        }

        void Purchase()
        {
            if (_selected) return;
            _selected = true;
            FireBaseAnalyticsManager.Instance.LogEvent("coin_short_go_shop");
            Close();
            UIShopPanel.OpenExclusive();
        }

        void WatchAd()
        {
            if (_selected || !adButton.interactable) return;
            _selected = true;
            FireBaseAnalyticsManager.Instance.LogEvent("coin_short_watch_ad");
            Close();
            GameMain.Instance.WatchAdForCoins();
        }

        void OnRectTransformDimensionsChange() => FitPanel();

        void FitPanel()
        {
            if (panel == null) return;
            Rect available = ((RectTransform)transform).rect;
            float scale = Mathf.Min(1f, (available.width - 64f) / panel.sizeDelta.x,
                (available.height - 64f) / panel.sizeDelta.y);
            panel.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }
    }
}
