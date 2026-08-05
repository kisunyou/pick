using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FunRabbit
{
    // 랜덤박스 관련 HUD 컴포넌트.
    // - 열기 버튼 클릭 시 UIRandomboxPanel을 연다.
    // - PlayerContext의 RandomBoxProgressValue / RandomBoxCount를 구독해 UI를 동기화하고 연출한다.
    public class UIRandomBoxHud : MonoBehaviour
    {
        [SerializeField] Button openRandomBoxPanelButton;
        [SerializeField] Image iconImage;
        [SerializeField] Image randomBoxCountBackImage;
        [SerializeField] TextMeshProUGUI randomBoxPercentText;
        [SerializeField] TextMeshProUGUI randomBoxCountText;

        [Header("퍼센트 카운트업 연출")]
        [SerializeField] float percentStepInterval = 0.03f;   // 퍼센트 +1 증가 간격(초)
        [SerializeField] float percentBounceScale = 1.2f;     // 퍼센트 텍스트 튕김 배율
        [SerializeField] float percentBounceDuration = 0.1f;  // 퍼센트 튕김 한 구간 시간(초)

        [Header("카운트/아이콘 튕김 연출")]
        [SerializeField] float bounceScale = 1.3f;            // 튕김 최대 배율
        [SerializeField] float bounceDuration = 0.15f;        // 튕김 한 구간 시간(초)

        [Header("아이콘 어필 루프 (보유 1개 이상일 때 - 클릭 유도)")]
        [SerializeField] float iconPulseScale = 1.12f;        // 뽈록뽈록 루프 배율
        [SerializeField] float iconPulseDuration = 0.5f;      // 루프 한 구간 시간(초)

        [Header("뒷배경(보유 표시)")]
        [SerializeField] float emptyAlpha = 0.6f;             // 보유 0개일 때 알파

        private int _prevPercent = -1;   // -1 = 아직 초기화 안 됨 (증가 판별용)
        private int _prevCount = -1;

        private Coroutine _percentRoutine;
        private Tween _percentTween;
        private Tween _iconTween;        // 증가 시 한 번 팝
        private Tween _iconLoopTween;    // 보유 시 어필 루프
        private Tween _countTextTween;

        private Vector3 _percentBaseScale = Vector3.one;
        private Vector3 _iconBaseScale = Vector3.one;
        private Vector3 _countTextBaseScale = Vector3.one;

        private void Start()
        {
            if (openRandomBoxPanelButton != null)
                openRandomBoxPanelButton.onClick.AddListener(OnClickOpenRandomBoxPanel);

            CacheBaseScales();

            // 현재 값으로 즉시 동기화(첫 콜백 = 초기화, 애니메이션 없음) 후 변화 구독
            PlayerContext.RandomBoxProgressValue.Attach(OnProgressChanged);
            PlayerContext.RandomBoxCount.Attach(OnCountChanged);
        }

        private void OnDestroy()
        {
            PlayerContext.RandomBoxProgressValue.Detach(OnProgressChanged);
            PlayerContext.RandomBoxCount.Detach(OnCountChanged);

            if (_percentRoutine != null)
                StopCoroutine(_percentRoutine);
            _percentTween?.Kill();
            _iconTween?.Kill();
            _iconLoopTween?.Kill();
            _countTextTween?.Kill();
        }

        private void CacheBaseScales()
        {
            if (randomBoxPercentText != null) _percentBaseScale = randomBoxPercentText.transform.localScale;
            if (iconImage != null) _iconBaseScale = iconImage.transform.localScale;
            if (randomBoxCountText != null) _countTextBaseScale = randomBoxCountText.transform.localScale;
        }

        // ===== 진행 게이지(퍼센트) =====

        private void OnProgressChanged(float progress)
        {
            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            // 초기화(첫 콜백) 또는 감소/동일: 애니메이션 없이 즉시 표시
            if (_prevPercent < 0 || percent <= _prevPercent)
            {
                if (_percentRoutine != null)
                {
                    StopCoroutine(_percentRoutine);
                    _percentRoutine = null;
                }
                SetPercentText(percent);
                _prevPercent = percent;
                return;
            }

            // 증가: prev -> percent 까지 +1씩 카운트업 + 튕김
            if (_percentRoutine != null)
                StopCoroutine(_percentRoutine);
            _percentRoutine = StartCoroutine(CountUpPercent(_prevPercent, percent));
            _prevPercent = percent;
        }

        private IEnumerator CountUpPercent(int from, int to)
        {
            for (int p = from + 1; p <= to; p++)
            {
                SetPercentText(p);
                PunchScale(randomBoxPercentText != null ? randomBoxPercentText.transform : null,
                    ref _percentTween, _percentBaseScale, percentBounceScale, percentBounceDuration);
                yield return new WaitForSeconds(percentStepInterval);
            }
            _percentRoutine = null;
        }

        private void SetPercentText(int percent)
        {
            if (randomBoxPercentText != null)
                randomBoxPercentText.text = percent + "%";
        }

        // ===== 보유 카운트 =====

        private void OnCountChanged(int count)
        {
            bool isInit = _prevCount < 0;
            bool increased = !isInit && count > _prevCount;

            // 값 세팅 (초기화/감소 포함 항상)
            SetCountText(count);

            // 증가할 때만 튕김 연출 (초기화/감소는 값만 세팅)
            if (increased)
            {
                // 아이콘: 한 번 크게 팝 → 팝이 끝나면 어필 루프 재개
                PlayIconPop();
                PunchScale(randomBoxCountText != null ? randomBoxCountText.transform : null,
                    ref _countTextTween, _countTextBaseScale, bounceScale, bounceDuration);
            }
            else
            {
                // 초기화/감소: 아이콘 어필 루프 상태만 보유량에 맞춰 갱신
                UpdateIconLoop(count);
            }

            // 뒷배경 상태(루프 펄스 / 알파)는 보유량 기반 - 초기화 포함 항상 갱신
            UpdateBackImageState(count);

            _prevCount = count;
        }

        private void SetCountText(int count)
        {
            if (randomBoxCountText != null)
                randomBoxCountText.text = count.ToString();
        }

        // 보유 1개 이상: 알파 정상. 0개: 알파 다운(흐리게). (스케일 연출 없음)
        private void UpdateBackImageState(int count)
        {
            if (randomBoxCountBackImage == null)
                return;

            SetBackImageAlpha(count >= 1 ? 1f : emptyAlpha);
        }

        private void SetBackImageAlpha(float alpha)
        {
            Color c = randomBoxCountBackImage.color;
            c.a = alpha;
            randomBoxCountBackImage.color = c;
        }

        // ===== 아이콘 어필 루프 (클릭 유도) =====

        // 보유 1개 이상: 아이콘을 약간씩 커졌다 돌아오는 루프(뽈록뽈록)로 어필. 0개: 정지.
        private void UpdateIconLoop(int count)
        {
            if (iconImage == null)
                return;

            if (count >= 1)
                StartIconLoop();
            else
                StopIconLoop();
        }

        private void StartIconLoop()
        {
            // 이미 루프 재생 중이면 유지
            if (_iconLoopTween != null && _iconLoopTween.IsActive() && _iconLoopTween.IsPlaying())
                return;

            _iconLoopTween?.Kill();
            iconImage.transform.localScale = _iconBaseScale;
            _iconLoopTween = iconImage.transform
                .DOScale(_iconBaseScale * iconPulseScale, iconPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StopIconLoop()
        {
            _iconLoopTween?.Kill();
            _iconLoopTween = null;
            if (iconImage != null)
                iconImage.transform.localScale = _iconBaseScale;
        }

        // 카운트 증가 시 한 번 크게 팝 → 팝이 끝나면 (보유량이 남아 있으면) 어필 루프 재개
        private void PlayIconPop()
        {
            if (iconImage == null)
                return;

            _iconLoopTween?.Kill();
            _iconLoopTween = null;
            _iconTween?.Kill();

            iconImage.transform.localScale = _iconBaseScale;
            _iconTween = iconImage.transform
                .DOScale(_iconBaseScale * bounceScale, bounceDuration)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo)
                .OnComplete(() =>
                {
                    if (PlayerContext.RandomBoxCount.Value >= 1)
                        StartIconLoop();
                });
        }

        // ===== 공통 =====

        // 대상 Transform을 baseScale에서 scaleMul배까지 커졌다 되돌아오는 튕김 연출
        private void PunchScale(Transform t, ref Tween tween, Vector3 baseScale, float scaleMul, float dur)
        {
            if (t == null)
                return;

            tween?.Kill();
            t.localScale = baseScale;
            tween = t.DOScale(baseScale * scaleMul, dur)
                .SetEase(Ease.OutQuad)
                .SetLoops(2, LoopType.Yoyo);
        }

        // 랜덤박스 패널 열기
        private void OnClickOpenRandomBoxPanel()
        {
            UIRandomboxPanel.CreateOrGet();
        }
    }
}
