using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// 패키지명 1회성 이관: 현재 값이 OldIdentifier 일 때만 NewIdentifier 로 교체한다.
// 이미 다른 값이면 아무것도 하지 않으므로 적용 확인 후 이 파일은 삭제해도 된다.
[InitializeOnLoad]
public static class AndroidPackageNameSetup
{
    const string OldIdentifier = "com.FunRabbit.pick";
    const string NewIdentifier = "com.funrabbit.pick";

    static AndroidPackageNameSetup()
    {
        if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != OldIdentifier)
            return;

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, NewIdentifier);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AndroidPackageNameSetup] applicationIdentifier: {OldIdentifier} -> {NewIdentifier}");
    }
}
