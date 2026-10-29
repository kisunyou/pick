using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 커맨드라인 batchmode 빌드용 스크립트.
// 사용: Unity.exe -quit -batchmode -projectPath <proj> -buildTarget Android
//       -executeMethod BuildScript.BuildAndroidApk   (APK)
//       -executeMethod BuildScript.BuildAndroidAab   (AAB, Google Play)
// ⚠️ 같은 프로젝트가 에디터에 열려 있으면 batchmode 인스턴스가 열리지 않는다 — 먼저 닫을 것.
public static class BuildScript
{
    // APK (테스트 배포용)
    public static void BuildAndroidApk() => BuildAndroid(appBundle: false);

    // AAB (Google Play 업로드용). 사용: ... -executeMethod BuildScript.BuildAndroidAab
    public static void BuildAndroidAab() => BuildAndroid(appBundle: true);

    [Serializable]
    class BuildPairResult
    {
        public string status, apk, aab, error;
        public int versionCode;
    }

    [MenuItem("Tools/Build/Release APK and AAB")]
    public static void BuildReleasePair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Finish Play Mode or the current build before building releases.");
        RunReleasePair();
    }

    static void RunReleasePair()
    {
        string resultPath = Path.Combine(Application.dataPath, "../Logs/release_pair_result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        var result = new BuildPairResult { status = "Building APK", versionCode = PlayerSettings.Android.bundleVersionCode };
        string buildName = "pic_" + DateTime.Now.ToString("yyMMddHHmm");
        bool previousBundle = EditorUserBuildSettings.buildAppBundle;
        File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
        try
        {
            result.apk = BuildAndroid(false, false, buildName);
            result.status = "Building AAB";
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            result.aab = BuildAndroid(true, false, buildName);
            result.status = "Succeeded";
        }
        catch (Exception e)
        {
            result.status = "Failed";
            result.error = e.Message;
            Debug.LogException(e);
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = previousBundle;
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
        }
    }

    // Keystore/keystore_credentials.txt(key=value 형식)에서 서명 정보를 읽어 PlayerSettings에 적용
    static void ApplyKeystoreCredentials()
    {
        string credPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Keystore/keystore_credentials.txt"));
        if (!File.Exists(credPath))
        {
            Debug.LogWarning($"[BuildScript] keystore 자격증명 파일 없음: {credPath} - 기존 PlayerSettings 값으로 진행");
            return;
        }

        var cred = File.ReadAllLines(credPath)
            .Select(line => line.Split(new[] { '=' }, 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim());

        PlayerSettings.Android.useCustomKeystore = true;
        if (cred.TryGetValue("keystoreName", out string keystoreName))
            PlayerSettings.Android.keystoreName = Path.GetFullPath(Path.Combine(Application.dataPath, "..", keystoreName));
        if (cred.TryGetValue("keyaliasName", out string keyaliasName))
            PlayerSettings.Android.keyaliasName = keyaliasName;
        if (cred.TryGetValue("keystorePass", out string keystorePass))
            PlayerSettings.Android.keystorePass = keystorePass;
        if (cred.TryGetValue("keyaliasPass", out string keyaliasPass))
            PlayerSettings.Android.keyaliasPass = keyaliasPass;

        Debug.Log($"[BuildScript] keystore 자격증명 적용: {PlayerSettings.Android.keystoreName} (alias: {PlayerSettings.Android.keyaliasName})");
    }

    static string BuildAndroid(bool appBundle, bool exitEditor = true, string buildNameOverride = null)
    {
        string ext = appBundle ? "aab" : "apk";
        string buildName = buildNameOverride ?? "pick_" + DateTime.Now.ToString("yyMMddHHmm");
        string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, $"../build/{buildName}.{ext}"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] Build Settings에 활성화된 씬이 없습니다.");
            if (exitEditor) EditorApplication.Exit(1);
            throw new InvalidOperationException("No enabled build scenes.");
        }

        // 확실히 Android 타깃으로 전환하고 APK / App Bundle 을 명시 선택
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = appBundle;

        // batchmode 세션에는 에디터에서 입력한 keystore 비밀번호가 없다 - 자격증명 파일에서 로드
        ApplyKeystoreCredentials();

        // Unity 6.3에서 URP Compatibility Mode 설정이 deprecated/hidden 처리되면서
        // 빌드 전 검증(URPPreprocessBuild)이 이 심볼 없이는 실패한다 - 심볼 추가로 기존 Compatibility Mode 유지.
        NamedBuildTarget androidTarget = NamedBuildTarget.Android;
        string defines = PlayerSettings.GetScriptingDefineSymbols(androidTarget);
        if (!defines.Split(';').Contains("URP_COMPATIBILITY_MODE"))
        {
            defines = string.IsNullOrEmpty(defines) ? "URP_COMPATIBILITY_MODE" : defines + ";URP_COMPATIBILITY_MODE";
            PlayerSettings.SetScriptingDefineSymbols(androidTarget, defines);
            Debug.Log("[BuildScript] URP_COMPATIBILITY_MODE 스크립팅 심볼 추가");
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None, // release
        };

        Debug.Log($"[BuildScript] 씬 {scenes.Length}개 빌드 시작 -> {outputPath}");
        foreach (var s in scenes) Debug.Log($"[BuildScript]   scene: {s}");

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log($"[BuildScript] 결과={summary.result}, 크기={summary.totalSize} bytes, " +
                  $"에러={summary.totalErrors}, 시간={summary.totalTime}");

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] BUILD SUCCEEDED: {summary.outputPath}");
            if (exitEditor) EditorApplication.Exit(0);
            return summary.outputPath;
        }
        else
        {
            Debug.LogError($"[BuildScript] BUILD FAILED: {summary.result}");
            if (exitEditor) EditorApplication.Exit(1);
            throw new InvalidOperationException("Android build failed: " + summary.result);
        }
    }
}
