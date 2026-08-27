using UnityEngine;
using UnityEngine.UI;

namespace FunRabbit
{
    // ally 액터 머리 위에 떠서 따라다니는 개별 hp 게이지. AllyBattleActor가 스폰 시 CreateOrGet으로 만들고
    // SetTarget(headSocket)으로 따라다닐 3D 위치를, SetHp(ratio)로 hp 비율을 전달한다.
    // 타겟(headSocket)이 사라지면(액터 파괴) 자동으로 자신도 닫는다.
    // 컬렉션(도감) 화면에서는 전투 정보를 노출하지 않으므로(UIHud.SetActiveHud의 bossBattle 숨김과 동일 기준)
    // GameStatus.COLLECTION 동안 슬라이더를 숨긴다. (루트는 살려 두어 타겟 파괴 감지/정리는 계속 동작)
    [UIOption(
        Path = "UI2/Prefabs/UIActorHPGage",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Multiple,
        isPool = false)]
    public class UIActorHPGage : BaseUIView<UIActorHPGage>
    {
        [SerializeField] private Slider hpSlider;

        private RectTransform _rectTransform;
        private Transform _target;

        protected override void Awake()
        {
            base.Awake();
            _rectTransform = (RectTransform)transform;

            // 구독 시 현재 상태로 즉시 1회 반영되므로, 도감 중에 처음 피격돼 생성된 게이지도 바로 숨겨진다
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        protected override void OnDestroy()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            base.OnDestroy();
        }

        // 컬렉션(도감) 화면에서는 전투 정보(hp 게이지)를 숨기고, 그 외 상태에서는 다시 보여준다
        private void OnChangedGameStatus(GameStatus status)
        {
            bool visible = status != GameStatus.COLLECTION;
            if (hpSlider != null && hpSlider.gameObject.activeSelf != visible)
                hpSlider.gameObject.SetActive(visible);
        }

        // 스폰 직후 호출: 이 게이지가 화면상에서 따라다닐 3D 트랜스폼(headSocket)을 지정한다.
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        public void SetHp(float ratio)
        {
            if (hpSlider != null)
                hpSlider.value = ratio;
        }

        private void LateUpdate()
        {
            // 타겟(액터)이 파괴되면 게이지도 함께 정리한다.
            if (_target == null)
            {
                // 앱 종료/씬 전환 중이라 UIManager가 이미 없으면 Close() 대신 직접 파괴한다.
                if (UIManager.IsCheckInstance())
                    Close();
                else
                    Destroy(gameObject);

                return;
            }

            UpdateScreenPosition();
        }

        // headSocket 위치를 화면(HUD) 좌표로 변환해 반영한다 (BossCamera.TryConvertWorldToHudPoint 참고).
        private void UpdateScreenPosition()
        {
            if (BossCamera.TryConvertWorldToHudPoint(_target.position, out Vector3 hudPoint))
                _rectTransform.position = hudPoint;
        }
    }
}
