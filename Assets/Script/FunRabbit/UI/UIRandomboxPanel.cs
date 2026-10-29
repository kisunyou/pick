using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIRandomboxPanel",
        Layer = UILayer.Contents,
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

        // 명시적 닫기(closeButton -> Close)에만 호출된다. 지급 대기 중인 아군 액터 보상을
        // 트레일 연출과 함께 지급한다. (앱 종료로 파괴될 때는 호출되지 않으므로
        // 남은 보상은 재시작 시 ActorBattleSystem이 연출 없이 지급한다)
        public override void OnClose()
        {
            base.OnClose();
            Control.GrantPendingAllyRewardsWithTrail();
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
        // 랜덤박스 열기 연출 시작 시 재생할 효과음 (2배속 재생)
        const string RandomBoxOpenSoundName = "randombox_open";
        const float RandomBoxOpenSoundPitch = 2f;

        // 코인 보상 표시 보류 유지 시간(초). 보상 팝업 확인 전까지는 하단 바 숫자를 올리지 않는다
        // (확인 버튼/팝업 닫힘에서 반드시 풀지만, 누락 대비 만료 안전장치).
        const float CoinDisplayHoldSeconds = 60f;

        private UIRandomboxPanel _panel;
        private bool _opening; // 열기(애니메이션~보상) 진행 중 여부 - 중복 실행 방지
        private ItemData _openedItem;                     // 이번 열기에서 이미 지급(저장)된 보상 - 애니메이션 후 팝업으로 공개
        private UIBottomBar.CoinDisplayHold _coinHold;    // 코인 보상의 하단 바 표시 보류 (확인 시 코인 비행 연출이 풀어줌)

        public void Initialize(UIRandomboxPanel panel)
        {
            _panel = panel;
            _opening = false;
            RefreshView();
        }

        public void Deinitialize()
        {
            // 공개 전에 패널이 파괴돼도 보상은 이미 저장돼 있다 - 표시 보류만 풀어준다
            ReleaseCoinHold();
            RefreshBuffIcons();
            _openedItem = null;
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

        // 열기 버튼 클릭: 추첨 → (랜덤박스 1개 차감 + 보상 지급)을 한 번에 저장 → 오픈 애니메이션 → 보상 공개.
        // 이전에는 차감은 즉시 저장, 추첨은 애니메이션 후, 지급은 팝업 확인 버튼(코인은 비행 도착)에서 해
        // 그 사이 앱이 종료되면 박스만 사라졌다. 이제 애니메이션/팝업/코인 비행은 이미 확정된 결과의 공개 연출이다.
        public void OnClickOpen()
        {
            if (_panel == null || _opening)
                return;

            if (PlayerContext.RandomBoxCount.Value < 1)
                return;

            // 추첨을 먼저 한다 - 추첨 불가(데이터 오류)면 박스를 소비하지 않는다
            ItemData item = DrawRewardItem();
            if (item == null)
                return;

            bool spent = false;
            long grantedCoin = 0;
            PlayerContext.RunAtomic(() =>
            {
                // 랜덤박스 1개 소비 + 보상 지급 (한 번의 PlayerPrefs.Save)
                spent = PlayerContext.SpendRandomBox();
                if (spent)
                    grantedCoin = ApplyReward(item);
            });
            if (!spent)
                return;

            // 지급과 같은 프레임에 하단 바 코인 숫자를 보류한다 (확인 버튼의 코인 비행 도착에 맞춰 올라감)
            ReleaseCoinHold();
            if (grantedCoin > 0 && UIBottomBar.Instance != null)
                _coinHold = UIBottomBar.Instance.HoldCoinDisplay(grantedCoin, CoinDisplayHoldSeconds);
            _openedItem = item;

            // 랜덤박스 오픈 - 매회 기록
            FireBaseAnalyticsManager.Instance.LogEvent("open_random_box");

            _opening = true;
            RefreshView(); // 소비된 카운트 반영 + 버튼 비활성화

            // 오픈 연출 시작과 함께 박스 열기 효과음 재생 (2배속)
            AudioManager.Instance.PlaySfx(RandomBoxOpenSoundName, RandomBoxOpenSoundPitch);

            _panel.PlayOpenAnimation(OnOpenAnimationComplete);
        }

        // 확률 추첨 → 아이템 조회 (아군 랜덤 아이템은 클리어한 액터 중 하나로 확정). 실패 시 null.
        private ItemData DrawRewardItem()
        {
            RandomBoxData box = PickRandomBox();
            if (box == null)
            {
                Debug.LogError("[UIRandomboxPanelControl] 추첨할 랜덤박스 데이터가 없습니다.");
                return null;
            }

            ItemData item = GameItemData.Get(box.itemkey);
            if (item == null)
            {
                Debug.LogError($"[UIRandomboxPanelControl] itemkey {box.itemkey} 에 해당하는 아이템이 없습니다.");
                return null;
            }

            // animalKey가 비어 있는 아군 액터 아이템(item.json key 11/12)은 클리어한 액터 중 하나를
            // 랜덤으로 뽑아 animalKey/icon_path를 채운 복사본으로 바꿔 팝업·지급에 사용한다.
            if (IsRandomAllyItem(item))
            {
                item = ResolveRandomAllyItem(item);
                if (item == null)
                {
                    Debug.LogError($"[UIRandomboxPanelControl] itemkey {box.itemkey}: 클리어한 액터가 없어 아군 액터를 지급할 수 없습니다.");
                    return null;
                }
            }

            return item;
        }

        // 애니메이션 종료 후: 이미 지급된 보상을 팝업으로 공개
        private void OnOpenAnimationComplete()
        {
            if (_openedItem == null)
            {
                ResetPanel();
                return;
            }

            ShowRewardPopup(_openedItem);
        }

        // animalKey가 비어 있는 아군 액터 아이템 = 클리어한 액터 중 랜덤 지급 아이템
        private static bool IsRandomAllyItem(ItemData item)
        {
            return item != null && item.itemType == "allyActor" && string.IsNullOrEmpty(item.animalKey);
        }

        // 클리어한 액터 중 하나를 랜덤으로 뽑아 animalKey/icon_path를 채운 ItemData 복사본을 만든다.
        // GameItemData가 캐시하는 원본을 바꾸지 않도록 반드시 복사본을 반환한다. 후보가 없으면 null.
        private static ItemData ResolveRandomAllyItem(ItemData item)
        {
            List<string> clearedActors = GetClearedActorKeys();
            if (clearedActors.Count == 0)
                return null;

            string animalKey = clearedActors[Random.Range(0, clearedActors.Count)];

            return new ItemData
            {
                key = item.key,
                name = item.name,
                icon_path = GameCommon.GetIconFullPath(animalKey),
                count = item.count,
                itemType = item.itemType,
                itemDescription = item.itemDescription,
                animalKey = animalKey,
            };
        }

        // 현재 스테이지보다 앞(= 이미 클리어된) 스테이지의 목표 액터 animalKey 목록.
        // actor.json에 없는 animalKey는 AddAllyActors가 지급하지 못하므로 제외한다.
        private static List<string> GetClearedActorKeys()
        {
            List<string> result = new List<string>();

            List<StageQuestData> stages = GameQuestData.StageQuestDataList?.stages;
            if (stages == null)
                return result;

            int currentStage = GameQuestManager.Instance.CurrentStage;
            foreach (StageQuestData stage in stages)
            {
                if (stage.stage >= currentStage || string.IsNullOrEmpty(stage.animalKey))
                    continue;

                if (GameActorData.Get(stage.animalKey) == null)
                    continue;

                if (!result.Contains(stage.animalKey))
                    result.Add(stage.animalKey);
            }

            return result;
        }

        // Probability 가중치로 랜덤박스 하나를 추첨한다.
        // 아군 액터 아이템은 현재까지 클리어한 스테이지의 액터만 추첨 대상에 포함한다.
        // (animalKey가 빈 아군 액터 아이템은 추첨 후 OnOpenAnimationComplete에서 클리어한 액터 중 랜덤 확정)
        private RandomBoxData PickRandomBox()
        {
            List<RandomBoxData> boxes = GameRandomBoxData.GetAll();
            if (boxes == null || boxes.Count == 0)
                return null;

            List<RandomBoxData> candidates = new List<RandomBoxData>(boxes.Count);
            foreach (var b in boxes)
            {
                if (IsBoxAvailable(b))
                    candidates.Add(b);
            }

            if (candidates.Count == 0)
                return null;

            int total = 0;
            foreach (var b in candidates)
                total += Mathf.Max(0, b.Probability);

            if (total <= 0)
                return candidates[0];

            int r = Random.Range(0, total);
            int acc = 0;
            foreach (var b in candidates)
            {
                acc += Mathf.Max(0, b.Probability);
                if (r < acc)
                    return b;
            }
            return candidates[candidates.Count - 1];
        }

        // 아군 액터 아이템은 해당 액터의 스테이지를 클리어했을 때만 추첨할 수 있다. 그 외 아이템은 항상 가능.
        // animalKey가 비어 있는(랜덤 지급) 아군 액터 아이템은 클리어한 액터가 1종 이상일 때만 추첨 가능.
        private static bool IsBoxAvailable(RandomBoxData box)
        {
            ItemData item = GameItemData.Get(box.itemkey);
            if (item == null)
                return false;

            if (item.itemType != "allyActor")
                return true;

            if (IsRandomAllyItem(item))
                return GetClearedActorKeys().Count > 0;

            return IsActorStageCleared(item.animalKey);
        }

        // animalKey를 목표로 하는 스테이지가 현재 스테이지보다 앞(= 이미 클리어됨)인지
        private static bool IsActorStageCleared(string animalKey)
        {
            List<StageQuestData> stages = GameQuestData.StageQuestDataList?.stages;
            if (stages == null)
                return false;

            int currentStage = GameQuestManager.Instance.CurrentStage;
            foreach (StageQuestData stage in stages)
            {
                if (stage.animalKey == animalKey)
                    return stage.stage < currentStage;
            }

            return false;
        }

        // 보상 팝업 표시 + 콜백 연결 (OK: 수령 연출 / 닫힘: 패널 초기화). 보상은 이미 지급돼 있다.
        private void ShowRewardPopup(ItemData item)
        {
            Sprite icon = SpriteCache.Get(item.icon_path);

            // item.json의 name/itemDescription 필드는 stringData 키 - 현재 언어 문자열로 변환해 표시
            UIRewardPopup popup = UIRewardPopup.CreateOrGet();
            popup.Set(icon,
                GetItemText(item, item.name),
                () => PlayRewardEffect(item, popup),
                GetItemText(item, item.itemDescription));
            popup.OnClosed = ResetPanel;
        }

        // stringKey를 현재 언어 문자열로 변환한다.
        // 아군 액터 아이템은 템플릿 문자열({0}=동물 이름, {1}=마리수)을 채워 반환한다.
        // 동물 이름 키는 직접 조립하지 않고 actor.json 행의 nameKey를 쓴다 -
        // 변형 키(octopus_g 등)로 조립하면 없는 스트링 키가 만들어진다.
        private static string GetItemText(ItemData item, string stringKey)
        {
            if (item.itemType == "allyActor")
                return LanguageManager.Instance.Get(stringKey,
                    LanguageManager.Instance.Get(GameCommon.GetDollNameStringKey(item.animalKey)), item.count);

            return LanguageManager.Instance.Get(stringKey);
        }

        // 보상 지급: 아이템 수량을 itemType에 맞춰 PlayerContext에 반영한다. 지급한 코인 양을 반환한다.
        // OnClickOpen 의 PlayerContext.RunAtomic 안(박스 차감과 같은 저장 단위)에서만 호출한다.
        private static long ApplyReward(ItemData item)
        {
            if (item == null || item.count <= 0)
                return 0;

            if (item.itemType == "reset")
            {
                PlayerContext.AddResetItemCount(item.count);
                return 0;
            }

            if (item.itemType == "allyActor")
            {
                // 즉시 합류시키지 않고 지급 대기 목록에 저장만 한다 - 패널이 닫힐 때
                // GrantPendingAllyRewardsWithTrail이 트레일 연출과 함께 지급하고,
                // 닫기 전에 앱이 종료되면 재시작 시 ActorBattleSystem이 연출 없이 지급한다.
                PlayerContext.AddPendingAllyReward(item.animalKey, item.count);
                return 0;
            }

            if (item.itemType == "apUp" || item.itemType == "deffenseUp")
            {
                // 보유량(PlayerContext)에 바로 반영한다. HUD 버프 아이콘 갱신은 보상 공개 후(RefreshBuffIcons)
                // (이전: BuffManager 가 없으면 지급 자체가 누락됐다)
                int itemKey = item.itemType == "apUp"
                    ? PlayerContext.ATTACK_POWER_UP_ITEM_KEY
                    : PlayerContext.DEFENSE_UP_ITEM_KEY;
                PlayerContext.AddItemAmount(itemKey, item.count);
                return 0;
            }

            PlayerContext.AddCoinAmount(item.count);
            return item.count;
        }

        // 보상 팝업 확인: 이미 지급된 보상의 수령 연출만 한다.
        // 코인이면 팝업 아이콘 위치에서 UIBottomBar로 코인 비행 연출을 재생하며 보류해 둔 숫자를 풀어준다.
        private void PlayRewardEffect(ItemData item, UIRewardPopup popup)
        {
            UIBottomBar.CoinDisplayHold hold = _coinHold;
            _coinHold = null; // 보류 해제 책임을 코인 연출로 넘긴다
            if (hold == null)
                return;

            RectTransform startPoint = popup != null ? popup.IconTransform : null;
            if (UIBottomBar.Instance != null)
                UIBottomBar.Instance.PlayCoinGetEffect(startPoint, hold);
        }

        private void ReleaseCoinHold()
        {
            if (_coinHold != null && UIBottomBar.Instance != null)
                UIBottomBar.Instance.ReleaseCoinDisplay(_coinHold);
            _coinHold = null;
        }

        // 버프 아이템 보유량을 HUD 아이콘에 반영한다 (AddBuff(…, 0) = 수량 변화 없이 아이콘 갱신만).
        private static void RefreshBuffIcons()
        {
            if (UIHud.Instance == null || UIHud.Instance.BuffManager == null)
                return;

            UIHud.Instance.BuffManager.AddBuff(BuffType.AttackPowerUp, 0);
            UIHud.Instance.BuffManager.AddBuff(BuffType.DefensePowerUp, 0);
        }

        // 패널이 닫힐 때(UIRandomboxPanel.OnClose): 지급 대기 중인 아군 액터 보상을
        // 화면 가운데 -> ally 대기열(UIAllyStackActors)로 날아가는 트레일 연출과 함께 지급한다.
        // 대기 목록 제거는 트레일 도착 시점에 하므로, 연출 도중 앱이 종료돼도
        // 재시작 시 ActorBattleSystem이 남은 보상을 연출 없이 지급한다.
        public void GrantPendingAllyRewardsWithTrail()
        {
            // 다른 트레일(뽑기 미션/인형 획득)이 이미 맡아 날아가는 항목은 제외한다
            List<PlayerContext.PendingAllyReward> rewards = PlayerContext.GetPendingAllyRewards(includeInFlight: false);
            if (rewards.Count == 0)
                return;

            // HUD가 없으면 연출 불가 - 대기 목록에 그대로 남겨 재시작 지급 경로에 맡긴다
            UIHud hud = UIHud.Instance;
            if (hud == null || hud.GetDollTrailHud == null || hud.AllyStackActors == null)
                return;

            // 트레일 시작점 = 화면 가운데 (UIHud 루트는 풀스크린 스트레치)
            RectTransform hudRect = (RectTransform)hud.transform;
            Vector3 startPosition = hudRect.TransformPoint(hudRect.rect.center);
            Vector3 targetPosition = hud.AllyStackActors.transform.position;

            foreach (PlayerContext.PendingAllyReward reward in rewards)
            {
                PlayerContext.MarkPendingAllyRewardInFlight(reward.id);
                hud.GetDollTrailHud.PlayTrail(
                    startPosition,
                    targetPosition,
                    GameCommon.GetIconPrefabFullPath(reward.animalKey),
                    // 꺼내기에 성공했을 때만 합류 (다른 경로가 먼저 지급했으면 중복 합류 없음)
                    () => PlayerContext.DeliverPendingAllyReward(reward));
            }
        }

        // 보상 팝업이 닫힐 때: 애니메이션을 재생 전 상태로 되돌리고 버튼/카운트 갱신
        private void ResetPanel()
        {
            // 확인 없이 팝업이 닫혀도 보류해 둔 코인 표시를 풀고 버프 아이콘을 갱신한다 (보상은 이미 지급됨)
            ReleaseCoinHold();
            RefreshBuffIcons();
            _openedItem = null;

            _opening = false;
            if (_panel == null)
                return;

            _panel.ResetOpenAnimation();
            RefreshView();
        }
    }
}
