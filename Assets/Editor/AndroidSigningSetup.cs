using System.IO;
using UnityEditor;

// Unity 는 keystore 비밀번호를 ProjectSettings 에 저장하지 않는다 (에디터 세션 메모리 전용).
// 에디터 로드 시 Keystore/keystore_credentials.txt (VCS 제외 파일) 를 읽어 Android 서명 설정을 주입한다.
[InitializeOnLoad]
public static class AndroidSigningSetup
{
    const string CredentialsPath = "Keystore/keystore_credentials.txt";

    static AndroidSigningSetup()
    {
        if (!File.Exists(CredentialsPath))
            return;

        string keystoreName = null, keyaliasName = null, keystorePass = null, keyaliasPass = null;
        foreach (var line in File.ReadAllLines(CredentialsPath))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0)
                continue;

            var key = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            switch (key)
            {
                case "keystoreName": keystoreName = value; break;
                case "keyaliasName": keyaliasName = value; break;
                case "keystorePass": keystorePass = value; break;
                case "keyaliasPass": keyaliasPass = value; break;
            }
        }

        if (string.IsNullOrEmpty(keystoreName) || string.IsNullOrEmpty(keyaliasName))
            return;

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystoreName;
        PlayerSettings.Android.keyaliasName = keyaliasName;
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasPass = keyaliasPass;
    }
}
