using UnityEditor;
using UnityEngine;

namespace FunRabbit
{
    // Actor(및 서브클래스) 인스펙터에 Apply 버튼을 추가한다.
    // 누르면 rigidbody/colliders는 현재 transform과 하위 자식에서, bottomSocket은 같은 이름의
    // 자식 오브젝트를 찾아 자동으로 연결해준다.
    [CustomEditor(typeof(Actor), true)]
    public class ActorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            if (GUILayout.Button("Apply"))
            {
                foreach (Object t in targets)
                {
                    if (t is Actor actor)
                        ApplyReferences(actor);
                }
            }
        }

        public static void ApplyReferences(Actor actor)
        {
            SerializedObject so = new SerializedObject(actor);

            Rigidbody rigidbody = actor.GetComponentInChildren<Rigidbody>(true);
            Collider[] colliders = actor.GetComponentsInChildren<Collider>(true);
            Transform bottomSocket = FindChildByName(actor.transform, "bottomSocket");
            Transform headSocket = FindChildByName(actor.transform, "headSocket");

            so.FindProperty("rigidbody").objectReferenceValue = rigidbody;

            SerializedProperty collidersProp = so.FindProperty("colliders");
            collidersProp.arraySize = colliders.Length;
            for (int i = 0; i < colliders.Length; i++)
                collidersProp.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];

            so.FindProperty("bottomSocket").objectReferenceValue = bottomSocket;
            so.FindProperty("headSocket").objectReferenceValue = headSocket;

            so.ApplyModifiedProperties();
        }

        public static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name)
                return root;

            foreach (Transform child in root)
            {
                Transform found = FindChildByName(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
