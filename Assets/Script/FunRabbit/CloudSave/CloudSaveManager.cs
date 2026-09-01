using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FunRabbit
{
    // 클라우드 세이브 매니저 (Firestore REST + Firebase Auth ID 토큰).
    // - 유저당 문서 1개: users/{uid} { saveVersion, updatedAt(ms), save(json) }
    // - 네이티브 Firestore SDK 없이 UnityWebRequest로 통신한다 (문서 1개 읽기/쓰기라 REST로 충분,
    //   APK 크기/SDK 버전잠금 부담 없음. 오프라인 저장소는 기존 PlayerPrefs가 담당).
    // - 부팅 동기화(SyncOnLogin) 정책:
    //     문서 없음                     → 로컬 스냅샷 업로드 (최초 마이그레이션)
    //     문서 있음 + 마커 == updatedAt → 이미 동기화됨 (그대로 진행)
    //     문서 있음 + 마커 != updatedAt → 클라우드 채택 (다른 기기/재설치 복원) 후 런타임 갱신
    // - 이후 자동 저장: 10초마다 스냅샷이 바뀌었으면 업로드 + 백그라운드 전환 시 즉시 업로드.
    //   부팅 동기화가 성공하기 전에는 자동 저장하지 않는다 (클라우드가 최신일 때 덮어쓰기 방지).
    public class CloudSaveManager : Singleton<CloudSaveManager>
    {
        const string FirestoreProjectId = "pick-ddf42"; // google-services.json project_id
        const int SupportedSaveVersion = 1;
        const float AutoSaveIntervalSeconds = 10f;
        const int RequestTimeoutSeconds = 10;

        // 이 uid로 마지막 동기화된 클라우드 updatedAt (다른 기기 갱신 감지용)
        const string SyncedMarkerKeyPrefix = "CloudSave_SyncedUpdatedAt_";

        // 부팅 동기화가 끝났는지 (성공해야 자동 저장이 활성화된다)
        public bool IsSynced { get; private set; }

        bool _isUploading;
        string _lastUploadedSaveJson;

        static string DocumentUrl(string uid) =>
            $"https://firestore.googleapis.com/v1/projects/{FirestoreProjectId}/databases/(default)/documents/users/{uid}";

        static string SyncedMarkerKey(string uid) => SyncedMarkerKeyPrefix + uid;

        void Start()
        {
            StartCoroutine(AutoSaveLoop());
        }

        // 백그라운드 전환 시 변경분 즉시 업로드 (완료 보장은 없음 - 복귀 후 자동 저장이 재시도)
        private void OnApplicationPause(bool pause)
        {
            if (pause && IsSynced)
                StartCoroutine(UploadIfDirtyCoroutine("pause"));
        }

        // ===== 부팅 동기화 =====

        // 로그인 확정 직후 호출 (UILoadingControl). 실패해도 onDone은 반드시 호출된다(fail-open).
        public void SyncOnLogin(Action onDone)
        {
            StartCoroutine(SyncOnLoginCoroutine(onDone));
        }

        private IEnumerator SyncOnLoginCoroutine(Action onDone)
        {
            var auth = FireBaseAuthManager.Instance;
            string uid = auth != null ? auth.UserId : null;
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogWarning("[CloudSaveManager] 미로그인 상태 - 클라우드 동기화 건너뜀");
                onDone?.Invoke();
                yield break;
            }

            string token = null;
            bool tokenDone = false;
            auth.GetIdToken(t => { token = t; tokenDone = true; });
            while (!tokenDone)
                yield return null;

            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[CloudSaveManager] ID 토큰 획득 실패 - 클라우드 동기화 건너뜀");
                onDone?.Invoke();
                yield break;
            }

            // 클라우드 문서 조회
            using (UnityWebRequest request = new UnityWebRequest(DocumentUrl(uid), "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.responseCode == 404)
                {
                    // 문서 없음 = 이 계정의 첫 클라우드 세이브 - 로컬 진행분을 업로드 (마이그레이션)
                    Debug.Log("[CloudSaveManager] 클라우드 세이브 없음 - 로컬 데이터 업로드");
                    yield return UploadSnapshotCoroutine(uid, token, uploaded =>
                    {
                        IsSynced = uploaded;
                    });
                    onDone?.Invoke();
                    yield break;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[CloudSaveManager] 클라우드 조회 실패({request.responseCode}): {request.error} - 로컬 데이터로 진행");
                    onDone?.Invoke();
                    yield break;
                }

                FsDoc doc = null;
                try { doc = JsonUtility.FromJson<FsDoc>(request.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning($"[CloudSaveManager] 클라우드 문서 파싱 실패: {e.Message}"); }

                if (doc?.fields?.save == null || string.IsNullOrEmpty(doc.fields.save.stringValue))
                {
                    Debug.LogWarning("[CloudSaveManager] 클라우드 문서 형식 이상 - 로컬 데이터로 진행 (자동 저장 비활성)");
                    onDone?.Invoke();
                    yield break;
                }

                int cloudVersion = ParseLong(doc.fields.saveVersion) is long v ? (int)v : 1;
                if (cloudVersion > SupportedSaveVersion)
                {
                    // 미래 버전 앱이 저장한 데이터 - 구버전 앱이 덮어쓰지 않도록 동기화 전체 중단
                    Debug.LogWarning($"[CloudSaveManager] 클라우드 세이브 버전({cloudVersion})이 지원 버전보다 높음 - 동기화 중단");
                    onDone?.Invoke();
                    yield break;
                }

                long cloudUpdatedAt = ParseLong(doc.fields.updatedAt) ?? 0;
                string marker = PlayerPrefs.GetString(SyncedMarkerKey(uid), string.Empty);

                if (marker == cloudUpdatedAt.ToString())
                {
                    // 이 기기가 마지막으로 동기화한 그대로 - 로컬이 최신이거나 동일
                    Debug.Log("[CloudSaveManager] 클라우드와 동기화 상태 일치 - 로컬 데이터로 진행");
                    _lastUploadedSaveJson = doc.fields.save.stringValue;
                    IsSynced = true;
                    onDone?.Invoke();
                    yield break;
                }

                // 다른 기기(또는 재설치 후 첫 로그인)가 저장한 클라우드 데이터 채택
                CloudSaveSnapshot snapshot = null;
                try { snapshot = JsonUtility.FromJson<CloudSaveSnapshot>(doc.fields.save.stringValue); }
                catch (Exception e) { Debug.LogWarning($"[CloudSaveManager] 클라우드 스냅샷 파싱 실패: {e.Message}"); }

                if (snapshot == null)
                {
                    onDone?.Invoke();
                    yield break;
                }

                Debug.Log($"[CloudSaveManager] 클라우드 세이브 채택 (updatedAt={cloudUpdatedAt}, stage={snapshot.GetInt("currentStage", 1)})");
                ApplyAndRefresh(snapshot);

                PlayerPrefs.SetString(SyncedMarkerKey(uid), cloudUpdatedAt.ToString());
                PlayerPrefs.Save();
                _lastUploadedSaveJson = doc.fields.save.stringValue;
                IsSynced = true;
                onDone?.Invoke();
            }
        }

        // 클라우드 스냅샷을 PlayerPrefs에 적용하고, 이미 로컬 값으로 초기화된 런타임 시스템들을 갱신한다.
        // 게이트(터치 투 스타트 이전) 시점에 호출되므로 게임 플레이 도중의 상태 꼬임은 없다.
        private void ApplyAndRefresh(CloudSaveSnapshot snapshot)
        {
            // 1) 아군 슬롯 정리를 먼저 한다 - ClearAllAllies가 슬롯 키를 저장(덮어쓰기)하므로
            //    스냅샷 적용 전에 실행해야 클라우드 값이 살아남는다
            bool hasBattle = ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battle);
            if (hasBattle)
                battle.ClearAllAlliesForCloudSave();

            // 2) 스냅샷 → PlayerPrefs (관리 키 전체 교체)
            snapshot.ApplyToPlayerPrefs();

            // 3) 재화/게이지 - PlayerContext 메모리 캐시와 옵저버(UI) 갱신
            PlayerContext.Initialize();

            // 4) 스테이지/보스 - SetCurrentStage가 bossHp를 최대치로 리셋하므로 클라우드 값을 재기록
            int stage = snapshot.GetInt("currentStage", 1);
            GameQuestManager.Instance.SetCurrentStage(stage);
            if (snapshot.TryGetInt("bossHp", out int bossHp))
            {
                PlayerPrefs.SetInt("bossHp", bossHp);
                PlayerPrefs.Save();
            }

            // 5) 아군 슬롯/대기열 - 클라우드 키 기준으로 재스폰
            if (hasBattle)
                battle.RestoreAllyBattleStateForCloudSave();

            // 6) 기계 안 인형 - 클라우드 StageData_{stage} 배치로 재생성
            GameDollCreator.Instance.CreateDolls();
        }

        // ===== 계정 전환 (게스트 → 이미 사용 중인 구글 계정으로 로그인 전환 시) =====

        // 전환 시작 - uid가 바뀌는 동안 자동 저장이 이전 계정 데이터를 새 계정 문서에 덮어쓰지 않게 정지한다.
        public void PrepareAccountSwitch()
        {
            IsSynced = false;
            _lastUploadedSaveJson = null;
        }

        // 전환 완료 - 새 계정 기준으로 부팅 동기화를 다시 수행한다 (그 계정의 클라우드 세이브 채택/업로드).
        public void ResyncAfterAccountSwitch()
        {
            SyncOnLogin(null);
        }

        // ===== 자동 저장 =====

        private IEnumerator AutoSaveLoop()
        {
            var wait = new WaitForSecondsRealtime(AutoSaveIntervalSeconds);
            while (true)
            {
                yield return wait;

                if (!IsSynced)
                    continue;

                yield return UploadIfDirtyCoroutine("auto");
            }
        }

        private IEnumerator UploadIfDirtyCoroutine(string reason)
        {
            if (_isUploading || !IsSynced)
                yield break;

            var auth = FireBaseAuthManager.Instance;
            string uid = auth != null ? auth.UserId : null;
            if (string.IsNullOrEmpty(uid))
                yield break;

            string saveJson = JsonUtility.ToJson(CloudSaveSnapshot.Capture());
            if (saveJson == _lastUploadedSaveJson)
                yield break;

            string token = null;
            bool tokenDone = false;
            auth.GetIdToken(t => { token = t; tokenDone = true; });
            while (!tokenDone)
                yield return null;

            if (string.IsNullOrEmpty(token))
                yield break;

            yield return UploadSnapshotCoroutine(uid, token, null, saveJson, reason);
        }

        private IEnumerator UploadSnapshotCoroutine(string uid, string token, Action<bool> onDone)
        {
            yield return UploadSnapshotCoroutine(uid, token, onDone, JsonUtility.ToJson(CloudSaveSnapshot.Capture()), "login");
        }

        private IEnumerator UploadSnapshotCoroutine(string uid, string token, Action<bool> onDone, string saveJson, string reason)
        {
            _isUploading = true;

            long updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var doc = new FsDoc
            {
                fields = new FsFields
                {
                    save = new FsString { stringValue = saveJson },
                    updatedAt = new FsInt { integerValue = updatedAt.ToString() },
                    saveVersion = new FsInt { integerValue = SupportedSaveVersion.ToString() },
                }
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(doc));

            using (UnityWebRequest request = new UnityWebRequest(DocumentUrl(uid), "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                _isUploading = false;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    _lastUploadedSaveJson = saveJson;
                    PlayerPrefs.SetString(SyncedMarkerKey(uid), updatedAt.ToString());
                    PlayerPrefs.Save();
                    Debug.Log($"[CloudSaveManager] 클라우드 저장 완료 ({reason}, {body.Length} bytes)");
                    onDone?.Invoke(true);
                }
                else
                {
                    Debug.LogWarning($"[CloudSaveManager] 클라우드 저장 실패({request.responseCode}): {request.error}\n{request.downloadHandler.text}");
                    onDone?.Invoke(false);
                }
            }
        }

        // ===== Firestore REST 문서 직렬화 =====

        private static long? ParseLong(FsInt field)
        {
            if (field == null || string.IsNullOrEmpty(field.integerValue))
                return null;

            return long.TryParse(field.integerValue, out long value) ? value : (long?)null;
        }

        [Serializable] private class FsDoc { public FsFields fields; }
        [Serializable] private class FsFields { public FsString save; public FsInt updatedAt; public FsInt saveVersion; }
        [Serializable] private class FsString { public string stringValue; }
        [Serializable] private class FsInt { public string integerValue; } // Firestore int64는 문자열로 직렬화된다
    }
}
