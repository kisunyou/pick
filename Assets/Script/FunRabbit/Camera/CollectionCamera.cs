using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FunRabbit
{
    // 컬렉션(도감) 화면 전용 카메라.
    // 현재는 씬에 배치된 위치/회전을 그대로 사용한다.
    // 활성화되면 pickMachine(인형뽑기 기계)을 가려서 컬렉션 화면과 겹치지 않게 하고,
    // 비활성화되면(다른 모드로 전환) 다시 보이게 되돌린다.
    //
    // 드래그로 X/Z 평면을 패닝한다 - 지도 앱처럼 "손가락으로 바닥을 잡고 끄는" 방식이다.
    // 터치한 지점의 바닥(도감 플레인 높이) 월드 좌표를 잡아 두고, 손가락이 움직이면 그 지점이
    // 계속 손가락 아래에 있도록 카메라를 옮긴다(해상도·줌과 무관하게 1:1, 상하좌우 모두 같은 방향).
    // 놓으면 마지막 드래그 속도로 관성 이동하다 감속해 멈춘다(Unity ScrollRect와 동일한 감속 공식).
    // 놓기 직전 손가락이 멈춰 있었다면(속도가 이미 0에 수렴) 관성 없이 그 자리에서 바로 멈춘다.
    // 이동 범위 끝에 닿으면 잡은 지점을 다시 잡아(re-anchor) 손가락을 되돌리는 즉시 반응한다.
    //
    // 줌인/줌아웃은 카메라 정면 방향으로 달리(dolly)한다 - PC는 마우스 스크롤, 모바일은 두 손가락
    // 핀치로 동작하며(1손가락 드래그와 동시 처리되지 않도록 분리), 최초 활성화 위치 기준
    // maxZoomIn/maxZoomOut 범위로 제한된다.
    public class CollectionCamera : GameCamera
    {
        [SerializeField] GameObject pickMachine;

        [Header("드래그 패닝")]
        [SerializeField] float dragSensitivity = 0.01f;    // 바닥 평면을 구할 수 없을 때(폴백)만 쓰는 스크린 픽셀 1당 월드 이동량
        [SerializeField] float velocitySmoothRate = 10f;   // 드래그 속도 스무딩 정도 (클수록 즉각 반응)
        [SerializeField] float maxMomentumSpeed = 40f;     // 관성 시작 속도 상한(units/s) - 순간 튐으로 멀리 날아가지 않게
        [SerializeField] float decelerationRate = 0.135f;  // 놓은 뒤 초당 감속 배율 (Unity ScrollRect 기본값)
        [SerializeField] float stopVelocitySqr = 0.01f;    // 속도(units/s)의 제곱이 이 값 미만이면 완전히 멈춤

        [Header("줌 인/아웃")]
        [SerializeField] float scrollZoomSensitivity = 1f;    // PC 마우스 스크롤 1노치당 이동량(units)
        [SerializeField] float pinchZoomSensitivity = 0.02f;  // 모바일 핀치 픽셀 변화 1당 이동량(units)
        [SerializeField] float maxZoomIn = 5f;                 // 최초 위치에서 정면으로 최대 이만큼 가까워질 수 있음
        [SerializeField] float maxZoomOut = 3f;                // 최초 위치에서 뒤로 최대 이만큼 멀어질 수 있음

        [Header("이동 범위 제한")]
        [SerializeField] float maxPanX = 8f;          // 최초 위치 기준 X축 최대 이동 범위 (±)
        [SerializeField] float maxPanZPositive = 20f; // 최초 위치 기준 Z축 + 방향 최대 이동 범위
        [SerializeField] float maxPanZNegative = 5f;  // 최초 위치 기준 Z축 - 방향 최대 이동 범위

        [Header("포커스 이동 (인형 탭)")]
        [SerializeField] float focusMoveDuration = 0.6f; // 대상까지 이동하는 시간(초)

        public override CameraMode Mode => CameraMode.Collection;

        private bool _isDragging;
        private Vector2 _lastPointerScreenPos;
        private bool _hasGrabPoint;      // 바닥 평면 위 잡은 지점이 유효한지 (없으면 픽셀 비례 폴백)
        private Vector3 _grabWorldPoint; // 드래그 시작 시 손가락 아래 바닥 월드 좌표
        private float? _groundY;         // 도감 바닥 높이 (CollectionArea 플레인 상단) - 최초 1회 계산
        private Vector3 _dragVelocity; // 월드 X/Z 평면 속도 (units/s) - 드래그 중엔 최근 속도, 뗀 후엔 관성 속도

        // 최초 활성화 위치 기준 정면 방향 누적 이동량(zoom) - 세션(모드 전환)이 바뀌어도 리셋하지 않는다.
        // (리셋하면 재진입할 때마다 한도가 그만큼 더 늘어나 결과적으로 한도를 벗어나게 된다)
        private float _zoomOffset;
        private bool _isPinching;
        private float _lastPinchDistance;

        // 최초(Awake 시점) 카메라 위치 - X/Z 이동 범위 제한의 기준점
        private Vector3 _basePosition;

        // 인형 탭 포커스 이동 트윈 (드래그/줌 입력이 들어오면 즉시 중단)
        private Tween _focusTween;

        protected override void Awake()
        {
            base.Awake();
            _basePosition = MainCamera.transform.position;
        }

        public override void OnActivate()
        {
            base.OnActivate();

            //if (pickMachine != null)
            //    pickMachine.SetActive(false);

            // 이전 세션의 드래그/관성 상태를 남기지 않는다
            _isDragging = false;
            _dragVelocity = Vector3.zero;
        }

        public override void OnDeactivate()
        {
            base.OnDeactivate();

            //if (pickMachine != null)
            //    pickMachine.SetActive(true);

            CancelFocus();
        }

        // ── 인형 탭 포커스 이동 ─────────────────────────────────────
        // 지정 월드 위치(인형)가 화면 중앙에 오도록 카메라를 부드럽게 평행 이동시킨다.
        // 카메라가 내려다보는 각도이므로, "카메라 중앙 시선이 대상 높이 평면과 만나는 지점"과
        // 대상의 XZ 차이만큼 이동해야 대상이 정중앙에 온다. (높이/줌은 유지)
        public void FocusOn(Vector3 worldPosition)
        {
            Transform camTransform = MainCamera.transform;
            Vector3 forward = camTransform.forward;

            // 시선이 수평에 가까우면 평면 교점을 구할 수 없다 (현재 셋업에선 발생하지 않음)
            if (Mathf.Abs(forward.y) < 0.01f)
                return;

            float distance = (worldPosition.y - camTransform.position.y) / forward.y;
            if (distance <= 0f)
                return; // 대상이 시선 뒤쪽 - 이동 불가

            Vector3 lookPoint = camTransform.position + forward * distance;

            Vector3 delta = worldPosition - lookPoint;
            delta.y = 0f;

            // 팬 범위 제한 안으로 클램프한 목표 위치
            Vector3 target = camTransform.position + delta;
            target.x = Mathf.Clamp(target.x, _basePosition.x - maxPanX, _basePosition.x + maxPanX);
            target.z = Mathf.Clamp(target.z, _basePosition.z - maxPanZNegative, _basePosition.z + maxPanZPositive);

            // 진행 중인 관성/이전 포커스는 중단하고 새 이동 시작
            _dragVelocity = Vector3.zero;
            CancelFocus();
            _focusTween = camTransform.DOMove(target, focusMoveDuration)
                .SetEase(Ease.InOutQuad)
                .OnComplete(() => _focusTween = null);
        }

        // 포커스 이동 중단 (사용자 드래그/줌이 개입하면 즉시 제어권을 돌려준다)
        private void CancelFocus()
        {
            if (_focusTween == null)
                return;

            _focusTween.Kill();
            _focusTween = null;
        }

        // 이 카메라가 비활성(모드가 다름) 상태면 GameObject 자체가 꺼져 있어 Update가 호출되지 않는다.
        private void Update()
        {
            // 두 손가락 이상이면 핀치 줌으로 처리하고, 1손가락 팬은 이번 프레임에서 건너뛴다
            // (팬과 줌 제스처가 동시에 충돌하지 않도록 분리)
            if (Input.touchCount >= 2)
            {
                if (_isDragging)
                    EndDrag();

                UpdatePinchZoom();
            }
            else
            {
                _isPinching = false;

                UpdatePointerState(out bool isPointerDown, out Vector2 pointerScreenPos);

                if (isPointerDown)
                {
                    if (!_isDragging)
                        BeginDrag(pointerScreenPos);
                    else
                        ContinueDrag(pointerScreenPos);
                }
                else if (_isDragging)
                {
                    EndDrag();
                }
                else
                {
                    ApplyMomentum();
                }

                UpdateScrollZoom();
            }

            ClampPanRange();
        }

        // 팬/줌으로 이동한 위치가 최초 위치 기준 X/Z 최대 범위를 벗어나지 않도록 자른다.
        // Z축은 +/- 방향 한도가 다르다(maxPanZPositive/maxPanZNegative).
        private void ClampPanRange()
        {
            // 포커스 트윈이 위치를 움직이는 중에는 트윈 목표가 이미 범위 안으로 잘려 있다
            MainCamera.transform.position = ClampToPanRange(MainCamera.transform.position);
        }

        private Vector3 ClampToPanRange(Vector3 pos)
        {
            pos.x = Mathf.Clamp(pos.x, _basePosition.x - maxPanX, _basePosition.x + maxPanX);
            pos.z = Mathf.Clamp(pos.z, _basePosition.z - maxPanZNegative, _basePosition.z + maxPanZPositive);
            return pos;
        }

        // 마우스/터치를 하나로 통일해 포인터가 눌려있는지와 화면 좌표를 반환한다.
        private void UpdatePointerState(out bool isPointerDown, out Vector2 screenPos)
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                screenPos = touch.position;
                isPointerDown = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
                return;
            }

            screenPos = Input.mousePosition;
            isPointerDown = Input.GetMouseButton(0);
        }

        private void BeginDrag(Vector2 pointerScreenPos)
        {
            // 닫기 버튼 등 UI 위에서 시작한 터치는 드래그로 취급하지 않는다
            if (IsPointerOverUI())
                return;

            _isDragging = true;
            _lastPointerScreenPos = pointerScreenPos;
            _hasGrabPoint = TryGetGroundPoint(pointerScreenPos, out _grabWorldPoint);
            _dragVelocity = Vector3.zero; // 새 드래그를 잡으면 이전 관성은 취소 (스크롤뷰를 손으로 잡아 멈추는 느낌)
            CancelFocus();                // 포커스 이동 중이어도 손을 대면 즉시 제어권을 가져온다
        }

        private void ContinueDrag(Vector2 pointerScreenPos)
        {
            Vector2 screenDelta = pointerScreenPos - _lastPointerScreenPos;
            _lastPointerScreenPos = pointerScreenPos;

            if (screenDelta == Vector2.zero || Time.deltaTime <= 0f)
                return;

            Transform camTransform = MainCamera.transform;
            Vector3 before = camTransform.position;

            // 잡은 바닥 지점이 지금 손가락 아래로 오도록 카메라를 옮긴다 (평행 이동이라 한 번에 정확히 맞는다)
            Vector3 worldDelta;
            if (_hasGrabPoint && TryGetGroundPoint(pointerScreenPos, out Vector3 currentPoint))
            {
                worldDelta = _grabWorldPoint - currentPoint;
                worldDelta.y = 0f;
            }
            else
            {
                worldDelta = ScreenDeltaToWorldXZ(screenDelta) * dragSensitivity;
            }

            camTransform.position = ClampToPanRange(before + worldDelta);

            // 범위 끝에서 잘렸으면 지금 손가락 아래 지점을 다시 잡는다 - 손가락을 되돌리는 즉시 반응하게
            Vector3 applied = camTransform.position - before;
            if (_hasGrabPoint && (applied - worldDelta).sqrMagnitude > 0.0001f)
                _hasGrabPoint = TryGetGroundPoint(pointerScreenPos, out _grabWorldPoint);

            // 최근 속도로 스무딩 - 뗄 당시 손가락이 멈춰 있었다면 자연히 0에 수렴해 관성이 남지 않는다
            // (실제로 움직인 양 기준이라 범위 끝에 막힌 방향으로는 관성이 생기지 않는다)
            Vector3 instantVelocity = applied / Time.deltaTime;
            _dragVelocity = Vector3.Lerp(_dragVelocity, instantVelocity, Time.deltaTime * velocitySmoothRate);
        }

        private void EndDrag()
        {
            _isDragging = false;
            _hasGrabPoint = false;
            _dragVelocity = Vector3.ClampMagnitude(_dragVelocity, maxMomentumSpeed);
            // _dragVelocity에 남아있는 마지막 속도로 다음 프레임부터 ApplyMomentum이 관성 이동을 이어간다
        }

        // 모바일 터치는 fingerId로 물어야 UI 위 여부가 정확하다 (인자 없는 호출은 마우스 포인터 기준)
        public static bool IsPointerOverUI()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            for (int i = 0; i < Input.touchCount; i++)
            {
                if (eventSystem.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                    return true;
            }

            return Input.touchCount == 0 && eventSystem.IsPointerOverGameObject();
        }

        // 화면 좌표의 시선이 도감 바닥 평면과 만나는 월드 좌표를 구한다
        private bool TryGetGroundPoint(Vector2 screenPos, out Vector3 point)
        {
            point = default;

            Ray ray = MainCamera.ScreenPointToRay(screenPos);
            Plane ground = new Plane(Vector3.up, new Vector3(0f, GetGroundY(), 0f));
            if (!ground.Raycast(ray, out float enter) || enter <= 0f)
                return false;

            point = ray.GetPoint(enter);
            return true;
        }

        // 도감 바닥 높이 = CollectionArea 하위 플레인 렌더러 bounds 상단 (CollectionManager와 같은 기준)
        private float GetGroundY()
        {
            if (_groundY.HasValue)
                return _groundY.Value;

            float groundY = 0f;
            if (GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos) && checkPos.CollectionArea != null)
            {
                Renderer[] planes = checkPos.CollectionArea.GetComponentsInChildren<Renderer>();
                if (planes.Length > 0)
                {
                    groundY = float.MinValue;
                    foreach (Renderer plane in planes)
                        groundY = Mathf.Max(groundY, plane.bounds.max.y);
                }
            }

            _groundY = groundY;
            return groundY;
        }

        // 드래그가 끝난 뒤: 남은 속도로 이동하며 감속하다, 충분히 느려지면 완전히 멈춘다
        private void ApplyMomentum()
        {
            if (_dragVelocity.sqrMagnitude < stopVelocitySqr)
            {
                _dragVelocity = Vector3.zero;
                return;
            }

            Vector3 before = MainCamera.transform.position;
            MainCamera.transform.position = ClampToPanRange(before + _dragVelocity * Time.deltaTime);

            // 범위 끝에 막힌 축의 관성은 버린다 (벽에 붙은 채 남은 속도로 다른 축만 미끄러지지 않게)
            Vector3 applied = MainCamera.transform.position - before;
            Vector3 expected = _dragVelocity * Time.deltaTime;
            if (Mathf.Abs(applied.x - expected.x) > 0.0001f) _dragVelocity.x = 0f;
            if (Mathf.Abs(applied.z - expected.z) > 0.0001f) _dragVelocity.z = 0f;

            _dragVelocity *= Mathf.Pow(decelerationRate, Time.deltaTime);
        }

        // 스크린 픽셀 delta를 카메라 기준 X/Z 평면 월드 방향으로 변환한다. (바닥 평면을 못 구할 때의 폴백)
        // 바닥을 잡고 끄는 방식과 같게 카메라는 손가락 반대 방향으로 움직인다 (화면 속 내용이 손가락을 따라온다).
        private Vector3 ScreenDeltaToWorldXZ(Vector2 screenDelta)
        {
            Transform camTransform = MainCamera.transform;

            Vector3 right = camTransform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 forward = camTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            return -right * screenDelta.x - forward * screenDelta.y;
        }

        // PC: 마우스 스크롤 휠로 줌. 휠을 위로(앞으로) 굴리면 줌 인.
        private void UpdateScrollZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f))
                return;

            ApplyZoomDelta(scroll * scrollZoomSensitivity);
        }

        // 모바일: 두 손가락 사이 거리 변화로 줌. 손가락을 벌리면(거리 증가) 줌 인.
        private void UpdatePinchZoom()
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);
            float distance = Vector2.Distance(t0.position, t1.position);

            // 핀치를 새로 시작한 프레임(손가락 중 하나가 막 닿은 프레임)은 델타 대신 거리만 기록
            if (!_isPinching || t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
            {
                _isPinching = true;
                _lastPinchDistance = distance;
                return;
            }

            float pinchDelta = distance - _lastPinchDistance;
            _lastPinchDistance = distance;

            ApplyZoomDelta(pinchDelta * pinchZoomSensitivity);
        }

        // 카메라 정면 방향으로 delta만큼 이동시키되, 최초 활성화 위치 기준 [-maxZoomOut, +maxZoomIn]
        // 범위를 벗어나지 않도록 실제 적용량을 clamp한다.
        private void ApplyZoomDelta(float delta)
        {
            float newOffset = Mathf.Clamp(_zoomOffset + delta, -maxZoomOut, maxZoomIn);
            float appliedDelta = newOffset - _zoomOffset;
            if (Mathf.Approximately(appliedDelta, 0f))
                return;

            CancelFocus(); // 포커스 이동 중 줌 입력이 들어오면 트윈이 위치를 덮어쓰지 않도록 중단

            _zoomOffset = newOffset;
            MainCamera.transform.position += MainCamera.transform.forward * appliedDelta;
        }
    }
}
