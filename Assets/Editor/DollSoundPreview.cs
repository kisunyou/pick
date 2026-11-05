using System;
using System.Linq;
using System.Reflection;
using FunRabbit;
using UnityEditor;
using UnityEngine;

// 2026-11-05 동물 울음소리 미리듣기: actor.json 판 순서대로 이름·소리 경로·재생 버튼 (플레이 모드 없이 에디터에서 바로 재생)
public class DollSoundPreview : EditorWindow
{
    Vector2 scroll;

    [MenuItem("Tools/신규 인형/동물 소리 미리듣기")]
    static void Open() => GetWindow<DollSoundPreview>("동물 소리");

    void OnEnable() => GameActorData.Load();

    void OnGUI()
    {
        if (GUILayout.Button("actor.json 다시 읽기")) GameActorData.Load();
        if (GUILayout.Button("■ 정지")) Stop();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var row in GameActorData.Actors.OrderBy(r => r.stage))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{row.stage}. {row.animalKey}", GUILayout.Width(130));
            GUILayout.Label(row.sound, GUILayout.Width(220));
            var clip = Resources.Load<AudioClip>("Sound/" + row.sound);
            GUI.enabled = clip != null;
            if (GUILayout.Button(clip != null ? $"▶ {clip.length:F1}초" : "없음", GUILayout.Width(80))) Play(clip);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // 에디터 내부 미리듣기(AudioUtil)를 쓴다 - 공개 API 가 없어 리플렉션
    static Type AudioUtil => typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    static void Play(AudioClip clip)
    {
        Stop();
        var m = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (m != null) m.Invoke(null, new object[] { clip, 0, false });
        else Debug.LogWarning("[DollSoundPreview] AudioUtil.PlayPreviewClip 을 찾지 못함 - 프로젝트 창에서 클립을 선택해 재생하세요: " + AssetDatabase.GetAssetPath(clip));
    }

    static void Stop() => AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
}
