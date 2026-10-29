using System;
using System.IO;
using System.Reflection;
using FunRabbit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class LogoIntroChecks
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;

    [MenuItem("Tools/Gameplay Polish/Check logo intro")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode.");
        Check(Near(UILogoIntroAnimation.EvaluateScale(0), Vector3.one * .7f), "Start scale.");
        Check(Near(UILogoIntroAnimation.EvaluateScale(.3f), Vector3.one * 1.4f), "Peak scale.");
        Check(Near(UILogoIntroAnimation.EvaluateScale(1), Vector3.one), "Final scale.");
        var squash = UILogoIntroAnimation.EvaluateScale(.53f);
        var stretch = UILogoIntroAnimation.EvaluateScale(.70f);
        Check(squash.x > 1 && squash.y < 1 && stretch.x < 1 && stretch.y > 1, "Missing jelly motion.");
        for (int i = 0; i <= 1000; i++)
        {
            Vector3 sample = UILogoIntroAnimation.EvaluateScale(i / 1000f);
            Check(sample.x >= .699f && sample.x <= 1.401f && sample.y >= .699f && sample.y <= 1.401f, "Scale range.");
        }
        var scene = EditorSceneManager.NewPreviewScene();
        float timeScale = Time.timeScale;
        try
        {
            var go = new GameObject("Logo intro test", typeof(RectTransform), typeof(UILogoIntroAnimation));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.localScale = Vector3.one * 1.2f;
            var intro = go.GetComponent<UILogoIntroAnimation>();
            float duration = new SerializedObject(intro).FindProperty("duration").floatValue;
            Time.timeScale = 0;
            intro.Play();
            Check(Near(go.transform.localScale, Vector3.one * .84f), "Authored scale not respected.");
            intro.Advance(duration * .3f);
            Check(Near(go.transform.localScale, Vector3.one * 1.68f), "Paused-time peak failed.");
            intro.Advance(duration);
            Check(Near(go.transform.localScale, Vector3.one * 1.2f), "Final authored scale not restored.");
            intro.Play();
            intro.Play();
            Check(Near(go.transform.localScale, Vector3.one * .84f), "Repeated play compounded scale.");
            intro.Advance(float.NaN);
            intro.Advance(-1f);
            Check(Near(go.transform.localScale, Vector3.one * .84f), "Invalid delta changed pose.");
            intro.Stop();
            Check(Near(go.transform.localScale, Vector3.one * 1.2f), "Stop did not restore scale.");
            int completed = 0;
            intro.Play(() => completed++);
            intro.Advance(duration * .3f);
            Check(completed == 0, "Intro completed before settling.");
            intro.Advance(duration);
            intro.Advance(duration);
            Check(completed == 1, "Completion must run exactly once.");
            intro.Play(() => completed++);
            intro.Hide();
            intro.Advance(duration);
            Check(completed == 1, "Hidden intro fired stale completion.");
            CheckLoadingGate(scene);
            CheckPointerGate(scene);
        }
        finally
        {
            Time.timeScale = timeScale;
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/logo_intro_result.txt",
            "PASS: scale keyframes, jelly axes, 1001 samples, paused time, repeat/cancel, one-shot completion, hidden initial logo, loading/intro/prompt order, early and duplicate input rejection, no automatic start, interrupted/repeated readiness, close cancellation, held-touch rejection and fresh press/submit.\n");
        Debug.Log("LOGO_INTRO_CHECKS_PASSED");
    }

    static void CheckPointerGate(Scene scene)
    {
        var go = new GameObject("Start input test", typeof(RectTransform), typeof(UILoadingStartInput));
        SceneManager.MoveGameObjectToScene(go, scene);
        var gate = go.GetComponent<UILoadingStartInput>();
        int clicks = 0;
        gate.Bind(() => clicks++);
        var pointer = new PointerEventData(null) { pointerId = 1, button = PointerEventData.InputButton.Left };
        gate.SetReady(false);
        gate.OnPointerDown(pointer);
        gate.SetReady(true);
        gate.OnPointerClick(pointer);
        Check(clicks == 0, "A touch held before readiness started gameplay.");
        gate.OnPointerDown(pointer);
        gate.OnPointerClick(pointer);
        gate.OnPointerClick(pointer);
        Check(clicks == 1, "Fresh press must start once.");
        gate.SetReady(true);
        gate.OnPointerDown(pointer);
        gate.SetReady(false);
        gate.SetReady(true);
        gate.OnPointerClick(pointer);
        Check(clicks == 1, "Stale press survived a readiness reset.");
        gate.OnSubmit(new BaseEventData(null));
        gate.OnSubmit(new BaseEventData(null));
        Check(clicks == 2, "Ready keyboard/controller submit must start once.");
    }

    static void CheckLoadingGate(Scene scene)
    {
        var root = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI2/Prefabs/UILoading.prefab"), scene);
        var loading = root.GetComponent<UILoading>();
        var serialized = new SerializedObject(loading);
        var prompt = (GameObject)serialized.FindProperty("_touchToStart").objectReferenceValue;
        var button = (Button)serialized.FindProperty("_touchToStartBtn").objectReferenceValue;
        var intro = root.GetComponentInChildren<UILogoIntroAnimation>(true);
        var image = intro.GetComponent<Image>();
        float duration = new SerializedObject(intro).FindProperty("duration").floatValue;
        var accept = typeof(UILoading).GetMethod("TryAcceptStartInput", BindingFlags.NonPublic | BindingFlags.Instance);
        bool TryAccept() => (bool)accept.Invoke(loading, null);
        int started = 0;
        loading.SetOnTouchToStart(() => started++);
        Check(!image.enabled, "Prefab flashes logo before the loading flow starts.");
        loading.ShowLoginButtons();
        Check(!image.enabled && !prompt.activeSelf && !button.interactable && !TryAccept(), "Initial input/logo not hidden.");
        loading.ShowTouchToStartBtn();
        Check(image.enabled && !prompt.activeSelf && !button.interactable && !TryAccept(), "Intro must precede prompt/input.");
        intro.Advance(duration * .3f);
        Vector3 peak = intro.transform.localScale;
        loading.ShowTouchToStartBtn();
        Check(Near(peak, intro.transform.localScale), "Duplicate readiness restarted intro.");
        Check(!TryAccept() && !prompt.activeSelf, "Peak pose accepted an early click.");
        intro.Advance(duration);
        Check(prompt.activeSelf && button.interactable && started == 0, "Intro did not unlock prompt, or started gameplay automatically.");
        Check(TryAccept() && !TryAccept() && !button.interactable, "Ready input not accepted exactly once.");
        loading.ShowLoginButtons();
        loading.ShowTouchToStartBtn();
        intro.Advance(duration * .3f);
        loading.ShowLoginButtons();
        intro.Advance(duration);
        Check(!image.enabled && !prompt.activeSelf && !TryAccept(), "Cancelled intro unlocked input.");
        loading.ShowTouchToStartBtn();
        intro.Advance(duration);
        Check(prompt.activeSelf && button.interactable, "Second loading cycle failed.");
        typeof(UILoading).GetMethod("ResetStartPresentation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(loading, null);
        intro.Advance(duration);
        Check(!image.enabled && !prompt.activeSelf && !button.interactable && !TryAccept() && started == 0,
            "Closing/reset leaked presentation or gameplay callback.");
    }
}
