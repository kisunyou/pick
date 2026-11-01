#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using FunRabbit;

namespace PickShorts
{
    // Editor-only recorder and local production controls. Uses the game's existing
    // input handlers; never grants catches, changes physics, or manufactures wins.
    [InitializeOnLoad]
    public static class ShortsCapture
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static readonly string Work = Path.Combine(Root, "_shorts", "work");
        static readonly string Raw = Path.Combine(Root, "_shorts", "raw");
        static RecorderController controller;
        static string recordingPath;
        static double nextStatus;
        static double pendingStart;
        static float moveUntil;
        static int moveDirection = -1;
        static int stopAtFrame = -1;
        static int startFrame;

        static ShortsCapture()
        {
            Directory.CreateDirectory(Work);
            Directory.CreateDirectory(Raw);
            if (!SessionState.GetBool("PickShorts.BridgeInitialized", false))
            {
                SessionState.SetBool("PickShorts.BridgeInitialized", true);
                var oldCommand = Path.Combine(Work, "command.json");
                if (File.Exists(oldCommand))
                {
                    try { SessionState.SetString("PickShorts.LastCommand", (string)JObject.Parse(File.ReadAllText(oldCommand))["id"] ?? ""); }
                    catch { }
                }
            }
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += () => StopRecording(false);
            EditorApplication.quitting += () => StopRecording(false);
        }

        [MenuItem("Tools/Shorts/녹화 시작")]
        public static void StartMenu()
        {
            SessionState.SetString("PickShorts.Date", DateTime.Now.ToString("yyyyMMdd"));
            SetPortraitGameView();
            if (!EditorApplication.isPlaying)
            {
                SessionState.SetBool("PickShorts.RecordOnPlay", true);
                EditorApplication.isPlaying = true;
            }
            else StartRecording(0);
        }

        [MenuItem("Tools/Shorts/녹화 종료")]
        public static void StopMenu() => StopRecording(true);

