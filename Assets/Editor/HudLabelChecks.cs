using System;
using System.IO;
using System.Linq;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class HudLabelChecks
{
    static int _checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    static T Field<T>(UnityEngine.Object obj, string name) where T : UnityEngine.Object =>
        (T)new SerializedObject(obj).FindProperty(name).objectReferenceValue;

    [MenuItem("Tools/Gameplay Polish/Check HUD labels")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _checks = 0;
        var rows = JsonUtility.FromJson<StringDataList>(File.ReadAllText("Assets/Resources/Table/stringData.json")).stringData;
        Check(rows.Select(r => r.key).Distinct().Count() == rows.Count, "Duplicate localization keys.");
        var start = rows.Single(r => r.key == "hud_start");
        var mission = rows.Single(r => r.key == "hud_mission_title");
        Check(start.kor == "\uC2DC\uC791" && mission.kor == "\uBBF8\uC158", "Korean labels do not match the request.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var canvasGo = new GameObject("Label preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1080, 1920);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UIHud.prefab"), scene);
            root.transform.SetParent(canvasGo.transform, false);
            var hud = root.GetComponent<UIHud>();
            var button = Field<Button>(hud, "enterStageButton");
            var startText = button.transform.Find("Text_Stage").GetComponent<TMP_Text>();
            var missionText = Field<TextMeshProUGUI>(root.GetComponentInChildren<UIMissionHud>(true), "missionTitleText");
            var labels = new[] { startText, missionText };
            var keys = new[] { "hud_start", "hud_mission_title" };
            var translations = new[]
            {
                new[] { start.kor, start.eng, start.jpn, start.tha },
                new[] { mission.kor, mission.eng, mission.jpn, mission.tha }
            };
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                var localized = label.GetComponent<LocalizedText>();
                Check(localized != null && localized.enabled, "LocalizedText is missing or disabled.");
                Check(new SerializedObject(localized).FindProperty("key").stringValue == keys[i], "Wrong localization binding.");
                Check(label.text == translations[i][0], "Authored preview does not show the Korean label.");
                foreach (string text in translations[i])
                {
                    Check(!string.IsNullOrWhiteSpace(text) && !text.Contains("{"), "Missing or dynamic label translation.");
                    label.text = text;
                    label.ForceMeshUpdate(true, true);
                    Check(!label.isTextOverflowing && label.textInfo.lineCount == 1, "Label does not fit: " + text);
                    Check(label.textInfo.characterCount > 0, "No rendered characters.");
                }
            }
            string hudSource = File.ReadAllText("Assets/Script/FunRabbit/UI/HUD/UIHud.cs");
            string missionSource = File.ReadAllText("Assets/Script/FunRabbit/MissionSystem.cs");
            Check(!hudSource.Contains("hud_stage_number") && !hudSource.Contains("fixedLabel.enabled = false"),
                "Start label is still overwritten at runtime.");
            Check(!missionSource.Contains("SetMissionTitle("), "Mission name still overwrites its fixed title.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/hud_labels_result.txt", "PASS: " + _checks +
            " checks; prefab bindings, Korean previews and single-line layout in all four languages. No player-save writes.\n");
        Debug.Log("HUD_LABEL_CHECKS_PASSED=" + _checks);
    }
}
