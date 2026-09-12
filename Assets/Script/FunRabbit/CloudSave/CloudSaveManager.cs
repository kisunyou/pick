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
                if (restored) { _switching = false; HasSaveConflict = false; }
                _switchGuard?.Dispose();
                _switchGuard = null;
                onDone?.Invoke(restored);
            });
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
            if (_uploading || !Current(generation, uid) || !IsSynced)
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
                        _uploading = false; done?.Invoke(false); yield break;
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
                    if (request.responseCode == 409 || request.responseCode == 412)
                    {
                        HasSaveConflict = true;
                        _synced = false;
                    }
                    Debug.LogWarning($"[CloudSave] Write not confirmed ({request.responseCode}); keeping local wallet and pending order.");
                    done?.Invoke(false);
                }
            }
        }

        void AcceptWrite(string uid, FsDoc doc, string json)
        {
            HasSaveConflict = false;
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
