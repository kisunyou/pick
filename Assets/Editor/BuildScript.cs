using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 커맨드라인 batchmode 빌드용 스크립트.
// 사용: Unity.exe -quit -batchmode -projectPath <proj> -buildTarget Android
//       -executeMethod BuildScript.BuildAndroidApk
public static class BuildScript
{
    public static void BuildAndroidApk()
    {
        string outputPath = Path.GetFullPath("build/pick.apk");
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

        // 확실히 Android 타깃 / APK(App Bundle 아님) 으로 빌드
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;

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
