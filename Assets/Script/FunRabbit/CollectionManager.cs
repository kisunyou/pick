using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FunRabbit
{
    // 컬렉션(도감) 연출의 메인 매니저.
    // GameStatus.COLLECTION 진입 시 Initialize되어, 획득한 스테이지의 인형 액터를
    // collection 영역(GameCheckPositions.CollectionArea 하위 Plane들) 위에 생성해 배회시킨다.
    // 이미 생성된 동물은 다시 열어도 중복 생성하지 않는다. (배회/애니 로직은 Actor가 담당)
    public class CollectionManager : Singleton<CollectionManager>
    {
        const float MIN_SPAWN_SEPARATION = 2f; // 생성 위치 간 최소 간격 (겹침 방지)
        const int SPAWN_POSITION_TRIES = 30;   // 겹치지 않는 위치 샘플링 최대 시도 횟수
        const float SPAWN_AREA_MARGIN = 1f;    // 플레인 가장자리 여유

        // 인형 터치(탭) 판정 - 카메라 드래그 패닝과 구분하기 위해 "짧고 거의 안 움직인 입력"만 탭으로 본다
        const float TAP_MAX_MOVE_PIXELS = 20f; // 이 픽셀 이상 움직이면 드래그로 판정
        const float TAP_MAX_DURATION = 0.35f;  // 이 시간(초) 이상 누르고 있으면 탭 아님
        const float TOUCH_RAY_DISTANCE = 500f; // 인형 터치 레이캐스트 최대 거리

        // animalKey -> 생성된 도감 인형 (중복 생성 방지)
        private readonly Dictionary<string, CollectionActor> _spawnedDolls = new Dictionary<string, CollectionActor>();

        // 배회 가능 영역 (Plane 렌더러들의 월드 bounds) - Actor들과 공유
        private readonly List<Bounds> _roamAreas = new List<Bounds>();
        private Transform _collectionArea;
        private float _floorY;
        private bool _isSpawning;

        // 탭 판정용 입력 상태
        private bool _isPressing;
        private bool _isTapCancelled;
        private Vector2 _pressStartPos;
        private float _pressStartTime;

        private void Start()
        {
            GameMain.SubscribeStatus(OnChangedGameStatus);
        }

        private void Update()
        {
            // 컬렉션 모드에서만 인형 터치를 처리한다
            if (!GameMain.IsCheckInstance() || GameMain.Instance.CurrentStatus != GameStatus.COLLECTION)
            {
                _isPressing = false;
                return;
            }

            UpdateDollTouch();
        }

        // 탭(짧고 거의 안 움직인 입력)을 감지해, 그 지점의 컬렉션 인형 울음소리를 재생한다.
        // (마우스/단일 터치는 Unity의 마우스 시뮬레이션으로 통합 처리)
        private void UpdateDollTouch()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _isPressing = true;
                _pressStartPos = Input.mousePosition;
                _pressStartTime = Time.unscaledTime;
                // UI(닫기 버튼 등) 위에서 시작한 입력은 탭으로 취급하지 않는다
                _isTapCancelled = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            }

            if (!_isPressing)
                return;

            // 두 손가락 이상(핀치 줌)이 개입하면 이번 입력은 탭이 아니다
            if (Input.touchCount >= 2)
                _isTapCancelled = true;

            // 이동량/시간이 탭 기준을 넘으면 드래그(패닝)로 판정
            if (((Vector2)Input.mousePosition - _pressStartPos).magnitude > TAP_MAX_MOVE_PIXELS
                || Time.unscaledTime - _pressStartTime > TAP_MAX_DURATION)
                _isTapCancelled = true;

            if (Input.GetMouseButtonUp(0))
            {
                _isPressing = false;

                if (!_isTapCancelled)
                    TryPlayTouchedDollSound(Input.mousePosition);
            }
        }

        // 화면 좌표에서 레이캐스트해 컬렉션 인형이 맞으면 울음소리 재생 + 카메라 포커스 이동.
        private void TryPlayTouchedDollSound(Vector2 screenPos)
        {
            Camera cam = GameCameraManager.Instance != null ? GameCameraManager.Instance.ActiveCamera : null;
            if (cam == null)
                return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, TOUCH_RAY_DISTANCE))
                return;

            Actor actor = hit.collider.GetComponentInParent<Actor>();
            if (actor == null || !actor.IsCollectionMode || actor.Context.Data == null)
                return;

            AudioManager.Instance.PlaySfx(GameActorData.GetSound(actor.Context.Data.animalKey));

            // 탭한 인형이 화면 중앙에 오도록 카메라를 부드럽게 이동
            if (GameCameraManager.Instance.ActiveGameCamera is CollectionCamera collectionCamera)
                collectionCamera.FocusOn(actor.transform.position);
        }

        private void OnChangedGameStatus(GameStatus status)
        {
            bool isCollection = status == GameStatus.COLLECTION;

            // 도감에 있는 동안만 인형을 활성화한다.
            // (이탈 시 배회 코루틴/Animator/kinematic 콜라이더 비용을 전부 끊고,
            //  메인 카메라(Everything 마스크)에 비치는 것도 차단.
            //  재활성화 시 배회 재개는 Actor.OnEnable이 담당)
            SetSpawnedDollsActive(isCollection);

            if (isCollection)
                Initialize();
        }

        private void SetSpawnedDollsActive(bool active)
        {
            foreach (var pair in _spawnedDolls)
            {
                CollectionActor actor = pair.Value;
                if (actor == null)
                    continue;

                if (actor.gameObject.activeSelf != active)
                    actor.gameObject.SetActive(active);

                // 재진입 시 actor.json의 collectionScale 변경분을 기존 인형에도 반영한다
                // (스케일은 생성 시점에 적용되므로, 테이블을 튜닝해도 살아있는 인형은 그대로 남는 문제 방지)
                if (active)
                    ApplyCollectionScale(actor);
            }
        }

        // 테이블의 collectionScale과 다르면 스케일을 갱신하고 바닥 접지를 다시 잡는다
        private void ApplyCollectionScale(CollectionActor actor)
        {
            string animalKey = actor.Context.Data != null ? actor.Context.Data.animalKey : null;
            Vector3 targetScale = Vector3.one * GameActorData.GetCollectionScale(animalKey);

            if (actor.transform.localScale == targetScale)
                return;

            actor.transform.localScale = targetScale;
            GroundToFloor(actor.gameObject);
        }

        // 컬렉션 진입 시 호출: 배회 영역을 준비하고, 획득한 스테이지의 인형을 (아직 없으면) 생성한다.
        public void Initialize()
        {
            if (!PrepareRoamAreas())
                return;

            // 이전 진입의 생성 코루틴이 아직 도는 중이면 그대로 이어간다 (중복 실행 방지)
            if (_isSpawning)
                return;

            StartCoroutine(SpawnClearedStageDolls());
        }

        // collection 오브젝트 하위 Plane 렌더러들의 bounds를 배회 영역으로 수집한다. (최초 1회)
        private bool PrepareRoamAreas()
        {
            if (_roamAreas.Count > 0)
                return true;

            if (!GameCheckPositions.TryGetSetInstance(out GameCheckPositions checkPos)
                || checkPos.CollectionArea == null)
            {
                Debug.LogError("[CollectionManager] GameCheckPositions.CollectionArea가 설정되지 않았습니다.");
                return false;
            }

            _collectionArea = checkPos.CollectionArea;

            float floorY = float.MinValue;
            foreach (var planeRenderer in _collectionArea.GetComponentsInChildren<Renderer>())
            {
                _roamAreas.Add(planeRenderer.bounds);
                floorY = Mathf.Max(floorY, planeRenderer.bounds.max.y);
            }

            if (_roamAreas.Count == 0)
            {
                Debug.LogError("[CollectionManager] collection 영역에 Plane(Renderer)이 없습니다.");
                return false;
            }

            _floorY = floorY;
            return true;
        }

        // 획득한(이미 클리어한) 스테이지의 인형만 순서대로 생성한다.
        // 현재 도전 중인 스테이지 자신은 아직 클리어 전이라 미포함 - UICollectionPanel/GameDollCreator.GetStageQuestPool과 동일 기준.
        private IEnumerator SpawnClearedStageDolls()
        {
            _isSpawning = true;

            int currentStage = GameQuestManager.IsCheckInstance()
                ? GameQuestManager.Instance.CurrentStage
                : 1;

            for (int stage = 1; stage < currentStage; stage++)
            {
                StageQuestData stageData = GameQuestData.GetStage(stage);
                if (stageData == null || string.IsNullOrEmpty(stageData.animalKey))
                    continue;

                // 이미 생성된 동물은 중복 생성하지 않는다
                if (_spawnedDolls.TryGetValue(stageData.animalKey, out CollectionActor existing) && existing != null)
                    continue;

                yield return SpawnDoll(stageData);
            }

            _isSpawning = false;
        }

        private IEnumerator SpawnDoll(StageQuestData stageData)
        {
            string path = stageData.Doll.GetModelPrefabFullPath();
            ResourceRequest request = Resources.LoadAsync<GameObject>(path);
            yield return request;

            GameObject prefab = request.asset as GameObject;
            if (prefab == null)
            {
                Debug.LogError($"[CollectionManager] 프리팹 로드 실패: {path}");
                yield break;
            }

            Vector3 position = PickSpawnPosition();
            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject doll = Instantiate(prefab, position, rotation, _collectionArea);
            doll.name = $"collection_{stageData.animalKey}";

            // actor.json 행의 texture 필드 적용 - 변형(bear_g 등) 인형은 도감에서도 변형색으로 보인다
            GameCommon.ApplyDataTexture(doll, stageData.animalKey);

            // 도감 배회 인형은 actor.json의 collectionScale로 스케일링한다
            // (GroundToFloor보다 먼저 적용해야 스케일 반영된 bounds로 바닥 접지가 계산된다)
            doll.transform.localScale = Vector3.one * GameActorData.GetCollectionScale(stageData.animalKey);

            Actor baseActor = doll.GetComponent<Actor>();
            if (baseActor == null)
            {
                Debug.LogError($"[CollectionManager] Actor 컴포넌트 없음: {path}");
                Destroy(doll);
                yield break;
            }

            // 프리팹에 baked-in된 base Actor를 CollectionActor로 교체한다
            // (Collection/Battle 모드가 동일 프리팹을 공유하므로, 타입 분기는 스폰 시점의 컴포넌트 교체로 처리)
            DestroyImmediate(baseActor);
            CollectionActor actor = doll.AddComponent<CollectionActor>();

            actor.Context.Data = stageData.Doll;
            actor.SetupCollectionMode(_roamAreas);

            GroundToFloor(doll);
            _spawnedDolls[stageData.animalKey] = actor;

            // 비동기 생성 도중 도감에서 이탈했다면 비활성 상태로 대기시킨다 (재진입 시 활성화)
            if (GameMain.IsCheckInstance() && GameMain.Instance.CurrentStatus != GameStatus.COLLECTION)
                doll.SetActive(false);
        }

        // 플레인 위 랜덤 위치를 고른다. 이미 배회 중인 인형들과 최소 간격을 유지한다.
        // (모든 시도가 겹치면 마지막 후보라도 사용 - 미생성보다 낫다)
        private Vector3 PickSpawnPosition()
        {
            Vector3 candidate = default;

            for (int attempt = 0; attempt < SPAWN_POSITION_TRIES; attempt++)
            {
                Bounds area = _roamAreas[Random.Range(0, _roamAreas.Count)];
                float x = Random.Range(area.min.x + SPAWN_AREA_MARGIN, area.max.x - SPAWN_AREA_MARGIN);
                float z = Random.Range(area.min.z + SPAWN_AREA_MARGIN, area.max.z - SPAWN_AREA_MARGIN);
                candidate = new Vector3(x, _floorY, z);

                if (!IsTooCloseToOthers(candidate))
                    break;
            }

            return candidate;
        }

        // 후보 위치가 이미 배회 중인 인형들과 너무 가까운지 검사한다. (인형은 움직이므로 현재 위치 기준)
        private bool IsTooCloseToOthers(Vector3 position)
        {
            float sqrMinSeparation = MIN_SPAWN_SEPARATION * MIN_SPAWN_SEPARATION;

            foreach (var pair in _spawnedDolls)
            {
                CollectionActor other = pair.Value;
                if (other == null)
                    continue;

                Vector3 diff = other.transform.position - position;
                diff.y = 0f;
                if (diff.sqrMagnitude < sqrMinSeparation)
                    return true;
            }

            return false;
        }

        // 인형 렌더러 bounds의 최하단이 플레인 표면에 닿도록 y를 보정한다. (모델별 피벗 차이 흡수)
        private void GroundToFloor(GameObject doll)
        {
            Renderer[] renderers = doll.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            float minY = float.MaxValue;
            foreach (var dollRenderer in renderers)
                minY = Mathf.Min(minY, dollRenderer.bounds.min.y);

            Vector3 pos = doll.transform.position;
            pos.y += _floorY - minY;
            doll.transform.position = pos;
        }
    }
}
