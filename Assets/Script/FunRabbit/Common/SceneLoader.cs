using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FunRabbit
{
    public class SceneLoader : Singleton<SceneLoader>
    {
        private string _curSceneName = string.Empty;

        public bool LoadAsync(string sceneName, System.Action onFinishLoad)
        {
            if(IsLoadedScene(sceneName))
            {
                Debug.Log($"이미 {sceneName} 씬이 로드 되어 있습니다.");
                return false;
            }

            StartCoroutine(LoadSceneWithEmptyTransition(sceneName, onFinishLoad));

            return true;
        }

        IEnumerator LoadSceneWithEmptyTransition(string sceneName, System.Action onFinishLoad)
        {
            // 로딩 화면을 활성화
            //loadingScreen.SetActive(true);

            // 빈 씬을 비동기적으로 로드
            AsyncOperation emptySceneLoad = SceneManager.LoadSceneAsync("Empty");
            while (!emptySceneLoad.isDone)
            {
                yield return null;
            }

            // 메모리 해제
            System.GC.Collect();
            Resources.UnloadUnusedAssets();

            // 원래 로드하려는 씬을 비동기적으로 로드
            AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneName);

            // 씬이 로드되는 동안 슬라이더 업데이트
            while (!asyncOperation.isDone)
            {
                float progress = Mathf.Clamp01(asyncOperation.progress / 0.9f);                
                yield return null;
            }

            onFinishLoad?.Invoke();
            // 씬 로드 완료 후 로딩 화면 비활성화
            //loadingScreen.SetActive(false);
        }

        public bool IsLoadedScene(string sceneName)
        {
            if (sceneName == _curSceneName)
                return true;

            sceneName = _curSceneName;
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.isLoaded;
        }
    }
}
