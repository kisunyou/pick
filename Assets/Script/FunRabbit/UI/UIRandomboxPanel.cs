using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIRandomboxPanel",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIRandomboxPanel : BaseUIView<UIRandomboxPanel>
    {
        [SerializeField] Button openButton;
        [SerializeField] Animator doll_random_open;
        [SerializeField] Button closeButton;
        [SerializeField] TextMeshProUGUI randomBoxCountText;

        public UIRandomboxPanelControl Control { get; private set; } = new UIRandomboxPanelControl();

        private Coroutine _openRoutine;

        void Start()
        {
            if (openButton != null)
                openButton.onClick.AddListener(() => Control.OnClickOpen());

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            // 애니메이션은 재생 전(첫 프레임 정지) 상태로 두고, 카운트/버튼 상태 초기화
            ResetOpenAnimation();
            Control.Initialize(this);
        }

        // ===== View (UIRandomboxPanelControl이 호출) =====

        public void SetRandomBoxCountText(int count)
        {
            if (randomBoxCountText != null)
                randomBoxCountText.text = count.ToString();
        }

        public void SetOpenButtonInteractable(bool interactable)
        {
            if (openButton != null)
                openButton.interactable = interactable;
        }

        public void SetCloseButtonInteractable(bool interactable)
        {
            if (closeButton != null)
                closeButton.interactable = interactable;
        }

        // 오픈 애니메이션을 재생 전 상태(기본 상태 첫 프레임에서 정지)로 되돌린다.
        public void ResetOpenAnimation()
        {
            if (doll_random_open == null)
                return;

            if (_openRoutine != null)
            {
                StopCoroutine(_openRoutine);
                _openRoutine = null;
            }

            doll_random_open.Rebind();     // 기본 상태 + 첫 프레임으로 리셋
            doll_random_open.Update(0f);   // 첫 프레임 즉시 반영
            doll_random_open.speed = 0f;   // 다시 멈춤
        }

        // 오픈 애니메이션을 재생하고, 끝나면 onComplete를 호출한다.
        public void PlayOpenAnimation(System.Action onComplete)
        {
            if (doll_random_open == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (_openRoutine != null)
                StopCoroutine(_openRoutine);
            _openRoutine = StartCoroutine(PlayOpenAnimationRoutine(onComplete));
        }

        private IEnumerator PlayOpenAnimationRoutine(System.Action onComplete)
        {
            doll_random_open.speed = 1f;

            // 재생 속도(speed) 영향을 받지 않는 "클립 원본 길이"로 대기한다.
            // (speed=0 상태에서 AnimatorStateInfo.length를 읽으면 Infinity가 나오므로 사용하지 않는다.)
            float length = GetCurrentClipLength();
            yield return new WaitForSeconds(length);

            doll_random_open.speed = 0f;   // 마지막 프레임에서 정지 유지

            _openRoutine = null;
            onComplete?.Invoke();
        }

        // 현재 재생 중인 상태의 클립 길이(초). 못 구하면 1초 폴백.
        private float GetCurrentClipLength()
        {
            AnimatorClipInfo[] clips = doll_random_open.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
                return clips[0].clip.length;

            return 1f;
        }

        protected override void OnDestroy()
        {
            Control.Deinitialize();
            base.OnDestroy();
        }
    }

    // UIRandomboxPanel의 로직(카운트 표시 / 열기 / 확률 추첨 / 보상)을 담당하는 컨트롤
    public class UIRandomboxPanelControl
    {
        private UIRandomboxPanel _panel;
        private bool _opening; // 열기(애니메이션~보상) 진행 중 여부 - 중복 실행 방지

        public void Initialize(UIRandomboxPanel panel)
        {
            _panel = panel;
            _opening = false;
            RefreshView();
        }

        public void Deinitialize()
        {
            _panel = null;
        }

        // PlayerContext의 RandomBoxCount로 카운트 텍스트/버튼 상태를 갱신한다.
        public void RefreshView()
        {
            if (_panel == null)
                return;

            int count = PlayerContext.RandomBoxCount.Value;
            _panel.SetRandomBoxCountText(count);
            // 진행 중이 아니고, 1개 이상 보유 시에만 열기 버튼 활성화
            _panel.SetOpenButtonInteractable(!_opening && count >= 1);
            // 애니메이션~보상 연출 중에는 닫기 버튼 비활성화
            _panel.SetCloseButtonInteractable(!_opening);
        }

        // 열기 버튼 클릭: 랜덤박스 1개 소비 → 오픈 애니메이션 재생
        public void OnClickOpen()
        {
            if (_panel == null || _opening)
                return;

            if (PlayerContext.RandomBoxCount.Value < 1)
                return;

            // 랜덤박스 1개 소비
            if (!PlayerContext.SpendRandomBox())
                return;

            _opening = true;
            RefreshView(); // 소비된 카운트 반영 + 버튼 비활성화

            _panel.PlayOpenAnimation(OnOpenAnimationComplete);
        }

        // 애니메이션 종료 후: 확률 추첨 → 아이템 조회 → 보상 팝업 표시
        private void OnOpenAnimationComplete()
        {
            RandomBoxData box = PickRandomBox();
            if (box == null)
            {
                Debug.LogError("[UIRandomboxPanelControl] 추첨할 랜덤박스 데이터가 없습니다.");
                ResetPanel();
                return;
            }

            ItemData item = GameItemData.Get(box.itemkey);
            if (item == null)
            {
                Debug.LogError($"[UIRandomboxPanelControl] itemkey {box.itemkey} 에 해당하는 아이템이 없습니다.");
                ResetPanel();
                return;
            }

            ShowRewardPopup(item);
        }

        // Probability 가중치로 랜덤박스 하나를 추첨한다.
        private RandomBoxData PickRandomBox()
        {
            List<RandomBoxData> boxes = GameRandomBoxData.GetAll();
            if (boxes == null || boxes.Count == 0)
                return null;

            int total = 0;
            foreach (var b in boxes)
                total += Mathf.Max(0, b.Probability);

            if (total <= 0)
                return boxes[0];

            int r = Random.Range(0, total);
            int acc = 0;
            foreach (var b in boxes)
            {
                acc += Mathf.Max(0, b.Probability);
                if (r < acc)
                    return b;
            }
            return boxes[boxes.Count - 1];
        }

        // 보상 팝업 표시 + 콜백 연결 (OK: 보상 지급 / 닫힘: 패널 초기화)
        private void ShowRewardPopup(ItemData item)
        {
            Sprite icon = Resources.Load<Sprite>(item.icon_path);

            UIRewardPopup popup = UIRewardPopup.CreateOrGet();
            popup.Set(icon, item.name, () => GrantReward(item));
            popup.OnClosed = ResetPanel;
        }

        // 보상 지급: 아이템 수량(코인)을 PlayerContext에 반영한다.
        private void GrantReward(ItemData item)
        {
            if (item == null || item.count <= 0)
                return;

            PlayerContext.AddCoinAmount(item.count);
        }

        // 보상 팝업이 닫힐 때: 애니메이션을 재생 전 상태로 되돌리고 버튼/카운트 갱신
        private void ResetPanel()
        {
            _opening = false;
            if (_panel == null)
                return;

            _panel.ResetOpenAnimation();
            RefreshView();
        }
    }
}
