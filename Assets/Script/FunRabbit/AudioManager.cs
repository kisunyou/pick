using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    // 게임/크레인 상태에 따라 BGM을 재생하는 사운드 매니저.
    // - 클립은 Resources/Sound 에서 이름으로 로드해 캐시한다.
    // - 같은 BGM이 이미 재생 중이면 다시 재생하지 않는다 (중복 방지).
    // - GameStatus.LOBBY / CraneStatus.READY → ready_bgm, CraneStatus.CONTROL_MOVING → play_bgm.
    public class AudioManager : Singleton<AudioManager>
    {
        const string SoundResourceRoot = "Sound/";
        const string ReadyBgmName = "ready_bgm";
        const string PlayBgmName = "play_bgm";
        const string SfxVolumePrefsKey = "SfxVolume";
        const string BgmVolumePrefsKey = "BgmVolume";

        private AudioSource _bgmSource;
        private AudioSource _sfxSource;
        private AudioSource _loopSfxSource;
        private string _currentBgmName;
        private string _currentLoopSfxName;
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();
        private Crane _crane;
        private float _sfxVolume = 1f;
        private float _bgmVolume = 1f;

        // 효과음 볼륨 (0~1). 변경 즉시 모든 SFX 소스에 반영되고 PlayerPrefs에 저장된다.
        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                _sfxSource.volume = _sfxVolume;
                _loopSfxSource.volume = _sfxVolume;
                PlayerPrefs.SetFloat(SfxVolumePrefsKey, _sfxVolume);
            }
        }

        // BGM 볼륨 (0~1). 변경 즉시 반영되고 PlayerPrefs에 저장된다.
        public float BgmVolume
        {
            get => _bgmVolume;
            set
            {
                _bgmVolume = Mathf.Clamp01(value);
                _bgmSource.volume = _bgmVolume;
                PlayerPrefs.SetFloat(BgmVolumePrefsKey, _bgmVolume);
            }
        }

        protected override void Awake()
        {
            base.Awake();

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;

            _loopSfxSource = gameObject.AddComponent<AudioSource>();
            _loopSfxSource.loop = true;
            _loopSfxSource.playOnAwake = false;

            // 저장된 볼륨 설정 로드 (없으면 1)
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefsKey, 1f));
            _bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumePrefsKey, 1f));
            _sfxSource.volume = _sfxVolume;
            _loopSfxSource.volume = _sfxVolume;
            _bgmSource.volume = _bgmVolume;
        }

        private void Start()
        {
            GameMain.SubscribeStatus(OnChangedGameStatus);
            // 크레인은 Stage0 씬 로드 후에 존재하므로, 로드 완료 시점에 구독한다.
            GameMain.SubscribeStageLoaded(OnStageLoaded);
        }

        private void OnStageLoaded()
        {
            if (_crane == null && Crane.TryGetSetInstance(out Crane crane))
                _crane = crane;

            // SubscribeStatus가 내부에서 중복 구독을 막고, 현재 상태를 즉시 1회 반영해 준다
            _crane?.SubscribeStatus(OnChangedCraneStatus);
        }

        private void OnChangedGameStatus(GameStatus status)
        {
            if (status == GameStatus.LOBBY)
                PlayBGM(ReadyBgmName);
        }

        private void OnChangedCraneStatus(int craneStatus)
        {
            if (craneStatus == CraneStatus.CONTROL_MOVING)
                PlayBGM(PlayBgmName);
            else if (craneStatus == CraneStatus.READY)
                PlayBGM(ReadyBgmName);
        }

        // BGM을 루프 재생한다. 같은 BGM이 이미 재생 중이면 아무것도 하지 않는다.
        public void PlayBGM(string clipName)
        {
            if (_currentBgmName == clipName && _bgmSource.isPlaying)
                return;

            AudioClip clip = LoadClip(clipName);
            if (clip == null)
                return;

            _currentBgmName = clipName;
            _bgmSource.clip = clip;
            _bgmSource.Play();
        }

        public void StopBGM()
        {
            _currentBgmName = null;
            _bgmSource.Stop();
        }

        // 효과음 1회 재생 (BGM과 별도 소스라 서로 간섭 없음). 경로가 비어 있으면 무시.
        public void PlaySfx(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return;

            AudioClip clip = LoadClip(clipName);
            if (clip != null)
                _sfxSource.PlayOneShot(clip);
        }

        // 루프 효과음(크레인 이동음 등)을 재생한다. 같은 클립이 이미 재생 중이면 무시.
        // (BGM/일반 효과음과 별도 소스라 서로 간섭 없음)
        public void PlayLoopSfx(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return;

            if (_currentLoopSfxName == clipName && _loopSfxSource.isPlaying)
                return;

            AudioClip clip = LoadClip(clipName);
            if (clip == null)
                return;

            _currentLoopSfxName = clipName;
            _loopSfxSource.clip = clip;
            _loopSfxSource.Play();
        }

        // 루프 효과음을 정지한다. (재생 중이 아니면 무시)
        public void StopLoopSfx()
        {
            if (!_loopSfxSource.isPlaying)
                return;

            _currentLoopSfxName = null;
            _loopSfxSource.Stop();
        }

        // 효과음을 지정 배속(pitch)으로 1회 재생한다.
        // 공용 SFX 소스의 pitch를 바꾸면 겹쳐 재생 중인 다른 효과음까지 영향을 받으므로,
        // 재생이 끝나면 사라지는 임시 소스를 만들어 격리한다.
        public void PlaySfx(string clipName, float pitch)
        {
            if (string.IsNullOrEmpty(clipName))
                return;

            AudioClip clip = LoadClip(clipName);
            if (clip == null)
                return;

            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.loop = false;
            source.playOnAwake = false;
            source.pitch = pitch;
            source.volume = _sfxVolume;
            source.clip = clip;
            source.Play();
            Destroy(source, clip.length / Mathf.Max(0.01f, pitch) + 0.1f);
        }

        // 효과음을 delaySeconds 뒤에 1회 재생한다.
        public void PlaySfxDelayed(string clipName, float delaySeconds)
        {
            if (string.IsNullOrEmpty(clipName))
                return;

            StartCoroutine(PlaySfxDelayedCoroutine(clipName, delaySeconds));
        }

        private IEnumerator PlaySfxDelayedCoroutine(string clipName, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);
            PlaySfx(clipName);
        }

        // Resources/Sound 에서 클립을 로드한다. (캐시)
        private AudioClip LoadClip(string clipName)
        {
            if (_clipCache.TryGetValue(clipName, out var cached) && cached != null)
                return cached;

            AudioClip clip = Resources.Load<AudioClip>(SoundResourceRoot + clipName);
            if (clip == null)
            {
                Debug.LogError($"[AudioManager] 사운드 로드 실패: {SoundResourceRoot}{clipName}");
                return null;
            }

            _clipCache[clipName] = clip;
            return clip;
        }
    }
}
