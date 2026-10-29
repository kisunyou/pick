using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FunRabbit
{
    // One user document holds a V2 snapshot, including the combined coin wallet.
    // Writes use the server updateTime precondition: another device's newer save
    // must never be overwritten by a stale local snapshot.
    public class CloudSaveManager : Singleton<CloudSaveManager>
    {
        const string ProjectId = "pick-ddf42";
        const string OwnerKey = "CloudSave_LocalOwner";
        const string RestoreKey = "CloudSave_RestoreRequired";
        const string MarkerPrefix = "CloudSave_SyncedUpdatedAt_";
        const float SyncTimeout = 12f;
        const int RequestTimeout = 10;
        int _generation;
        bool _synced;
        bool _uploading;
        bool _gameplayStarted;
        bool _switching;
        bool _deleting;            // 계정 삭제 진행/완료 - 삭제된 문서를 업로드로 되살리지 않게 모든 쓰기를 막는다
        bool _conflictNotified;    // 이번 충돌을 사용자에게 이미 알렸는지 (자동 저장마다 반복 알림 방지)
        string _syncedUid;
        string _serverUpdateTime;
        string _lastUploaded;
        string _uncertainUpload;
        UnityWebRequest _request;
        Coroutine _syncJob;
        Coroutine _syncWatchdog;
        Action _syncDone;
        IDisposable _syncGuard;
        IDisposable _switchGuard;

        public bool IsSyncing => _syncDone != null;
        public bool GameplayStarted => _gameplayStarted;
        public bool HasSaveConflict { get; private set; }
        public bool IsSynced => _synced && _syncedUid == CurrentUid;
        public bool CanPurchase => IsSynced && !IsSyncing && !_switching && !SessionOperation.IsBusy;
        static string CurrentUid => FireBaseAuthManager.IsCheckInstance() ? FireBaseAuthManager.Instance.UserId : null;
        static string Url(string uid) => $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/users/{uid}";
        bool Current(int generation, string uid) => generation == _generation && uid == CurrentUid;

        void Start() { StartCoroutine(AutoSaveLoop()); }
        void OnApplicationPause(bool pause)
        {
            // Never block Unity's main thread waiting for an async request.
            // CoinWallet is already durable locally; unconfirmed orders are retried.
            if (pause && IsSynced && !IsSyncing && !_switching && !_uploading)
                StartCoroutine(Upload(_generation, CurrentUid, null));
        }

        public void MarkGameplayStarted()
        {
            CancelLoginSync();
            _gameplayStarted = true;
            if (Application.isPlaying)
                GameplayAnalytics.StageStarted(GameQuestManager.Instance.CurrentStage);
        }

        public void SyncOnLogin(Action onDone)
        {
            if (IsSynced && !_switching) { onDone?.Invoke(); return; }
            // Once local play is admitted, only an explicit, blocked account switch
            // may replace its data. No late background restore is allowed.
            if (_gameplayStarted && !_switching) { onDone?.Invoke(); return; }
            CancelLoginSync();
            _synced = false;
            _syncGuard = SessionOperation.Begin(dimBackground: _gameplayStarted || _switching);
            _syncDone = onDone ?? (() => { });
            int generation = ++_generation;
            _syncWatchdog = StartCoroutine(SyncWatchdog(generation));
            _syncJob = StartCoroutine(Sync(generation));
        }

        IEnumerator SyncWatchdog(int generation)
        {
            yield return new WaitForSecondsRealtime(SyncTimeout);
            if (generation == _generation && IsSyncing) CancelLoginSync();
        }

        public void CancelLoginSync()
        {
            if (!IsSyncing) return;
            ++_generation; // invalidates token callbacks as well as HTTP responses
            _request?.Abort();
            if (_syncJob != null) StopCoroutine(_syncJob);
            _uploading = false;
            _request = null;
            FinishSync(false);
        }

        void FinishSync(bool success)
        {
            if (_syncWatchdog != null) StopCoroutine(_syncWatchdog);
            _syncWatchdog = null;
            _syncJob = null;
            _synced = success;
            if (success) _syncedUid = CurrentUid;
            Action done = _syncDone;
            _syncDone = null;
            // On an account mismatch, keep the old data inaccessible until restore.
            if (!success && NeedsAccountRestore())
            {
                _switching = true;
                if (_switchGuard == null) _switchGuard = SessionOperation.Begin();
                SessionOperation.ShowRetry(() => ResyncAfterAccountSwitch(null));
            }
            _syncGuard?.Dispose();
            _syncGuard = null;
            done?.Invoke();
        }

        bool NeedsAccountRestore()
        {
            string owner = PlayerPrefs.GetString(OwnerKey, "");
            return !string.IsNullOrEmpty(CurrentUid) &&
                (PlayerPrefs.GetString(RestoreKey, "") == CurrentUid ||
                 (!string.IsNullOrEmpty(owner) && owner != CurrentUid));
        }

        IEnumerator Sync(int generation)
        {
            string uid = CurrentUid;
            if (string.IsNullOrEmpty(uid)) { FinishSync(false); yield break; }
            string token = null;
            bool tokenDone = false;
            FireBaseAuthManager.Instance.GetIdToken(t => { token = t; tokenDone = true; });
            while (!tokenDone && Current(generation, uid)) yield return null;
            if (!Current(generation, uid)) yield break;
            if (string.IsNullOrEmpty(token)) { FinishSync(false); yield break; }

            using (var request = new UnityWebRequest(Url(uid), "GET"))
            {
                Setup(request, token);
                _request = request;
                yield return request.SendWebRequest();
                _request = null;
                if (!Current(generation, uid)) yield break;
                if (request.responseCode == 404)
                {
                    _serverUpdateTime = null;
                    // A different account with no save gets a clean initial state,
                    // never the previous account's purchases or progress.
                    if (NeedsAccountRestore())
                    {
                        var initial = new CloudSaveSnapshot { version = 1 };
                        ApplyAndRefresh(initial);
                    }
                    bool uploaded = false;
                    yield return UploadWithToken(generation, uid, token, ok => uploaded = ok);
                    if (!Current(generation, uid)) yield break;
                    if (uploaded) SetOwner(uid);
                    FinishSync(uploaded);
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[CloudSave] Restore failed ({request.responseCode}).");
                    FinishSync(false); yield break;
                }
                FsDoc doc = ParseDoc(request.downloadHandler.text);
                if (doc == null) { FinishSync(false); yield break; }
                CloudSaveSnapshot snapshot;
                try
                {
                    snapshot = JsonUtility.FromJson<CloudSaveSnapshot>(doc.fields.save.stringValue);
                    snapshot.Validate();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CloudSave] Invalid snapshot: {e.Message}");
                    FinishSync(false); yield break;
                }
                string marker = PlayerPrefs.GetString(MarkerPrefix + uid, "");
                if (NeedsAccountRestore() || (marker != doc.updateTime && marker != doc.fields.updatedAt.integerValue))
                    ApplyAndRefresh(snapshot);
                _serverUpdateTime = doc.updateTime;
                _lastUploaded = doc.fields.save.stringValue;
                _uncertainUpload = null;
                PlayerPrefs.SetString(MarkerPrefix + uid, doc.updateTime);
                SetOwner(uid);
                FinishSync(true);
            }
        }

        void SetOwner(string uid)
        {
            PlayerPrefs.SetString(OwnerKey, uid);
            PlayerPrefs.DeleteKey(RestoreKey);
            PlayerPrefs.Save();
        }

        void ApplyAndRefresh(CloudSaveSnapshot snapshot)
        {
            snapshot.Validate();
            GameplayAnalytics.EndAttempt(true);
            GameQuestManager.Instance.PrepareForCloudRestore();
            bool hasBattle = ActorBattleSystem.TryGetSetInstance(out ActorBattleSystem battle);
            if (hasBattle) battle.ClearAllAlliesForCloudSave();
            snapshot.ApplyToPlayerPrefs();
            PlayerContext.Initialize();
            GameQuestManager.Instance.SetCurrentStage(snapshot.GetInt("currentStage", 1));
            if (snapshot.TryGetInt("bossHp", out int hp))
            {
                PlayerPrefs.SetInt("bossHp", hp);
                // SetCurrentStage resets the maximum; restore the source scale before migrating HP.
                if (snapshot.TryGetInt("bossHpMax", out int maxHp))
                    PlayerPrefs.SetInt("bossHpMax", maxHp);
                else
                    PlayerPrefs.DeleteKey("bossHpMax");
            }
            PlayerPrefs.Save();
            // bossHp/bossHpMax 를 GameQuestManager 를 거치지 않고 직접 썼다 - 캐시를 버려 다음 조회에서 다시 보정되게
            GameQuestManager.Instance.InvalidateSaveCache();
            GameQuestManager.Instance.NotifyRestoredBossHp();
            if (hasBattle) battle.RestoreAllyBattleStateForCloudSave();
            GameDollCreator.Instance.CreateDolls();
            foreach (var timer in UnityEngine.Object.FindObjectsByType<UICoinTimerHud>(FindObjectsSortMode.None))
                timer.RefreshFromSave();
            if (MissionSystem.IsCheckInstance()) MissionSystem.Instance.RefreshFromSave();
        }

        public void PrepareAccountSwitch()
        {
            // Settle reward callbacks against their original account before pausing.
            foreach (var trail in UnityEngine.Object.FindObjectsByType<UIGetDollTrailHud>(FindObjectsSortMode.None))
                trail.CompletePendingRewards();
            if (UIBottomBar.Instance != null) UIBottomBar.Instance.CompletePendingRewards();
            if (_switchGuard == null) _switchGuard = SessionOperation.Begin();
            _switching = true;
            CancelLoginSync();
            ++_generation;
            _request?.Abort();
            StopAllCoroutines();
            _request = null;
            _uploading = false;
            _serverUpdateTime = null;
            _lastUploaded = null;
            _uncertainUpload = null;
            StartCoroutine(AutoSaveLoop());
            // In-flight uploads may still reach the server, but generation checks
            // prevent their callbacks from touching the next account's session.
            _synced = false;
        }

        public void ResyncAfterAccountSwitch(Action<bool> onDone)
        {
            if (CurrentUid != PlayerPrefs.GetString(OwnerKey, ""))
            {
                PlayerPrefs.SetString(RestoreKey, CurrentUid ?? "");
                PlayerPrefs.Save();
            }
            SessionOperation.HideRetry();
            SyncOnLogin(() =>
            {
                bool success = IsSynced;
                if (success)
                {
                    _switching = false;
                    _switchGuard?.Dispose();
                    _switchGuard = null;
                    SessionOperation.HideRetry();
                }
                else
                {
                    // Retry is explicit and keeps input/physics blocked; callers
                    // must not report successful linking while restore is pending.
                    SessionOperation.ShowRetry(() => ResyncAfterAccountSwitch(onDone));
                }
                onDone?.Invoke(success);
            });
        }

        public void SaveNow(Action<bool> onDone)
        {
            if (!IsSynced || IsSyncing || _switching) { onDone?.Invoke(false); return; }
            StartCoroutine(Upload(_generation, CurrentUid, onDone));
        }

        public void RecoverPurchaseConflict(Action<bool> onDone)
        {
            if (!HasSaveConflict || string.IsNullOrEmpty(CurrentUid)) { onDone?.Invoke(IsSynced); return; }
            string owner = CurrentUid;
            PrepareAccountSwitch();
            PlayerPrefs.SetString(RestoreKey, owner);
            PlayerPrefs.Save();
            SyncOnLogin(() =>
            {
                bool restored = IsSynced && CurrentUid == owner;
                if (restored) { _switching = false; HasSaveConflict = false; _conflictNotified = false; }
                _switchGuard?.Dispose();
                _switchGuard = null;
                onDone?.Invoke(restored);
            });
        }

        // ── 저장 충돌 알림 (다른 기기가 더 최근에 저장함: 409/412 또는 재확인 시 updateTime 불일치) ──
        // 충돌 후에는 이 기기의 업로드가 멈추고(_synced=false), 다음 실행 때 서버본으로 덮이므로
        // 사용자에게 토스트로 알리고 "지금 서버 데이터 불러오기" 를 제안한다. 데이터 구조는 바꾸지 않는다.
        void NotifySaveConflict(long responseCode)
        {
            Debug.LogWarning($"[CloudSave] 저장 충돌 감지 ({responseCode}) - 이 기기의 업로드 중단, 다음 실행 시 서버본으로 교체됨. 사용자 알림.");
            if (_conflictNotified || _deleting)
                return;
            _conflictNotified = true;
            if (FireBaseAnalyticsManager.IsCheckInstance())
                FireBaseAnalyticsManager.Instance.LogEvent("cloud_save_conflict", "code", responseCode.ToString());

            // 구매 지급 중이면 ShopManager 가 자체 재동기화 오버레이(purchase_save_conflict)로 처리한다
            if (SessionOperation.IsBusy || (ShopManager.IsCheckInstance() && ShopManager.Instance.IsAccountChangeBlocked))
                return;
            if (!UIManager.IsCheckInstance())
                return;

            UITopMessage.ShowMessage(LanguageManager.Instance.Get("message_save_conflict"));

            UIPopup popup = UIPopup.CreateOrGet();
            if (popup != null)
                popup.Set(
                    LanguageManager.Instance.Get("popup_save_conflict_title"),
                    LanguageManager.Instance.Get("popup_save_conflict_body"),
                    () => StartCoroutine(ReloadAfterConflict()));
        }

        // 서버 데이터 다시 불러오기 - 크레인 플레이 도중이면 READY 복귀까지 기다렸다가 진행 (플레이 중 인형 리셋 방지)
        IEnumerator ReloadAfterConflict()
        {
            while (Crane.TryGetSetInstance(out Crane crane) && crane.Status != CraneStatus.READY)
                yield return null;
            while (SessionOperation.IsBusy)
                yield return null;
            if (!HasSaveConflict)
                yield break;

            Debug.Log("[CloudSave] 사용자 요청 - 충돌 후 서버 데이터 다시 불러오기");
            // ⚠️ RecoverPurchaseConflict → PrepareAccountSwitch 가 StopAllCoroutines 하므로 이 코루틴의 마지막 동작이어야 한다
            RecoverPurchaseConflict(ok =>
            {
                Debug.Log($"[CloudSave] 충돌 후 서버 데이터 불러오기 결과: {ok}");
                UITopMessage.ShowMessage(LanguageManager.Instance.Get(
                    ok ? "message_save_conflict_reloaded" : "message_save_conflict_reload_fail"));
            });
        }

        // ── 계정 삭제 (설정 > 계정 삭제, Google Play 정책) ──
        // 서버 문서(users/{uid}) DELETE → Firebase 유저 삭제 → 로컬 저장 초기화 → 앱 재시작.
        // 진행 내내 SessionOperation 오버레이로 입력/시간을 멈추고, 끝나면 오버레이의 버튼으로 재시작한다.

        // 기기 설정값(언어·볼륨·진동)은 계정 데이터가 아니므로 로컬 초기화 후에도 유지한다
        const string LanguageKey = "LanguageType";
        const string SfxVolumeKey = "SfxVolume";
        const string BgmVolumeKey = "BgmVolume";
        const string VibrationKey = "VibrationEnabled";

        public bool IsDeletingAccount => _deleting;

        // 지금 삭제를 시작할 수 있는지 (동기화·계정 전환·구매 지급 중에는 불가)
        public bool CanDeleteAccount => !_deleting && !IsSyncing && !_switching && !SessionOperation.IsBusy &&
            !(ShopManager.IsCheckInstance() && (ShopManager.Instance.IsAccountChangeBlocked || ShopManager.Instance.HasPendingPurchase));

        public void DeleteAccountAndRestart(Action onFailed)
        {
            if (!CanDeleteAccount) { onFailed?.Invoke(); return; }
            StartCoroutine(DeleteAccountRoutine(onFailed));
        }

        IEnumerator DeleteAccountRoutine(Action onFailed)
        {
            IDisposable guard = SessionOperation.Begin();
            SessionOperation.SetStatusText(LanguageManager.Instance.Get("account_delete_in_progress"));
            _deleting = true;
            FireBaseAnalyticsManager.Instance.LogEvent("account_delete", "result", "try");

            // 진행 중인 업로드가 끝나길 기다린다 (삭제 뒤 늦게 도착한 쓰기가 문서를 되살리지 않게)
            float deadline = Time.realtimeSinceStartup + RequestTimeout;
            while (_uploading && Time.realtimeSinceStartup < deadline)
                yield return null;

            // 1) Firestore users/{uid} 문서 삭제 (404 = 이미 없음 → 성공)
            string uid = CurrentUid;
            if (!string.IsNullOrEmpty(uid))
            {
                string token = null;
                bool tokenDone = false;
                FireBaseAuthManager.Instance.GetIdToken(t => { token = t; tokenDone = true; });
                while (!tokenDone) yield return null;

                bool deleted = false;
                long code = 0;
                if (!string.IsNullOrEmpty(token))
                {
                    using (var request = new UnityWebRequest(Url(uid), "DELETE"))
                    {
                        Setup(request, token);
                        yield return request.SendWebRequest();
                        code = request.responseCode;
                        deleted = request.result == UnityWebRequest.Result.Success || code == 404;
                    }
                }

                if (!deleted)
                {
                    Debug.LogWarning($"[CloudSave] 계정 삭제 - 서버 문서 삭제 실패 ({code}). 아무것도 지우지 않고 중단.");
                    FireBaseAnalyticsManager.Instance.LogEvent("account_delete", "result", "server_fail");
                    _deleting = false;
                    SessionOperation.SetStatusText(string.Empty);
                    guard.Dispose();
                    onFailed?.Invoke();
                    yield break;
                }
                Debug.Log($"[CloudSave] 계정 삭제 - 서버 문서 삭제 완료 (users/{uid}, {code})");
            }

            // 여기부터는 되돌릴 수 없다 - 이 기기 세션을 클라우드와 끊는다
            _synced = false;
            _syncedUid = null;
            HasSaveConflict = false;
            ++_generation;
            ResetLocalData();

            // 2) Firebase 유저 삭제 (최근 로그인 필요 시 FireBaseAuthManager 가 재인증 1회 시도)
            FireBaseAuthManager.DeleteUserResult result = FireBaseAuthManager.DeleteUserResult.Success;
            if (FireBaseAuthManager.IsCheckInstance() && FireBaseAuthManager.Instance.IsLoggedIn)
            {
                bool authDone = false;
                FireBaseAuthManager.Instance.DeleteCurrentUser(r => { result = r; authDone = true; });
                while (!authDone) yield return null;
            }

            string bodyKey = "account_delete_done_body";
            if (result != FireBaseAuthManager.DeleteUserResult.Success)
            {
                // 게임 데이터는 이미 지워졌다. 로그인 계정만 남았으므로 로그아웃해 두고,
                // 다시 로그인한 뒤 한 번 더 삭제하도록 안내한다.
                Debug.LogWarning($"[CloudSave] 계정 삭제 - 로그인 계정 삭제 실패({result}), 로그아웃 후 재로그인 안내");
                bodyKey = result == FireBaseAuthManager.DeleteUserResult.RequiresRecentLogin
                    ? "account_delete_relogin_body" : "account_delete_auth_fail_body";
                FireBaseAuthManager.Instance.SignOut();
            }

            ResetLocalData();
            SessionOperation.ShowRetry(RestartApp, bodyKey, "account_delete_restart");
            // guard 는 재시작까지 유지한다 (게임 시간 정지 + 입력 차단 = 지운 데이터가 다시 저장될 틈을 없앤다)
        }

        // 로컬 저장 초기화 - 기기 설정값만 남기고 PlayerPrefs 전체 삭제
        static void ResetLocalData()
        {
            // 키/타입은 LanguageManager(PLAYER_PREFS_KEY, string) / AudioManager(float) / VibrationManager(int) 와 일치시킨다
            bool hasLanguage = PlayerPrefs.HasKey(LanguageKey);
            string language = PlayerPrefs.GetString(LanguageKey, "");
            bool hasSfx = PlayerPrefs.HasKey(SfxVolumeKey);
            float sfx = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            bool hasBgm = PlayerPrefs.HasKey(BgmVolumeKey);
            float bgm = PlayerPrefs.GetFloat(BgmVolumeKey, 1f);
            bool hasVibration = PlayerPrefs.HasKey(VibrationKey);
            int vibration = PlayerPrefs.GetInt(VibrationKey, 1);

            PlayerPrefs.DeleteAll();

            if (hasLanguage) PlayerPrefs.SetString(LanguageKey, language);
            if (hasSfx) PlayerPrefs.SetFloat(SfxVolumeKey, sfx);
            if (hasBgm) PlayerPrefs.SetFloat(BgmVolumeKey, bgm);
            if (hasVibration) PlayerPrefs.SetInt(VibrationKey, vibration);
            PlayerPrefs.Save();
            // 진행 데이터가 전부 지워졌다 - 스테이지/보스 hp 메모리 캐시도 버린다
            GameQuestManager.InvalidateSaveCacheIfExists();
        }

        // 처음(GameStarter → 로딩/로그인 화면)부터 다시 시작
        // Android: 런처 인텐트(CLEAR_TASK)로 새 태스크를 띄운 뒤 현재 프로세스를 종료 → 모든 싱글톤/정적 상태가 초기화된다.
        static void RestartApp()
        {
            ResetLocalData();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_ANDROID
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var launchIntent = packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", activity.Call<string>("getPackageName")))
                using (var component = launchIntent.Call<AndroidJavaObject>("getComponent"))
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var restartIntent = intentClass.CallStatic<AndroidJavaObject>("makeRestartActivityTask", component))
                using (var process = new AndroidJavaClass("android.os.Process"))
                {
                    activity.Call("startActivity", restartIntent);
                    process.CallStatic("killProcess", process.CallStatic<int>("myPid"));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CloudSave] 앱 재시작 실패 - 종료로 대체: {e.Message}");
                Application.Quit();
            }
