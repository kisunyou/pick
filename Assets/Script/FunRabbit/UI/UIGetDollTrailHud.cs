using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace FunRabbit
{
    // 획득한 인형(등)이 start 지점 → target 지점으로 살짝 베지어 곡선을 그리며 날아가는 연출을 담당.
    // prefabName으로 로드한 프리팹은 풀링(재사용)한다.
    public class UIGetDollTrailHud : MonoBehaviour
    {
        [SerializeField] Transform getDollStartTransform;
        [SerializeField] Transform getRandomBoxTargetTransform;

        [Header("연출")]
        [SerializeField] float flyDuration = 1.0f;      // 비행 시간(초)
        [SerializeField] float curveHeightRatio = 0.6f; // U자 곡선이 아래로 파이는 깊이(비행 거리 대비 비율)

        // prefabName -> 로드한 원본 프리팹 (Resources 중복 로드 방지)
        private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        // prefabName -> 재사용 대기 중인 인스턴스 풀
        private readonly Dictionary<string, Queue<GameObject>> _pool = new Dictionary<string, Queue<GameObject>>();
        // 재생 중인 트윈 (파괴 시 정리용)
        private readonly List<Tween> _activeTweens = new List<Tween>();

        public Transform GetRandomBoxTargetTransform => getRandomBoxTargetTransform;

        // 획득 연출 전용 이펙트 프리팹의 Resources 경로 (프리팹 캐시/풀 키로도 그대로 사용)
        // uiGetDollEffect = 구버전(로컬 공간 반짝임), uiGetDollTrailEffect = 월드 공간 꼬리 연출
        // ⚠️ uiGetDollTrailEffect는 UIParticleSystem(실험적 - 게임 뷰 미렌더링) 기반이라 현재 미사용
        const string GetDollEffectPrefabName = "FX/get_doll_effect/uiGetDollTrailEffect";

        // 랜덤박스 획득 트레일로 날릴 아이콘 프리팹 (HUD 랜덤박스 버튼과 같은 스프라이트)
        const string RandomBoxIconPrefabName = "UI2/Prefabs/MissionIconPrefab/randomBoxMissionIcon";

        // uiGetDollEffect 프리팹을 로드해 인형 획득 트레일을 재생한다. (기존과 동일하게 풀링)
        //public void PlayGetDollTrailEffect(System.Action onArriveEvent)
        //{
        //    PlayGetDollTrail(GetDollEffectPrefabName, onArriveEvent);
        //}

        // 랜덤박스 아이콘을 HUD 랜덤박스 버튼으로 날리는 획득 트레일. (기존과 동일하게 풀링)
        // (기존 uiGetDollTrailEffect 파티클은 게임 뷰에서 렌더링되지 않아, ally 인형과
        //  동일한 "아이콘 날리기" 방식으로 교체 - 2026-08-09)
        public void PlayGetRandomBoxTrailEffect(System.Action onArriveEvent)
        {
            PlayGetRandomBoxTrail(RandomBoxIconPrefabName, onArriveEvent);
        }


        public void PlayGetDollTrail(string prefabName, Transform targetTransform, System.Action onArriveEvent)
        {
            if (targetTransform == null)
            {
                Debug.LogError("[UIGetDollTrailHud] PlayGetDollTrail: targetTransform이 없습니다.");
                onArriveEvent?.Invoke();
                return;
            }

            PlayTrail(getDollStartTransform.position, targetTransform.position, prefabName, onArriveEvent);
        }

        public void PlayGetRandomBoxTrail(string prefabName, System.Action onArriveEvent)
        {
            if (getRandomBoxTargetTransform == null)
            {
                Debug.LogError("[UIGetDollTrailHud] PlayGetRandomBoxTrail: getRandomBoxTargetTransform이 없습니다.");
                onArriveEvent?.Invoke();
                return;
            }
            PlayTrail(getDollStartTransform.position, getRandomBoxTargetTransform.position, prefabName, onArriveEvent);
        }

        // start 위치에서 target 위치로 prefabName 프리팹을 베지어 곡선으로 이동시킨다.
        // 도착하면 onArriveEvent를 호출하고, 사용한 인스턴스는 풀로 반환한다.
        public void PlayTrail(Vector3 start, Vector3 target, string prefabName, System.Action onArriveEvent)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                Debug.LogError("[UIGetDollTrailHud] PlayTrail: prefabName이 비어있습니다.");
                onArriveEvent?.Invoke();
                return;
            }

            GameObject instance = GetFromPool(prefabName);
            if (instance == null)
            {
                // 프리팹 로드 실패 등 - 연출 없이 도착 콜백만 보장
                onArriveEvent?.Invoke();
                return;
            }

            Transform t = instance.transform;
            t.SetAsLastSibling();

            // 3차 베지어로 U자형(∪) 경로를 만든다.
            // 시작/도착 사이 1/4, 3/4 지점을 아래로 끌어내려 아래로 훅 내려갔다 올라오는 U 느낌.
            Vector3 p0 = start;
            Vector3 p3 = target;
            Vector3 dir = p3 - p0;
            float depth = dir.magnitude * curveHeightRatio; // U가 아래로 파이는 깊이
            Vector3 p1 = p0 + dir * 0.25f + Vector3.down * depth;
            Vector3 p2 = p0 + dir * 0.75f + Vector3.down * depth;

            t.position = p0;

            float progress = 0f;
            Tween tween = DOTween.To(() => progress, x =>
                {
                    progress = x;
                    if (t == null)
                        return;
                    // 3차 베지어: (1-t)^3 P0 + 3(1-t)^2 t P1 + 3(1-t) t^2 P2 + t^3 P3
                    float u = 1f - progress;
                    t.position = u * u * u * p0
                               + 3f * u * u * progress * p1
                               + 3f * u * progress * progress * p2
                               + progress * progress * progress * p3;
                }, 1f, flyDuration)
                .SetEase(Ease.InOutQuad);

            tween.OnComplete(() =>
            {
                _activeTweens.Remove(tween);
                onArriveEvent?.Invoke();
                ReturnToPool(prefabName, instance);
            });

            _activeTweens.Add(tween);
        }

        // 풀에 대기 중인 인스턴스가 있으면 재사용, 없으면 프리팹을 로드해 새로 생성한다.
        private GameObject GetFromPool(string prefabName)
        {
            if (_pool.TryGetValue(prefabName, out var queue))
            {
                while (queue.Count > 0)
                {
                    GameObject reused = queue.Dequeue();
                    if (reused != null)
                    {
                        reused.SetActive(true);
                        return reused;
                    }
                }
            }

            GameObject prefab = LoadPrefab(prefabName);
            if (prefab == null)
                return null;

            GameObject created = Instantiate(prefab, transform);
            created.SetActive(true);
            return created;
        }

        // 사용이 끝난 인스턴스를 비활성화하고 prefabName별 풀에 넣는다.
        private void ReturnToPool(string prefabName, GameObject instance)
        {
            if (instance == null)
                return;

            instance.SetActive(false);

            if (!_pool.TryGetValue(prefabName, out var queue))
            {
                queue = new Queue<GameObject>();
                _pool[prefabName] = queue;
            }
            queue.Enqueue(instance);
        }

        // prefabName으로 Resources에서 프리팹을 로드한다. (캐시)
        private GameObject LoadPrefab(string prefabName)
        {
            if (_prefabCache.TryGetValue(prefabName, out var cached) && cached != null)
                return cached;

            GameObject prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogError($"[UIGetDollTrailHud] 프리팹 로드 실패: {prefabName}");
                return null;
            }

            _prefabCache[prefabName] = prefab;
            return prefab;
        }

        private void OnDestroy()
        {
            foreach (var tween in _activeTweens)
                tween?.Kill();
            _activeTweens.Clear();
        }
    }
}
