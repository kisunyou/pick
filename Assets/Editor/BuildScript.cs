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

    static void BuildAndroid(bool appBundle)
    {
        string ext = appBundle ? "aab" : "apk";
        string buildName = "pick_" + DateTime.Now.ToString("yyMMddHHmm");
        string outputPath = Path.GetFullPath($"build/{buildName}.{ext}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] Build Settings에 활성화된 씬이 없습니다.");
            EditorApplication.Exit(1);
            return;
        }

        // 확실히 Android 타깃으로 전환하고 APK / App Bundle 을 명시 선택
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = appBundle;

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
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildScript] BUILD FAILED: {summary.result}");
            EditorApplication.Exit(1);
        }
    }
}