#else
            Application.Quit();
#endif
        }

        IEnumerator AutoSaveLoop()
        {
            var wait = new WaitForSecondsRealtime(60f);
            while (true)
            {
                yield return wait;
                if (IsSynced && !IsSyncing && !_switching && !_uploading)
                    yield return Upload(_generation, CurrentUid, null);
            }
        }

        IEnumerator Upload(int generation, string uid, Action<bool> done)
        {
            float deadline = Time.realtimeSinceStartup + RequestTimeout;
            while (_uploading && Current(generation, uid) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (_uploading || !Current(generation, uid) || !IsSynced || _deleting)
            { done?.Invoke(false); yield break; }
            // Reserve the write slot before token acquisition.
            _uploading = true;
            string token = null;
            bool tokenDone = false;
            FireBaseAuthManager.Instance.GetIdToken(t => { token = t; tokenDone = true; });
            while (!tokenDone && Current(generation, uid) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!Current(generation, uid)) { _uploading = false; done?.Invoke(false); yield break; }
            if (string.IsNullOrEmpty(token)) { _uploading = false; done?.Invoke(false); yield break; }
            yield return UploadWithToken(generation, uid, token, done);
        }

        IEnumerator UploadWithToken(int generation, string uid, string token, Action<bool> done)
        {
            if (_deleting) { done?.Invoke(false); yield break; }
            _uploading = true;
            string json = JsonUtility.ToJson(CloudSaveSnapshot.Capture());
            // After an ambiguous timeout, recognize our own accepted write without
            // replacing any local changes made since that request was sent.
            if (_uncertainUpload != null)
            {
                using (var read = new UnityWebRequest(Url(uid), "GET"))
                {
                    Setup(read, token); _request = read;
                    yield return read.SendWebRequest(); _request = null;
                    if (!Current(generation, uid)) { _uploading = false; done?.Invoke(false); yield break; }
                    if (read.result != UnityWebRequest.Result.Success && read.responseCode != 404)
                    { _uploading = false; done?.Invoke(false); yield break; }
                    FsDoc remote = read.result == UnityWebRequest.Result.Success ? ParseDoc(read.downloadHandler.text) : null;
                    if (remote != null && remote.fields.save.stringValue == _uncertainUpload)
                    {
                        AcceptWrite(uid, remote, _uncertainUpload);
                    }
                    else if ((remote != null && remote.updateTime != _serverUpdateTime) ||
                             (read.responseCode == 404 && !string.IsNullOrEmpty(_serverUpdateTime)))
                    {
                        _synced = false;
                        HasSaveConflict = true;
                        Debug.LogWarning("[CloudSave] Another device changed this save. Explicit resync is required; purchase confirmation deferred.");
                        _uploading = false;
                        NotifySaveConflict(read.responseCode);
                        done?.Invoke(false); yield break;
                    }
                    _uncertainUpload = null;
                }
            }
            if (json == _lastUploaded) { _uploading = false; done?.Invoke(true); yield break; }
            var doc = new FsDoc { fields = new FsFields {
                save = new FsString { stringValue = json },
                saveVersion = new FsInt { integerValue = "2" },
                updatedAt = new FsInt { integerValue = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
            }};
            string condition = string.IsNullOrEmpty(_serverUpdateTime) ? "currentDocument.exists=false" :
                "currentDocument.updateTime=" + UnityWebRequest.EscapeURL(_serverUpdateTime);
            using (var request = new UnityWebRequest(Url(uid) + "?" + condition, "PATCH"))
            {
                Setup(request, token);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(doc)));
                request.SetRequestHeader("Content-Type", "application/json");
                _request = request;
                yield return request.SendWebRequest(); _request = null;
                _uploading = false;
                if (!Current(generation, uid)) { done?.Invoke(false); yield break; }
                FsDoc saved = request.result == UnityWebRequest.Result.Success ? ParseDoc(request.downloadHandler.text) : null;
                if (saved != null)
                {
                    AcceptWrite(uid, saved, json);
                    done?.Invoke(true);
                }
                else
                {
                    _uncertainUpload = json;
                    Debug.LogWarning($"[CloudSave] Write not confirmed ({request.responseCode}); keeping local wallet and pending order.");
                    if (request.responseCode == 409 || request.responseCode == 412)
                    {
                        HasSaveConflict = true;
                        _synced = false;
                        NotifySaveConflict(request.responseCode);
                    }
                    done?.Invoke(false);
                }
            }
        }

        void AcceptWrite(string uid, FsDoc doc, string json)
        {
            HasSaveConflict = false;
            _conflictNotified = false;
            _serverUpdateTime = doc.updateTime;
            _lastUploaded = json;
            _uncertainUpload = null;
            PlayerPrefs.SetString(MarkerPrefix + uid, doc.updateTime);
            PlayerPrefs.Save();
        }

        static void Setup(UnityWebRequest request, string token)
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = RequestTimeout;
        }

        static FsDoc ParseDoc(string json)
        {
            try
            {
                FsDoc doc = JsonUtility.FromJson<FsDoc>(json);
                if (doc?.fields?.save?.stringValue == null || doc.fields.updatedAt?.integerValue == null ||
                    !int.TryParse(doc.fields.saveVersion?.integerValue, out int version) || version < 1 || version > 2 ||
                    string.IsNullOrEmpty(doc.updateTime)) return null;
                return doc;
            }
            catch { return null; }
        }

        [Serializable] class FsDoc { public FsFields fields; public string updateTime; }
        [Serializable] class FsFields { public FsString save; public FsInt updatedAt; public FsInt saveVersion; }
        [Serializable] class FsString { public string stringValue; }
        [Serializable] class FsInt { public string integerValue; }
    }
}
