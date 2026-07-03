using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public enum GameStatus
    {
        LOBBY,
        INGAME,
    }

    public class GameMain : Singleton<GameMain>
    {
        [SerializeField] public float HorizontalSpeed = 500.0f;
        [SerializeField] public float DownSpeed = 15.0f;
        [SerializeField] public float UpSpeed = 30.0f;

        // 게임 상태 변경 이벤트
        public System.Action<GameStatus> OnChangedStatus { get; set; }
        // 스테이지 로드 완료 신호 (로딩 UI 등 로드 완료 시점이 필요한 곳에서 구독)
        public System.Action OnStageLoaded { get; set; }

        // 현재 게임 상태 (늦게 구독한 쪽에서 즉시 반영할 수 있도록 보관)
        public GameStatus CurrentStatus { get; private set; }
        public bool HasStatus { get; private set; }
        // 스테이지 로드가 한 번이라도 완료됐는지
        public bool IsStageLoaded { get; private set; }

        private void Start()
        {
            PlayerContext.Initialize();
            var uiHUD = UIHud.CreateOrGet();
            

            // UIDpadControl 기본 인스턴스를 깨워 게임/크레인 상태를 구독시킴
            // (UIDpad는 표시가 필요한 시점에 컨트롤이 생성)
            _ = UIDpadControl.getDefault;

            // 생성 시 OnOpen에서 로딩 표시 시작, 이후 흐름은 UILoadingControl이 처리
            UILoading.CreateOrGet();

            SceneLoader.Instance.LoadAsync("Stage0", this.OnLoadedStage);
        }

        private void OnLoadedStage()
        {
            SetGameStatus(GameStatus.LOBBY);
            GameDollCreator.Instance.CreateDolls();

            IsStageLoaded = true;
            OnStageLoaded?.Invoke();
        }

        public void SetGameStatus(GameStatus status)
        {
            if (!GameMachine.TryGetSetInstance(out _))
            {
                Debug.LogError("GameMachine 인스턴스가 존재하지 않습니다.");
                return;
            }

            CurrentStatus = status;
            HasStatus = true;
            OnChangedStatus?.Invoke(status);
        }

        // 상태 변경 구독 (+ 이미 설정된 상태가 있으면 즉시 1회 반영)
        public static void SubscribeStatus(System.Action<GameStatus> handler)
        {
            if (!IsCheckInstance())
                return;

            Instance.OnChangedStatus -= handler;
            Instance.OnChangedStatus += handler;

            if (Instance.HasStatus)
                handler(Instance.CurrentStatus);
        }

        public static void UnsubscribeStatus(System.Action<GameStatus> handler)
        {
            if (IsCheckInstance())
                Instance.OnChangedStatus -= handler;
        }

        // 스테이지 로드 완료 구독 (+ 이미 완료됐으면 즉시 1회 반영)
        public static void SubscribeStageLoaded(System.Action handler)
        {
            if (!IsCheckInstance())
                return;

            Instance.OnStageLoaded -= handler;
            Instance.OnStageLoaded += handler;

            if (Instance.IsStageLoaded)
                handler();
        }

        public static void UnsubscribeStageLoaded(System.Action handler)
        {
            if (IsCheckInstance())
                Instance.OnStageLoaded -= handler;
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(20f, 20f, 160f, 50f), "TEST"))
            {
                UIRandomboxPanel.CreateOrGet();
                //UIHud.CreateOrGet().GetDollTrailHud.PlayGetDollTrail(Vector3.zero, "UI2/Prefabs/MissionIconPrefab/pigMissionIcon", null);
                //UIHud.CreateOrGet().OnTestPlayCoinGetEffect();
                //var questData = GameQuestData.GetStage(5);
                //var nextData = GameQuestData.GetStage(6);
                //var panel = UIMissionClearPanel.CreateOrGet();
                //if (panel != null)
                //    panel.SetData(questData?.GetModelPrefabFullPath(), nextData?.GetModelPrefabFullPath());
                //UIHud.CreateOrGet().ShowTimer(10);
                //int stage = GameQuestManager.Instance.CurrentStage;
                //if (stage >= 0)
                //{
                //    StageManager.Save(stage);
                //    Debug.Log($"[GameMain] StageManager.Save({stage}) 완료");
                //}
                //else
                //{
                //    Debug.LogWarning($"[GameMain] 잘못된 stage 값({stage}), 저장 스킵.");
                //}
            }
        }
    }
}
