using UnityEngine;

namespace FunRabbit
{
    // 전투 중 자주 바뀌는 진행값(보스 hp, 아군 슬롯 hp)의 디스크 반영(PlayerPrefs.Save)을 모아서 하는 스케줄러.
    // - 값 자체는 호출자가 PlayerPrefs.SetXxx 로 즉시 쓴다 (메모리 반영 → 같은 프레임의 조회/클라우드 스냅샷 캡처에 바로 보인다).
    // - 디스크 flush 만 dirty 플래그로 모아 FlushInterval 초마다, 또는 백그라운드 전환/포커스 상실/종료 시 1회 한다.
    //   (PlayerPrefs.Save 는 Android 에서 SharedPreferences 커밋 = 디스크 쓰기라 피격마다 부르면 프레임이 튄다)
    // - 앱이 강제 종료(프로세스 kill)되면 마지막 flush 이후 최대 FlushInterval 초 동안의 "전투 hp 진행"만 잃는다 - 허용.
    //   코인·보상·스테이지 전환·클리어(보스 hp 0) 등 재화/보상에 영향을 주는 저장은 이 스케줄러를 쓰지 않고
    //   지금처럼 즉시 PlayerPrefs.Save (PlayerContext.RunAtomic / FlushPrefs, GameQuestManager.QueueStageClear 등) 한다.
    // - 다른 경로의 PlayerPrefs.Save 가 먼저 불리면 미반영 값도 함께 디스크에 쓰인다 (그 뒤 flush 는 중복이지만 무해).
    public class PrefsSaveScheduler : Singleton<PrefsSaveScheduler>
    {
        // 디스크 flush 주기 (초, 실시간 기준 - 일시정지/타임스케일과 무관)
        public const float FlushInterval = 2.5f;

        static bool _dirty;
        static float _dirtySince;

        // 값은 이미 PlayerPrefs.SetXxx 로 썼고, 디스크 flush 만 미룬다
        public static void MarkDirty()
        {
            // 에디터 편집 모드(에디터 검사 스크립트 등)에서는 스케줄러를 만들 수 없으니 바로 저장한다
            if (!Application.isPlaying)
            {
                _dirty = false;
                PlayerPrefs.Save();
                return;
            }

            if (!_dirty)
            {
                _dirty = true;
                _dirtySince = Time.realtimeSinceStartup;
            }

            // 첫 사용 시 스케줄러 오브젝트 생성 (DontDestroyOnLoad). 종료 중이면 만들지 않는다 (종료 시 Unity 가 자체 저장)
            if (!IsCheckInstance() && !IsQuitting)
                MakeInstance();
        }

        // 미반영 값이 있으면 지금 디스크에 쓴다
        public static void FlushNow()
        {
            if (!_dirty)
                return;

            _dirty = false;
            PlayerPrefs.Save();
        }

        private void Update()
        {
            if (_dirty && Time.realtimeSinceStartup - _dirtySince >= FlushInterval)
                FlushNow();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushNow();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                FlushNow();
        }

        private void OnApplicationQuit()
        {
            FlushNow();
        }
    }
}
