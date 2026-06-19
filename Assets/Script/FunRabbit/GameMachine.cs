using UnityEngine;

namespace FunRabbit
{
    public class GameMachine : InstanceSetter<GameMachine>
    {
        [SerializeField] GameObject headModelObject;

        public GameObject HeadModelObject
        {
            get { return headModelObject; }
        }

        protected override void Awake()
        {
            base.Awake();
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
        }

        // 게임 상태 변경 시 헤드 모델 표시 갱신
        private void OnChangedGameStatus(GameStatus status)
        {
            if (headModelObject == null)
                return;

            switch (status)
            {
                case GameStatus.LOBBY:
                    headModelObject.SetActive(true);
                    break;

                case GameStatus.INGAME:
                    headModelObject.SetActive(false);
                    break;
            }
        }
    }
}
