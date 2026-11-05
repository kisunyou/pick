using System;
using System.IO;
using System.Reflection;
using FunRabbit;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 2026-11-05 강제 업데이트 플레이 검사 (실제 로딩 흐름 + 실제 Firestore config/app 문서).
// 배치 실행 (-quit 없이 - 검사가 끝나면 스스로 종료):
//   Unity -batchmode -projectPath D:/pick -executeMethod ForceUpdatePlayTest.RunBlocked   -> 빌드 번호 0 으로 가정 = 막혀야 함
//   Unity -batchmode -projectPath D:/pick -executeMethod ForceUpdatePlayTest.RunPass      -> 실제 빌드 번호 = 통과해야 함
// 결과: .utmp/force_update/result_<mode>.txt
[InitializeOnLoad]
public static class ForceUpdatePlayTest
{
    const string ModeKey = "pick.ForceUpdatePlayTest.Mode";
    const string Output = ".utmp/force_update";
    const double Timeout = 120;
    const double SettleSeconds = 3;

    static double _start;
    static double _foundAt = -1;

    // 화면 캡처 (UI 루트 캔버스를 잠시 카메라 모드로 바꿔 렌더 텍스처에 그린다 - 배치 모드엔 게임 화면이 없음)
    const int CaptureWidth = 1080, CaptureHeight = 1920;
    static int _captureStep;
    static string _passMessage;
    static Camera _captureCamera;
    static RenderTexture _captureTexture;
    static RenderMode _prevRenderMode;
    static Camera _prevWorldCamera;

    static ForceUpdatePlayTest()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    public static void RunBlocked() => Begin("blocked", 0);
    public static void RunPass() => Begin("pass", -1);

    static void Begin(string mode, int versionCodeOverride)
    {
        SessionState.SetString(ModeKey, mode);
        AppVersionGate.EditorVersionCodeOverride = versionCodeOverride;
        EditorSceneManager.OpenScene("Assets/Scenes/GameStarter.unity");
        EditorApplication.isPlaying = true;
    }

