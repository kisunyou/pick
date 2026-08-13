using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
using UnityEngine.Android;
#endif

namespace FunRabbit
{
    // 코인 타이머(10분) 완료 시각에 맞춰, 게임을 안 하고 있을 때(백그라운드) 안드로이드
    // 로컬 알림("코인을 받을 수 있습니다. 지금 시도하세요")을 보낸다.
    //
    // - 예약: 앱이 백그라운드로 갈 때(GameMain.OnApplicationPause) 카운트다운이 아직
    //   진행 중인 경우에만 완료 시각으로 예약한다. 이미 "받기" 가능 상태로 나간 경우는
    //   방금 화면에서 본 정보이므로 알림을 보내지 않는다.
    // - 하루 1회 제한: 예약한 날짜(yyyyMMdd)를 기록해 같은 날 중복 예약을 막는다.
    //   발송 전에 복귀해 취소된 알림은 유저가 못 본 것이므로 기록을 롤백해
    //   같은 날 다시 예약될 수 있게 한다.
    // - 복귀: 미발송 예약을 취소한다 (게임 중에는 알림이 뜨지 않도록).
    // - 문구는 예약 시점의 현재 언어(stringData: noti_coin_title/body)로 적용된다.
    public static class CoinRewardNotificationScheduler
    {
        const string TitleKey = "noti_coin_title";
        const string BodyKey = "noti_coin_body";

#if UNITY_ANDROID && !UNITY_EDITOR
        const string ChannelId = "coin_reward";
        const string KeyNotiDate = "CoinNotiDate"; // 마지막 예약 날짜 (yyyyMMdd) - 하루 1회 제한
        const string KeyNotiId = "CoinNotiId";     // 마지막 예약 알림 id (복귀 시 취소용)

        static bool _channelReady;

        static string Today() => System.DateTime.Now.ToString("yyyyMMdd");

        // 채널 등록 + (Android 13+) 알림 권한 요청. 앱 시작 시 1회 호출.
        public static void Init()
        {
            RegisterChannel();
            RequestPermissionIfNeeded();
        }

        static void RegisterChannel()
        {
            if (_channelReady)
                return;

            var channel = new AndroidNotificationChannel()
            {
                Id = ChannelId,
                Name = "Coin Reward",
                Importance = Importance.Default,
                Description = "Coin timer reward notifications",
            };
            AndroidNotificationCenter.RegisterNotificationChannel(channel);
            _channelReady = true;
        }

        // Android 13(API 33)+ 는 알림 표시에 런타임 권한이 필요하다.
        // 이하 버전에서는 HasUserAuthorizedPermission이 항상 true라 요청이 스킵된다.
        static void RequestPermissionIfNeeded()
        {
            const string permission = "android.permission.POST_NOTIFICATIONS";
            if (!Permission.HasUserAuthorizedPermission(permission))
                Permission.RequestUserPermission(permission);
        }

        // 백그라운드 진입 시: 코인 타이머 완료 시각에 알림 예약 (하루 1회)
        public static void OnAppPause()
        {
            if (PlayerPrefs.GetString(KeyNotiDate, string.Empty) == Today())
                return; // 오늘 이미 예약/발송함

            // 카운트다운 진행 중일 때만 - 이미 "받기" 가능 상태(remaining<=0)면 보내지 않는다
            if (!CoinGetTimer.TryGetRemainingSeconds(out double remaining) || remaining <= 0)
                return;

            RegisterChannel();

            var notification = new AndroidNotification()
            {
                Title = LanguageManager.Instance.Get(TitleKey),
                Text = LanguageManager.Instance.Get(BodyKey),
                FireTime = System.DateTime.Now.AddSeconds(remaining),
                ShowTimestamp = true,
            };

            int id = AndroidNotificationCenter.SendNotification(notification, ChannelId);
            PlayerPrefs.SetInt(KeyNotiId, id);
            PlayerPrefs.SetString(KeyNotiDate, Today());
            PlayerPrefs.Save();
        }

        // 복귀 시: 아직 발송되지 않은 예약이 있으면 취소하고 하루 1회 기록을 롤백한다.
        // (이미 발송된 알림은 기록을 유지해 같은 날 재발송을 막는다)
        public static void OnAppResume()
        {
            int id = PlayerPrefs.GetInt(KeyNotiId, -1);
            if (id < 0)
                return;

            var status = AndroidNotificationCenter.CheckScheduledNotificationStatus(id);
            if (status == NotificationStatus.Scheduled)
            {
                AndroidNotificationCenter.CancelScheduledNotification(id);
                PlayerPrefs.DeleteKey(KeyNotiDate); // 미발송 - 유저가 못 봤으므로 롤백
            }

            PlayerPrefs.DeleteKey(KeyNotiId);
            PlayerPrefs.Save();
        }
#else
        // 에디터/타 플랫폼에서는 아무것도 하지 않는다 (iOS 대응 시 이 클래스에 분기 추가)
        public static void Init() { }
        public static void OnAppPause() { }
        public static void OnAppResume() { }
#endif
    }
}
