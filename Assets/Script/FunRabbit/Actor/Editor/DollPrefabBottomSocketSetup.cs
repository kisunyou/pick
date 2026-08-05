using UnityEditor;
using UnityEngine;

namespace FunRabbit
{
    // dollPrefabs 폴더의 모든 프리팹에 bottomSocket(모델 렌더러 기준 최하단 위치)을 추가/재배치하고,
    // Actor의 rigidbody/colliders/bottomSocket SerializeField를 연결한다 (ActorEditor.Apply와 동일 로직 재사용).
    // 이미 bottomSocket이 있는 프리팹은 위치만 갱신한다 - 재실행해도 안전(idempotent).
    public static class DollPrefabBottomSocketSetup
    {
        const string DollPrefabsFolder = "Assets/Resources/Prefabs/dollPrefabs";
        const string BottomSocketName = "bottomSocket";

        [MenuItem("TeenyWorld/Doll Prefabs/bottomSocket 일괄 추가 + Actor 필드 연결")]
        public static void SetupAllDollPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DollPrefabsFolder });

            int processed = 0;
            int skipped = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    Actor actor = root.GetComponent<Actor>();
                    if (actor == null)
                    {
                        Debug.LogWarning($"[DollPrefabBottomSocketSetup] Actor 컴포넌트 없음, 스킵: {path}");
                        skipped++;
                        continue;
                    }

                    if (!TryGetRendererBounds(root, out Bounds bounds))
                    {
                        Debug.LogWarning($"[DollPrefabBottomSocketSetup] Renderer 없음, 스킵: {path}");
                        skipped++;
                        continue;
                    }

                    Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                    Transform bottomSocket = ActorEditor.FindChildByName(root.transform, BottomSocketName);
                    if (bottomSocket == null)
                    {
                        GameObject socketObject = new GameObject(BottomSocketName);
                        socketObject.transform.SetParent(root.transform, false);
                        bottomSocket = socketObject.transform;
                    }

                    bottomSocket.rotation = Quaternion.identity;
                    bottomSocket.position = bottomCenter;

                    ActorEditor.ApplyReferences(actor);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    processed++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DollPrefabBottomSocketSetup] 완료 - 처리 {processed}개 / 스킵 {skipped}개");
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return true;
        }
    }
}