    static string Mode => SessionState.GetString(ModeKey, "");

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || Mode == "")
            return;
        _start = EditorApplication.timeSinceStartup;
        _foundAt = -1;
        _captureStep = 0;
        _passMessage = null;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - _start;
            if (elapsed > Timeout)
                throw new Exception($"시간 초과 (gate={AppVersionGate.State}, current={AppVersionGate.CurrentVersionCode}, min={AppVersionGate.MinVersionCode})");

            if (Mode == "blocked") TickBlocked();
            else TickPass();
        }
        catch (Exception e)
        {
            Finish(false, e.Message);
        }
    }

    static void TickBlocked()
    {
        if (_passMessage != null)
        {
            TickCapture();
            return;
        }

        UIPopup popup = UIManager.IsCheckInstance() ? UIPopup.Get() : null;
        if (popup == null || !popup.IsForced)
            return;
        if (_foundAt < 0)
            _foundAt = EditorApplication.timeSinceStartup;
        // 팝업이 뜬 뒤 몇 초 더 흘려 로그인/시작 화면으로 넘어가지 않는지 본다
        if (EditorApplication.timeSinceStartup - _foundAt < SettleSeconds)
            return;

        Check(AppVersionGate.State == AppVersionGate.Status.UpdateRequired, "판정 = 업데이트 필요");
        Check(AppVersionGate.CurrentVersionCode == 0, "가정한 빌드 번호 0");
        Check(AppVersionGate.MinVersionCode >= 1, "Firestore min_version_code 읽음 (" + AppVersionGate.MinVersionCode + ")");

        LanguageManager lang = LanguageManager.Instance;
        Check(F<TextMeshProUGUI>(popup, "titleText").text == lang.Get("popup_force_update_title"), "제목 문구");
        Check(F<TextMeshProUGUI>(popup, "descriptionText").text == lang.Get("popup_force_update_body"), "본문 문구");
        Button ok = F<Button>(popup, "okButton");
        Check(ok.gameObject.activeInHierarchy && ok.interactable, "업데이트 버튼 표시");
        Check(ok.GetComponentInChildren<TMP_Text>(true).text == lang.Get("button_update"), "버튼 라벨 = 업데이트");
        Check(!F<Button>(popup, "cancelButton").gameObject.activeSelf, "취소 버튼 숨김");
        Check(!F<Button>(popup, "closeButton").gameObject.activeSelf, "닫기 버튼 숨김");
        Check(!F<Button>(popup, "dimedButton").interactable, "배경 눌러 닫기 막힘");

        // 로딩 화면 위에 그려지는지 (정렬 순서)
        Canvas popupCanvas = popup.GetComponent<Canvas>();
        UILoading loading = UILoading.Get();
        Check(loading != null, "로딩 화면 유지");
        int loadingOrder = loading.GetComponentInParent<Canvas>().sortingOrder;
        Check(popupCanvas != null && popupCanvas.overrideSorting && popupCanvas.sortingOrder > loadingOrder,
            $"팝업 정렬 {popupCanvas?.sortingOrder} > 로딩 {loadingOrder}");
        Check(popup.GetComponent<GraphicRaycaster>() != null, "팝업 터치 가능");

        // 게임 진입이 막혔는지: 로그인 버튼·터치 투 스타트 모두 숨김
        Check(!F<Button>(loading, "_guestLoginBtn").gameObject.activeSelf && !F<Button>(loading, "_googleLoginBtn").gameObject.activeSelf, "로그인 버튼 숨김");
        Check(!F<GameObject>(loading, "_touchToStart").activeSelf, "터치 투 스타트 숨김");

        // 다른 호출부가 덮어써도, 뒤로가기를 눌러도 그대로
        popup.Set("다른 팝업", "덮어쓰기 시도", null);
        Check(F<TextMeshProUGUI>(popup, "titleText").text == lang.Get("popup_force_update_title"), "다른 팝업이 덮어쓰지 못함");
        Check(popup.HandleBackKey() && UIPopup.Get() == popup, "뒤로가기로 안 닫힘");

        _passMessage = ($"막힘 확인: current {AppVersionGate.CurrentVersionCode} < min {AppVersionGate.MinVersionCode}, 팝업 정렬 {popupCanvas.sortingOrder} > 로딩 {loadingOrder}, 문구 '{lang.Get("popup_force_update_title")}' / 버튼 '{lang.Get("button_update")}'");
    }

    // 1단계: 루트 캔버스를 캡처 카메라에 붙인다 / 몇 프레임 뒤(캔버스 크기·배치 갱신) 2단계: 렌더 후 PNG 저장, 원복
    static void TickCapture()
    {
        Canvas root = UIManager.Instance.GetComponent<Canvas>();
        _captureStep++;
        if (_captureStep == 1)
        {
            _captureTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24);
            var go = new GameObject("ForceUpdateCaptureCamera");
            _captureCamera = go.AddComponent<Camera>();
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.black;
            // UI 오브젝트 레이어가 제각각일 수 있어 전부 그리되, 카메라를 월드 밖으로 빼 3D 씬은 안 보이게 한다
            _captureCamera.cullingMask = -1;
            _captureCamera.transform.position = new Vector3(0f, -10000f, 0f);
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.farClipPlane = 10f;
            _captureCamera.orthographic = true;
            _captureCamera.targetTexture = _captureTexture;
            _prevRenderMode = root.renderMode;
            _prevWorldCamera = root.worldCamera;
            root.renderMode = RenderMode.ScreenSpaceCamera;
            root.worldCamera = _captureCamera;
            root.planeDistance = 1f;
            return;
        }
        if (_captureStep < 6)
            return;

        Canvas.ForceUpdateCanvases();
        _captureCamera.Render();
        RenderTexture.active = _captureTexture;
        var tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        Directory.CreateDirectory(Output);
        File.WriteAllBytes(Output + "/force_update_popup.png", tex.EncodeToPNG());

        root.renderMode = _prevRenderMode;
        root.worldCamera = _prevWorldCamera;
        UnityEngine.Object.DestroyImmediate(_captureCamera.gameObject);
        Finish(true, _passMessage + ", 캡처 " + Output + "/force_update_popup.png");
    }

    static void TickPass()
    {
        if (!AppVersionGate.IsDone || !UIManager.IsCheckInstance())
            return;
        UILoading loading = UILoading.Get();
        if (loading == null)
            return;
        bool loginShown = F<Button>(loading, "_guestLoginBtn").gameObject.activeSelf;
        bool startShown = F<GameObject>(loading, "_touchToStart").activeSelf;
        if (!loginShown && !startShown)
            return;

        Check(AppVersionGate.State == AppVersionGate.Status.Pass, "판정 = 통과");
        Check(AppVersionGate.CurrentVersionCode == PlayerSettings.Android.bundleVersionCode, "빌드 번호 = " + PlayerSettings.Android.bundleVersionCode);
        UIPopup popup = UIPopup.Get();
        Check(popup == null || !popup.IsForced, "강제 팝업 없음");
        Finish(true, $"통과 확인: current {AppVersionGate.CurrentVersionCode} >= min {AppVersionGate.MinVersionCode}, 다음 화면 = {(loginShown ? "로그인 버튼" : "터치 투 스타트")}");
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("실패: " + message);
    }

    static T F<T>(object target, string name) where T : class
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(target) as T;
        }
        throw new Exception("필드 없음: " + name);
    }

    static void Finish(bool pass, string message)
    {
        EditorApplication.update -= Tick;
        string mode = Mode;
        SessionState.EraseString(ModeKey);
        AppVersionGate.EditorVersionCodeOverride = -1;
        Directory.CreateDirectory(Output);
        File.WriteAllText($"{Output}/result_{mode}.txt", (pass ? "PASS: " : "FAIL: ") + message + "\n");
        Debug.Log($"[ForceUpdatePlayTest] {mode} {(pass ? "PASS" : "FAIL")}: {message}");
        if (Application.isBatchMode)
            EditorApplication.Exit(pass ? 0 : 1);
        else
            EditorApplication.isPlaying = false;
    }
}