        [MenuItem("Tools/Shorts/10초 테스트 녹화")]
        public static void TestMenu()
        {
            if (EditorApplication.isPlaying) StartRecording(10);
            else
            {
                SessionState.SetInt("PickShorts.Duration", 10);
                StartMenu();
            }
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) StopRecording(false);
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true;
                if (SessionState.GetBool("PickShorts.RecordOnPlay", false))
                {
                    SessionState.SetBool("PickShorts.RecordOnPlay", false);
                    pendingStart = EditorApplication.timeSinceStartup + 2;
                }
            }
        }

        public static void OpenForCapture()
        {
            if (!EditorApplication.isPlaying)
                EditorSceneManager.OpenScene("Assets/Scenes/GameStarter.unity", OpenSceneMode.Single);
            SetPortraitGameView();
        }

        static void SetPortraitGameView()
        {
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            PlayModeWindow.SetCustomRenderingResolution(1080, 1920, "Shorts 1080x1920");
        }

        static void StartRecording(int seconds)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode is required.");
            if (controller != null && controller.IsRecording()) throw new InvalidOperationException("Already recording.");
            SetPortraitGameView();
            EditorUtility.audioMasterMute = false;
            AudioListener.volume = 1f;
            AudioListener.pause = false;
            if (AudioManager.IsCheckInstance())
            {
                AudioManager.Instance.SfxVolume = 1f;
                AudioManager.Instance.BgmVolume = 0f;
            }
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.SetRecordModeToManual();
            settings.FrameRate = 30;
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.CapFrameRate = true;
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Shorts Portrait With Game Audio";
            movie.Enabled = true;
            movie.CaptureAudio = true;
            movie.CaptureAlpha = false;
            movie.EncoderSettings = new CoreEncoderSettings {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
                EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High,
                TargetBitRate = 12, GopSize = 60
            };
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1080, OutputHeight = 1920 };
            string date = SessionState.GetString("PickShorts.Date", DateTime.Now.ToString("yyyyMMdd"));
            recordingPath = Path.Combine(Raw, "bbop_rec_" + date + "_" + DateTime.Now.ToString("HHmmss_fff"));
            movie.OutputFile = recordingPath;
            settings.AddRecorderSettings(movie);
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (!controller.StartRecording()) throw new InvalidOperationException("Recorder could not start.");
            startFrame = Time.frameCount;
            stopAtFrame = seconds > 0 ? startFrame + seconds * 30 : -1;
            AppendEvent("record_start", new { path = recordingPath + ".mp4", frame = startFrame });
        }

        static void StopRecording(bool exitPlay)
        {
            if (controller != null)
            {
                controller.StopRecording();
                AppendEvent("record_stop", new { path = recordingPath + ".mp4", frames = Time.frameCount - startFrame });
                controller = null;
            }
            stopAtFrame = -1;
            SetMove(-1, 0);
            if (exitPlay && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        static CraneMovingControl Movement()
        {
            if (!Crane.TryGetSetInstance(out var crane)) return null;
            return (CraneMovingControl)typeof(Crane).GetField("_craneMovingControl", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(crane);
        }

        static void SetMove(int direction, float seconds)
        {
            var movement = Movement();
            if (movement != null)
                for (int i = 0; i < 4; i++) movement.SetMovingValue(i, i == direction);
            moveDirection = direction;
            moveUntil = Time.unscaledTime + seconds;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (pendingStart > 0 && EditorApplication.timeSinceStartup >= pendingStart && EditorApplication.isPlaying)
            {
                pendingStart = 0;
                try { StartRecording(SessionState.GetInt("PickShorts.Duration", 0)); SessionState.SetInt("PickShorts.Duration", 0); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (moveDirection >= 0 && Time.unscaledTime >= moveUntil) SetMove(-1, 0);
            if (stopAtFrame >= 0 && Time.frameCount >= stopAtFrame) StopRecording(false);
            var requestPath = Path.Combine(Work, "command.json");
            if (File.Exists(requestPath))
            {
                try
                {
                    var request = JObject.Parse(File.ReadAllText(requestPath));
                    string id = (string)request["id"];
                    if (!string.IsNullOrEmpty(id) && id != SessionState.GetString("PickShorts.LastCommand", ""))
                    {
                        SessionState.SetString("PickShorts.LastCommand", id);
                        object result;
                        try { result = new { id, ok = true, data = Execute(request) }; }
                        catch (Exception e) { result = new { id, ok = false, error = e.ToString() }; }
                        WriteJson("response.json", result);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Shorts] Command read: " + e.Message); }
            }
            if (EditorApplication.timeSinceStartup >= nextStatus)
            {
                nextStatus = EditorApplication.timeSinceStartup + 1;
                try { WriteJson("state.json", State(false)); } catch { }
            }
        }

        static object Execute(JObject request)
        {
            string action = (string)request["action"];
            switch (action)
            {
                case "status": return State(true);
                case "open": OpenForCapture(); break;
                case "play":
                    OpenForCapture();
                    PlayerPrefs.SetString("LanguageType", "KOR");
                    PlayerPrefs.SetFloat("BgmVolume", 0);
                    SessionState.SetString("PickShorts.Date", (string)request["date"] ?? "20261101");
                    EditorApplication.isPlaying = true; break;
                case "record": StartRecording((int?)request["seconds"] ?? 0); break;
                case "stop": StopRecording((bool?)request["exitPlay"] ?? false); break;
                case "menu":
                    if (!EditorApplication.ExecuteMenuItem((string)request["path"])) throw new Exception("Menu not found.");
                    break;
                case "enter": UIHud.Get().Control.OnClickStageEnterBtn(); break;
                case "attempt": UIHud.Get().Control.OnClickPlayBtn(); break;
                case "grab": UIHud.Get().Control.OnClickGrapBtn(); break;
                case "move": SetMove((int)request["direction"], (float?)request["seconds"] ?? 0.2f); break;
                case "click":
                    var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .First(x => x.GetInstanceID() == (int)request["instanceId"]);
                    if (!button.IsInteractable()) throw new Exception("Button is not interactable.");
                    var startInput = button.GetComponent<UILoadingStartInput>();
                    if (startInput != null) startInput.OnSubmit(new BaseEventData(EventSystem.current));
                    else button.onClick.Invoke();
                    break;
                case "screenshot":
                    string name = Path.GetFileName((string)request["name"] ?? "preview.png");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Work, name)); break;
                case "refresh": AssetDatabase.Refresh(); break;
                default: throw new ArgumentException("Unknown action: " + action);
            }
            AppendEvent("command", request);
            return State(false);
        }

        static object State(bool detailed)
        {
            Crane.TryGetSetInstance(out var crane);
            var state = new JObject {
                ["project"] = Root, ["playing"] = EditorApplication.isPlaying,
                ["recording"] = controller != null && controller.IsRecording(),
                ["recordingPath"] = recordingPath, ["frame"] = Time.frameCount,
                ["width"] = Screen.width, ["height"] = Screen.height,
                ["craneStatus"] = crane != null ? crane.Status : -1,
                ["gameStatus"] = GameMain.IsCheckInstance() ? GameMain.Instance.CurrentStatus.ToString() : "none",
                ["stage"] = GameQuestManager.IsCheckInstance() ? GameQuestManager.Instance.CurrentStage : 0,
                ["bossHp"] = GameQuestManager.IsCheckInstance() ? GameQuestManager.Instance.BossHp : 0,
                ["bossMaxHp"] = GameQuestManager.IsCheckInstance() ? GameQuestManager.Instance.MaxBossHp : 0,
                ["gameplayStarted"] = CloudSaveManager.IsCheckInstance() && CloudSaveManager.Instance.GameplayStarted
            };
            state["audio"] = new JObject {
                ["editorMuted"] = EditorUtility.audioMasterMute,
                ["listenerVolume"] = AudioListener.volume, ["listenerPaused"] = AudioListener.pause,
                ["sfxVolume"] = AudioManager.IsCheckInstance() ? AudioManager.Instance.SfxVolume : -1,
                ["bgmVolume"] = AudioManager.IsCheckInstance() ? AudioManager.Instance.BgmVolume : -1
            };
            if (detailed && EditorApplication.isPlaying)
            {
                state["buttons"] = JArray.FromObject(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(x => x.gameObject.activeInHierarchy).Select(x => new { id = x.GetInstanceID(), name = x.name, interactable = x.IsInteractable(),
                        text = string.Join(" ", x.GetComponentsInChildren<TMPro.TMP_Text>().Select(v => v.text)) }));
                state["dolls"] = JArray.FromObject(UnityEngine.Object.FindObjectsByType<DollBoxActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Select(x => new { id = x.GetInstanceID(), name = x.name, type = x.GetType().Name, x = x.transform.position.x, y = x.transform.position.y, z = x.transform.position.z }));
                if (crane != null)
                {
                    var body = (Rigidbody)typeof(Crane).GetField("pivotRigidbody", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(crane);
                    state["cranePosition"] = new JObject { ["x"] = body.position.x, ["y"] = body.position.y, ["z"] = body.position.z };
                }
            }
            return state;
        }

        static void WriteJson(string name, object data) => File.WriteAllText(Path.Combine(Work, name), JsonConvert.SerializeObject(data, Formatting.Indented));
        static void AppendEvent(string kind, object data) => File.AppendAllText(Path.Combine(Work, "capture_events.jsonl"), JsonConvert.SerializeObject(new { kind, gameTime = Time.unscaledTime, frame = Time.frameCount, data }) + "\n");
    }
}
#endif
