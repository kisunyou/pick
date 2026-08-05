using UnityEngine;
using UnityEngine.EventSystems;

namespace FunRabbit
{
    [UIOption(
        Path = "UI2/Prefabs/UIDpad",
        Layer = UILayer.Hud,
        OpenMode = UIOpenMode.Single,
        isPool = false)]
    public class UIDpad : BaseUIView<UIDpad>, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [SerializeField] private RectTransform _touchJoyStick;   // 터치 감지 영역
        [SerializeField] private RectTransform _dpadCenter;      // 움직이는 중앙 원
        [SerializeField] private RectTransform _dpadRadius;      // 반경 원
        [SerializeField] private GameObject[] _moveFocuses;      // 현재 이동 방향 표시 (top_left, top_right, bottom_left, bottom_right 순)
        [SerializeField] private float _focusDeadzone = 0.1f;    // 방향 표시 최소 입력 크기

        private Vector2 _originCenterPos;      // dpadCenter 초기 위치
        private Vector2 _touchStartPos;        // 터치 시작 위치 (dpadRadius 중심)
        private float _radius;                 // dpadRadius 반경
        private bool _isDragging = false;

        public Vector2 Delta { get; private set; } = Vector2.zero;  // 외부에서 읽는 값 (0~1)

        // 델타값 변경 콜백
        public System.Action<Vector2> OnDeltaChanged;

        protected override void Awake()
        {
            base.Awake();
            _originCenterPos = _dpadCenter.anchoredPosition;
            _radius = _dpadRadius.rect.width * 0.5f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isDragging = true;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_dpadRadius.parent,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPos
            );

            _dpadCenter.anchoredPosition = Vector2.zero;
            Delta = Vector2.zero;
            UpdateMoveFocuses(Delta);
            OnDeltaChanged?.Invoke(Delta);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging)
                return;

            // dpadRadius 기준 로컬 좌표로 변환
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _dpadRadius,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPos
            );

            // dpadRadius 중심(0,0) 기준으로 clamp
            float distance = localPos.magnitude;
            Vector2 clampedPos = distance > _radius
                ? localPos.normalized * _radius
                : localPos;

            _dpadCenter.anchoredPosition = clampedPos;

            Delta = clampedPos / _radius;
            //Debug.Log($"Delta: {Delta}");
            UpdateMoveFocuses(Delta);
            OnDeltaChanged?.Invoke(Delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isDragging = false;

            _dpadCenter.anchoredPosition = Vector2.zero;

            Delta = Vector2.zero;
            UpdateMoveFocuses(Delta);
            OnDeltaChanged?.Invoke(Delta);
        }

        // 디패드 방향에 따라 _moveFocuses 활성/비활성 (입력이 데드존 이하이면 전부 비활성)
        private void UpdateMoveFocuses(Vector2 delta)
        {
            if (_moveFocuses == null || _moveFocuses.Length == 0)
                return;

            int activeIndex = -1;

            // 데드존을 넘는 입력일 때만 방향 표시
            if (delta.magnitude >= _focusDeadzone)
            {
                bool isRight = delta.x >= 0f;
                bool isTop = delta.y >= 0f;
                // 0:top_left, 1:top_right, 2:bottom_left, 3:bottom_right
                activeIndex = isTop ? (isRight ? 1 : 0) : (isRight ? 3 : 2);
            }

            for (int i = 0; i < _moveFocuses.Length; i++)
            {
                if (_moveFocuses[i] != null)
                    _moveFocuses[i].SetActive(i == activeIndex);
            }
        }

        // 외부에서 델타값 읽기
        public Vector2 GetDelta()
        {
            return Delta;
        }

        public bool IsDragging()
        {
            return _isDragging;
        }
    }

    // 게임 상태 + 크레인 상태에 따라 D-Pad 표시를 갱신하는 컨트롤
    // Default<T> 싱글톤이므로 UIDpad가 생성되지 않아도 단독으로 동작하며,
    // 표시가 필요한 시점에 UIDpad를 생성/표시한다.
    public class UIDpadControl : Default<UIDpadControl>
    {
        private Crane _crane;

        // getDefault로 처음 생성될 때 호출 — UIDpad 없이도 게임 상태를 구독
        protected override void OnStart()
        {
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        // ReleaseDefault로 해제될 때 호출
        protected override void OnDestroy()
        {
            GameMain.UnsubscribeStatus(OnChangedGameStatus);
            UnsubscribeCrane();
        }

        // 게임 상태 변경 시: 기본은 숨김, 인게임에서는 크레인 상태가 표시를 결정
        private void OnChangedGameStatus(GameStatus status)
        {
            switch (status)
            {
                case GameStatus.LOBBY:
                    UnsubscribeCrane();
                    SetVisible(false);
                    break;

                case GameStatus.INGAME:
                    SetVisible(false);
                    SubscribeCrane();
                    break;
            }
        }

        // 크레인 상태 구독 (구독 즉시 현재 상태가 반영됨)
        private void SubscribeCrane()
        {
            if (_crane == null && Crane.TryGetSetInstance(out Crane crane))
                _crane = crane;

            _crane?.SubscribeStatus(OnChangedCraneStatus);
        }

        private void UnsubscribeCrane()
        {
            _crane?.UnsubscribeStatus(OnChangedCraneStatus);
            _crane = null;
        }

        // 크레인 상태 변경 시: 조작 가능(CONTROL_MOVING)일 때만 D-Pad 노출
        private void OnChangedCraneStatus(int craneStatus)
        {
            SetVisible(craneStatus == CraneStatus.CONTROL_MOVING);
        }

        // 표시 시 UIDpad를 생성/표시, 숨김 시 이미 있으면 숨김 (없으면 무시)
        private void SetVisible(bool visible)
        {
            if (visible)
            {
                UIDpad.CreateOrGet().Show();
            }
            else
            {
                UIDpad dpad = UIDpad.Get();
                if (dpad != null)
                    dpad.Hide();
            }
        }
    }
}