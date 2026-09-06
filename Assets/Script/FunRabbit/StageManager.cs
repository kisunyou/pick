using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class StageManager
    {
        [System.Serializable]
        public class StageData
        {
            public int stageKey;
            public Vector3[] positions;
            public Vector3[] scales;
            public Vector3[] rotations;
            public string[] prefabNames;
        }

        private const string KEY_PREFIX = "StageData_";

        // actor 보관 리스트
        private static readonly List<Actor> actors = new List<Actor>();

        // 보관된 actor 수가 변경될 때 발생하는 이벤트 (현재 개수)
        public static event System.Action<int> OnActorCountChanged;

        // 현재 스테이지에 존재하는 인형(Actor) 수
        public static int ActorCount => actors.Count;

        // 개수 변화 없이 현재 개수로 이벤트를 재발행한다.
        // (인형 리셋 종료 등 외부 상태가 바뀌어 구독자의 재평가가 필요할 때 사용)
        public static void NotifyActorCountChanged()
        {
            OnActorCountChanged?.Invoke(actors.Count);
        }

        // actor 보관 리스트에 actor를 추가한다.
        public static void AddActor(Actor actor)
        {
            if (actor == null)
                return;

            if (!actors.Contains(actor))
            {
                actors.Add(actor);
                OnActorCountChanged?.Invoke(actors.Count);
            }
        }

        // actor 보관 리스트에서 actor를 삭제한다.
        public static void RemoveActor(Actor actor)
        {
            if (actor == null)
                return;

            if (actors.Remove(actor))
                OnActorCountChanged?.Invoke(actors.Count);
        }

        // 지정한 위치 반경 내에 인형(Actor)이 하나라도 있으면 true.
        // (크레인이 들어올린 뒤 집게 근처에 인형이 있는지 = 실제로 잡았는지 판별용)
        public static bool IsAnyActorNear(Vector3 position, float radius)
        {
            float sqrRadius = radius * radius;
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (a == null)
                    continue;

                if ((a.transform.position - position).sqrMagnitude <= sqrRadius)
                    return true;
            }
            return false;
        }

        // 지정한 위치 반경 내의 인형(Actor)들을 results에 담는다. (results는 호출자가 재사용하는 버퍼)
        public static void GetActorsNear(Vector3 position, float radius, List<Actor> results)
        {
            float sqrRadius = radius * radius;
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (a == null)
                    continue;

                if ((a.transform.position - position).sqrMagnitude <= sqrRadius)
                    results.Add(a);
            }
        }

        // actor 보관 리스트의 모든 actor 정보를 stage 키로 json 저장한다.
        public static void Save(int stage)
        {
            int count = actors.Count;

            StageData data = new StageData
            {
                stageKey = stage,
                positions = new Vector3[count],
                scales = new Vector3[count],
                rotations = new Vector3[count],
                prefabNames = new string[count],
            };

            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                if (actor == null)
                    continue;

                Transform t = actor.transform;
                data.positions[i] = t.position;
                data.scales[i] = t.localScale;
                data.rotations[i] = t.eulerAngles;
                data.prefabNames[i] = actor.gameObject.name;
            }

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(KEY_PREFIX + stage, json);
            PlayerPrefs.Save();

            Debug.Log($"[StageManager] Save stage {stage} ({count} actors)\n{json}");
        }

        // stage 키로 저장된 json 데이터를 얻어 반환한다.
        public static StageData GetStage(int stage)
        {
            string key = KEY_PREFIX + stage;
            if (!PlayerPrefs.HasKey(key))
                return null;

            string json = PlayerPrefs.GetString(key);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonUtility.FromJson<StageData>(json);
        }
    }
}
