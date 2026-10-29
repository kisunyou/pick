using System;
using System.IO;
using System.Reflection;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MissionProgressChecks
{
    static int _checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    [MenuItem("Tools/Gameplay Polish/Check mission progress refresh")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        _checks = 0;
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/mission_progress_result.txt", "RUNNING\n");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var canvasGo = new GameObject("Mission progress test", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1080, 1920);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UIHud.prefab"), scene);
            root.transform.SetParent(canvasGo.transform, false);
            var hud = root.GetComponentInChildren<UIMissionHud>(true);
            var settings = new SerializedObject(hud);
            var text = (TMP_Text)settings.FindProperty("missionSliderText").objectReferenceValue;
            var slider = (Slider)settings.FindProperty("missionSlider").objectReferenceValue;
            var onEnable = typeof(UIMissionHud).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            int notifications = 0;
            slider.onValueChanged.AddListener(_ => notifications++);

            // Isolated display inputs, never the user's PlayerPrefs or real mission reward path.
            int savedProgress = 3;
            hud.UpdateMissionProgressText(savedProgress, 5, false);
            Check(text.text == "3 / 5" && Mathf.Approximately(slider.value, .6f), "Initial progress is wrong.");
            text.text = "0 / 5";
            slider.SetValueWithoutNotify(0);
            hud.UpdateMissionProgressText(savedProgress, 5, false);
            Check(text.text == "3 / 5" && Mathf.Approximately(slider.value, .6f), "Retained mission refresh lost progress.");
            for (int i = 0; i < 3; i++)
            {
                text.text = "0 / 5";
                slider.SetValueWithoutNotify(0);
                onEnable.Invoke(hud, null);
                Check(text.text == "3 / 5" && Mathf.Approximately(slider.value, .6f), "HUD reactivation lost progress.");
            }
            savedProgress++;
            hud.UpdateMissionProgressText(savedProgress, 5, false);
            Check(text.text == "4 / 5" && Mathf.Approximately(slider.value, .8f), "Next collection did not display 4/5.");
            hud.UpdateMissionProgressText(0, 7, false);
            onEnable.Invoke(hud, null);
            Check(text.text == "0 / 7" && slider.value == 0, "A new mission retained the old progress.");
            hud.UpdateMissionProgressText(0, 0, false);
            Check(text.text == "0 / 0" && slider.value == 0, "Zero target produced an invalid ratio.");
            Check(notifications == 0, "Read-only display refresh invoked slider callbacks.");
            var tween = typeof(UIMissionHud).GetField("_iconPunchTween", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(tween.GetValue(hud) == null, "Stage refresh played the collection animation.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllText("Logs/mission_progress_result.txt", "PASS: " + _checks +
            " checks; 3/5 retained, stale zero display restored, repeated HUD activation, next item 4/5, new mission reset, zero goal, no slider callbacks/punch on refresh. No player-save writes.\n");
        Debug.Log("MISSION_PROGRESS_CHECKS_PASSED=" + _checks);
    }
}
