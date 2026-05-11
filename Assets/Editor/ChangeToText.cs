using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class FindComponentInFiles : EditorWindow
{
    public class TextInfo
    {
        public string text;
        public Color color;
        public TMP_FontAsset font;
        public Material material;
        public float fontSize;
        public float fontMinSize;
        public float fontMaxSize;
        public bool autoSize;
        public TextAnchor alignmentOption;
        public FontStyles fontStyles;
        public bool raycastTarget;
    }

    private static TextInfo ConvertToText(TextMeshProUGUI text)
    {
        TextInfo info = new TextInfo();
        info.text = text.text;                
        info.color = text.color;
        info.fontSize = text.fontSize;
        info.fontMinSize = text.fontSizeMin;
        info.fontMaxSize = text.fontSizeMax;
        info.autoSize = text.autoSizeTextContainer;
        info.raycastTarget = text.raycastTarget;

        switch (text.alignment)
        {
            case TextAlignmentOptions.Bottom:
                info.alignmentOption = TextAnchor.LowerCenter;
                break;
            case TextAlignmentOptions.BottomLeft:
                info.alignmentOption = TextAnchor.LowerLeft;
                break;
            case TextAlignmentOptions.BottomRight:
                info.alignmentOption = TextAnchor.LowerRight;
                break;
            case TextAlignmentOptions.Center:
                info.alignmentOption = TextAnchor.MiddleCenter;
                break;
            case TextAlignmentOptions.Left:
                info.alignmentOption = TextAnchor.MiddleLeft;
                break;
            case TextAlignmentOptions.Right:
                info.alignmentOption = TextAnchor.MiddleRight;
                break;
            case TextAlignmentOptions.Top:
                info.alignmentOption = TextAnchor.UpperCenter;
                break;
            case TextAlignmentOptions.TopLeft:
                info.alignmentOption = TextAnchor.UpperLeft;
                break;
            case TextAlignmentOptions.TopRight:
                info.alignmentOption = TextAnchor.UpperRight;
                break;
        }

        switch (text.fontStyle)
        {
            case FontStyles.Bold:
                info.fontStyles = FontStyles.Bold;
                break;
            
            case FontStyles.Italic:
                info.fontStyles = FontStyles.Italic;
                break;
            default:
                info.fontStyles = FontStyles.Normal;
                break;
        }

        return info;
    }

    [MenuItem("TeenyWorld/특정 컴포넌트가 포함된 프리팹 검색")]
    public static void OpenFindComponentInFilesPanel()
    {
        FindComponentInFiles window = EditorWindow.GetWindow<FindComponentInFiles>(typeof(FindComponentInFiles));
        window.minSize = new Vector2(400, 300);
    }

    string _rootFolderName = "Assets";
    string _componentName = string.Empty;


    public static Type GetTypeFromAssemblies(string TypeName)
    {
        // null 반환 없이 Type이 얻어진다면 얻어진 그대로 반환.
        var type = Type.GetType(TypeName);
        if (type != null)
            return type;

        // 프로젝트에 분명히 포함된 클래스임에도 불구하고 Type이 찾아지지 않는다면,
        // 실행중인 어셈블리를 모두 탐색 하면서 그 안에 찾고자 하는 Type이 있는지 검사.
        var currentAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var referencedAssemblies = currentAssembly.GetReferencedAssemblies();
        foreach (var assemblyName in referencedAssemblies)
        {
            var assembly = System.Reflection.Assembly.Load(assemblyName);
            if (assembly != null)
            {
                type = assembly.GetType(TypeName);
                if (type != null)
                    return type;
            }
        }

        return null;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("탐색 시작 폴더");
        _rootFolderName = GUILayout.TextField(_rootFolderName);
        EditorGUILayout.LabelField("컴포넌트 이름");
        _componentName = "TextMeshProUGUI";// GUILayout.TextField(_componentName);

        if (GUILayout.Button("검색") == true)
        {
            if (_componentName.Length <= 0)
            {
                EditorUtility.DisplayDialog("알림", "검색 할 컴포넌트 이름을 입력하세요", "확인");
            }
            else
            {
                Find();
            }

        }

    }

    private void Find()
    {
        Font font = Resources.Load<Font>("Font/Daum_Regular");

        HashSet<UnityEngine.Object> allObjects = new HashSet<UnityEngine.Object>();
        var rootDirInfo = new DirectoryInfo(_rootFolderName);
        DirectoryInfo[] directoriesArray = rootDirInfo.GetDirectories();

        Type findComponentType = GetTypeFromAssemblies(_componentName);

        foreach (DirectoryInfo dirInfo in directoriesArray)
        {
            int find_idx = dirInfo.FullName.IndexOf("Assets");
            string path = dirInfo.FullName.Substring(find_idx, dirInfo.FullName.Length - find_idx);
            path = path.Replace("\\", "/");
            string[] guids2 = AssetDatabase.FindAssets("t:Prefab", new[] { path });
            foreach (var guid in guids2)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(assetPath, typeof(UnityEngine.Object));

                if (obj is GameObject)
                {
                    GameObject targetObject = obj as GameObject;

                    Component[] findComponents = targetObject.GetComponentsInChildren<TextMeshProUGUI>(true);

                    if (findComponents == null)
                        continue;

                    foreach(var component in findComponents)
                    {
                        GameObject componentObject = component.gameObject;
                        TextMeshProUGUI textMeshPro = component as TextMeshProUGUI;
                        if (textMeshPro == null)
                            continue;

                        var info = FindComponentInFiles.ConvertToText(textMeshPro);

                        DestroyImmediate(textMeshPro, true);

                        componentObject.AddComponent<Text>();
                        var newText = componentObject.GetComponent<Text>();
                        if (newText == null)
                        {
                            continue;
                        }
                        newText.text = info.text;
                        newText.font = font;

                        newText.fontSize = (int)info.fontSize;
                        newText.color = info.color;
                        newText.resizeTextForBestFit = info.autoSize;
                        newText.resizeTextMinSize = (int)info.fontMinSize;
                        newText.resizeTextMaxSize = (int)info.fontMaxSize;
                        newText.alignment = info.alignmentOption;
                        newText.raycastTarget = info.raycastTarget;
                    }
                } // obj

            } // guids2
        }
    }


}
