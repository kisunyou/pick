using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace FunRabbit
{
    // 월드(3D) 공간 원샷 이펙트 재생기. Resources에서 프리팹을 로드해 지정 월드 위치에
    // 그대로 재생하고, 재생이 끝나면 비활성화해 풀로 반환한다. (UI 캔버스와 무관한 순수 3D 재생)
    public class WorldFxPlayer : Singleton<WorldFxPlayer>
    {
        // prefabName -> 로드한 원본 프리팹 (Resources 중복 로드 방지)
        private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        // prefabName -> 재사용 대기 중인 인스턴스 풀
        private readonly Dictionary<string, Queue<GameObject>> _pool = new Dictionary<string, Queue<GameObject>>();
        // prefabName -> 재생 시간 (모든 파티클의 duration + startLifetime 최대치)
        private readonly Dictionary<string, float> _durationCache = new Dictionary<string, float>();
        // 반환 대기 중인 트윈
        private readonly List<Tween> _activeTweens = new List<Tween>();

        // prefabName 프리팹을 월드 위치에 1회 재생한다. 재생이 끝나면 자동으로 풀에 반환된다.
        public void Play(string prefabName, Vector3 worldPos, float scale = 1f)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                Debug.LogError("[WorldFxPlayer] Play: prefabName이 비어있습니다.");
                return;
            }

            GameObject instance = GetFromPool(prefabName);
            if (instance == null)
                return;

            Transform t = instance.transform;
            t.position = worldPos;
            t.localScale = Vector3.one * scale;

            // 파티클 재생이 모두 끝난 뒤 풀로 반환 (인스턴스가 먼저 파괴돼도 null 가드로 무해)
            Tween tween = null;
            tween = DOVirtual.DelayedCall(GetEffectDuration(prefabName, instance), () =>
            {
                _activeTweens.Remove(tween);
                ReturnToPool(prefabName, instance);
            });
            _activeTweens.Add(tween);
        }

        // 프리팹 내 모든 파티클의 (duration + startLifetime 최대치) 중 최댓값. 프리팹당 1회 계산 후 캐시.
        private float GetEffectDuration(string prefabName, GameObject instance)
        {
            if (_durationCache.TryGetValue(prefabName, out float cached))
                return cached;

            float duration = 0.5f; // 파티클이 없어도 보장하는 최소 재생 시간
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                duration = Mathf.Max(duration, main.duration + main.startLifetime.constantMax);
            }

            _durationCache[prefabName] = duration;
            return duration;
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

            // 자신(원점, 스케일 1) 아래에 모아두기만 할 뿐 위치/크기에는 영향이 없다.
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
                Debug.LogError($"[WorldFxPlayer] 프리팹 로드 실패: {prefabName}");
                return null;
            }

            _prefabCache[prefabName] = prefab;
            return prefab;
        }
    }
}
