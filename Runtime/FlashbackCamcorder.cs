using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Flashback.Encoder;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using ThreadPriority = System.Threading.ThreadPriority;
using UnityObject = UnityEngine.Object;

namespace Flashback
{
    public enum CamcorderState
    {
        Recording,
        Suspending,
        Stopped
    }

    public enum CamcorderFileStyle
    {
        Timestamp,
        Numbered
    }

    public enum CamcorderAspectRatio
    {
        Square,
        FromCamera,
        Custom
    }

    [AddComponentMenu("Capipocavara/Flashback Camcorder")]
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class FlashbackCamcorder : MonoBehaviour, ISerializationCallbackReceiver
    {
        private const int MinWidth = 8;
        private const int MinHeight = 8;
        private const int MaxFps = 30;

        [SerializeField, Min(MinWidth)] private int width = 480;
        [SerializeField, Min(MinHeight)] private int height = 270;
        [SerializeField] private CamcorderAspectRatio aspectRatio = CamcorderAspectRatio.Square;
        [SerializeField, Range(1, MaxFps)] private int framesPerSecond = 20;
        [SerializeField, Range(1, 100)] private int quality = 15;
        [SerializeField, Min(-1)] private int repeat;
        [SerializeField, Min(0.1f)] private float frameBufferSize = 5f;
        [SerializeField, Range(0f, 2f)] private float longPressDelay = 0.5f;
        [SerializeField] private string currentSaveFolder = "";

#if UNITY_EDITOR
        private int maxFrameBufferSize;
        private int failedAsyncGPUReadbackRequest;
        private string lastFile;
        private float saveProgress;
#endif

        public string SaveFolder {
            get {
                GeneratePath();
                return currentSaveFolder;
            }
            [UsedImplicitly]
            set {
                currentSaveFolder = value;
                GeneratePath();
            }
        }

        [SerializeField] private string currentFilePrefix = "Flashback";

        public string FilePrefix {
            get {
                folderNumberedGifCount = Directory.GetFiles(currentSaveFolder, currentFilePrefix + "-????.gif").Length + 1;
                return currentFilePrefix;
            }
            [UsedImplicitly]
            set {
                currentFilePrefix = value;
                folderNumberedGifCount = Directory.GetFiles(currentSaveFolder, currentFilePrefix + "-????.gif").Length + 1;
            }
        }

        [SerializeField] private CamcorderFileStyle currentFileStyle = CamcorderFileStyle.Timestamp;

        public CamcorderFileStyle FileStyle {
            get => currentFileStyle;
            [UsedImplicitly]
            set {
                currentFileStyle = value;
                folderNumberedGifCount = Directory.GetFiles(currentSaveFolder, currentFilePrefix + "-????.gif").Length + 1;
            }
        }

        public ThreadPriority encodingPriority = ThreadPriority.BelowNormal;
        public bool autoStart = true;

#if UNITY_EDITOR
        public string Statistics {
            get {
                if (!isInitialized)
                    return "Initializing";

                var stats = new StringBuilder().Append("Recorder State: ").Append(State).AppendLine();
                stats.Append("Frames to keep: ").Append(maxFramesToKeep).AppendLine();
                stats.Append("Frames to capture: ").Append(maxFramesToCapture).AppendLine();
                stats.Append("Current FrameBuffer size: ").Append(rawFrames.Count).AppendLine();
                stats.Append("Max FrameBuffer size: ").Append(maxFrameBufferSize).AppendLine();
                stats.AppendLine();
                stats.Append("Active AsyncGPUReadbackRequest: ").Append(asyncRequests.Count).AppendLine();
                stats.Append("Failed AsyncGPUReadbackRequest: ").Append(failedAsyncGPUReadbackRequest).AppendLine();
                stats.AppendLine();
                stats.Append("Grabbed Gif Frames: ").Append(grabbedGifFrames.Count).AppendLine();
                stats.Append("Encoding Jobs: ").Append(encodingJobs.Count);
                if (IsSaving) {
                    stats.AppendLine();
                    stats.Append("Progress Report: ").AppendFormat("{0:f2}%", saveProgress);
                }

                if (string.IsNullOrEmpty(lastFile))
                    return stats.ToString();

                stats.AppendLine();
                stats.AppendLine("Last File Saved: ").Append(lastFile);
                return stats.ToString();
            }
        }

        public float EstimatedMemoryUse => framesPerSecond * frameBufferSize * width * height * 4 / (1024 * 1024);
#endif

        [UsedImplicitly] public CamcorderState State { get; private set; } = CamcorderState.Stopped;
        [UsedImplicitly] public bool IsSaving { get; private set; }

        public UnityEvent<int, float> onFileSaveProgress;
        public UnityEvent<int, string> onFileSaved;
        public UnityEvent onStopped;

        private static uint frameID;
        private int maxFramesToKeep;
        private int maxFramesToCapture;
        private float passedTime;
        private float timePerFrame;
        private int folderNumberedGifCount = 1;
        private Queue<RenderTexture> rawFrames;
        private List<GifFrame> grabbedGifFrames;
        private int gifFramesToGrab;
        private Queue<AsyncGPUReadbackRequest> asyncRequests;
        private List<EncoderWorker> encodingJobs;
        private readonly Queue<Action> unityEventQueue = new();
        private Camera attachedCamera;
        private bool isInitialized;

        public FlashbackCamcorder(string lastFile) => this.lastFile = lastFile;

        private void Awake()
        {
            var capacity = Mathf.RoundToInt((frameBufferSize + longPressDelay) * framesPerSecond);

            rawFrames = new Queue<RenderTexture>(capacity);
            grabbedGifFrames = new List<GifFrame>(capacity * 2);
            encodingJobs = new List<EncoderWorker>(10);
            asyncRequests = new Queue<AsyncGPUReadbackRequest>(5);
            attachedCamera = GetComponent<Camera>();

            onFileSaveProgress ??= new UnityEvent<int, float>();
            onFileSaved ??= new UnityEvent<int, string>();
            onStopped ??= new UnityEvent();

            Init();
        }

        private void Update()
        {
            while (unityEventQueue.Count > 0) {
                unityEventQueue.Dequeue().Invoke();
            }

            passedTime += Time.unscaledDeltaTime;
            if (passedTime >= timePerFrame) {
                if (State != CamcorderState.Stopped && !attachedCamera.enabled)
                    attachedCamera.Render();

                if (IsSaving && gifFramesToGrab > 0 && rawFrames.Peek() != null)
                    asyncRequests.Enqueue(AsyncGPUReadback.Request(rawFrames.Peek()));
            }

            if (asyncRequests.Count > 0 && asyncRequests.Peek().done) {
                var req = asyncRequests.Dequeue();
                if (!req.hasError) {
                    var frame = new GifFrame {
                        Width = width,
                        Height = height,
                        Data = req.GetData<Color32>().ToArray(),
                        ID = frameID++
                    };

                    // Sanity check the frame
                    if (frame.Width * frame.Height == frame.Data.Length) {
                        lock (grabbedGifFrames) {
                            grabbedGifFrames.Add(frame);
                        }

                        gifFramesToGrab--;
                    } else
                        Debug.Log("Discarding bad frame.");
                }
#if UNITY_EDITOR
                else {
                    failedAsyncGPUReadbackRequest++;
                }
#endif
            }

            if (encodingJobs.Count <= 0 || IsSaving)
                return;

            IsSaving = true;
            encodingJobs[0].Start();
            encodingJobs.RemoveAt(0);
        }

        private void LateUpdate()
        {
            if (State != CamcorderState.Suspending || IsSaving)
                return;

            isInitialized = false;
            if (rawFrames != null) {
                foreach (var rt in rawFrames)
                    Flush(rt);

                rawFrames.Clear();
            }

            grabbedGifFrames.Clear();
            asyncRequests.Clear();
            State = CamcorderState.Stopped;
            onStopped.Invoke();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (passedTime >= timePerFrame) {
                passedTime -= timePerFrame;

                if (State != CamcorderState.Stopped) {
                    RenderTexture rt = null;
                    // Clean up superfluous frames and recycle the last one for the new frame
                    if (rawFrames.Count >= maxFramesToKeep)
                        rt = rawFrames.Dequeue();

                    if (rt == null) {
                        rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) {
                            wrapMode = TextureWrapMode.Clamp,
                            filterMode = FilterMode.Bilinear,
                            anisoLevel = 0
                        };
                    }

                    Graphics.Blit(source, rt, Vector2.one, Vector2.zero);
                    rawFrames.Enqueue(rt);
#if UNITY_EDITOR
                    maxFrameBufferSize = Math.Max(maxFrameBufferSize, rawFrames.Count);
#endif
                }
            }

            Graphics.Blit(source, destination);
        }

        private void OnDestroy() => Stop();
        
        private void Init()
        {
            maxFramesToCapture = Mathf.RoundToInt(frameBufferSize * framesPerSecond);
            maxFramesToKeep = maxFramesToCapture + Mathf.RoundToInt(longPressDelay * framesPerSecond);
            timePerFrame = 1f / framesPerSecond;
            passedTime = 0f;

            ComputeHeight();
            GeneratePath();

            // warm starting
            for (var i = 0; i < maxFramesToKeep; i++)
                rawFrames.Enqueue(null);

            gifFramesToGrab = 0;

            // Make sure the output folder is set or use the default one
            if (autoStart)
                State = CamcorderState.Recording;

            isInitialized = true;
        }
        
        [UsedImplicitly]
        public void Setup(int initWidth, int initHeight, CamcorderAspectRatio initAspectRatio, int initFramesPerSecond, int initQuality, int initRepeat, float initFrameBufferSize, float initLongPressDelay)
        {
            switch (State) {
                case CamcorderState.Recording:
                    Debug.Log("Still recording. Call Stop() first.");
                    break;
                case CamcorderState.Suspending:
                    Debug.Log("Still suspending. Wait until stopped. Subscribe to 'OnStopped' to get notified.");
                    break;
                case CamcorderState.Stopped:
                    width = Mathf.Max(MinWidth, initWidth);
                    height = Mathf.Max(MinHeight, initHeight);
                    aspectRatio = initAspectRatio;
                    framesPerSecond = Mathf.Clamp(initFramesPerSecond, 1, MaxFps);
                    quality = Mathf.Clamp(initQuality, 1, 100);
                    repeat = Mathf.Max(-1, initRepeat);
                    frameBufferSize = Mathf.Max(0.1f, initFrameBufferSize);
                    longPressDelay = Mathf.Clamp(initLongPressDelay, 0f, 2f);

                    Init();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [UsedImplicitly]
        public void Record()
        {
            if (State == CamcorderState.Stopped && !isInitialized)
                Init();

            State = CamcorderState.Recording;
        }

        [UsedImplicitly]
        public void Stop()
        {
            if (State != CamcorderState.Recording)
                return;

            State = CamcorderState.Suspending;
            encodingJobs.Clear();
        }

        [UsedImplicitly]
        public void Snap(string filename = null)
        {
            if (rawFrames.Count == 0) {
                Debug.LogWarning("Nothing to save. Maybe you forgot to start the camcorder?");
                return;
            }

            if (State != CamcorderState.Recording)
                return;

            if (string.IsNullOrEmpty(filename))
                filename = FilePrefix + FileStyle switch {
                    CamcorderFileStyle.Timestamp => "-" + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "'" + DateTime.Now.Millisecond.ToString("D4"),
                    CamcorderFileStyle.Numbered => "-" + folderNumberedGifCount++.ToString("D4"),
                    _ => ""
                };

            gifFramesToGrab = maxFramesToCapture;
            encodingJobs.Add(new EncoderWorker(encodingPriority) {
                GifFrames = grabbedGifFrames,
                FramesToEncode = maxFramesToCapture,
                StartFrame = frameID,
                FilePath = SaveFolder + "\\" + filename + ".gif",
                Encoder = new GifEncoder(width, height, repeat, quality, Mathf.RoundToInt(timePerFrame * 1000f)),
                OnFileSaveProgress = FileSaveProgress,
                OnFileSaved = FileSaved
            });
        }


        private void FileSaveProgress(int id, float progress)
        {
            unityEventQueue.Enqueue(() => { onFileSaveProgress?.Invoke(id, progress); });
#if UNITY_EDITOR
            saveProgress = progress * 100f;
#endif
        }

        private void FileSaved(int id, string filename)
        {
            IsSaving = false;
            if (encodingJobs.Count == 0)
                grabbedGifFrames.Clear();

            unityEventQueue.Enqueue(() => { onFileSaved?.Invoke(id, filename); });
#if UNITY_EDITOR
            saveProgress = 0f;
            lastFile = filename;
#endif
        }


        public void GeneratePath()
        {
            if (string.IsNullOrEmpty(currentSaveFolder)) {
#if UNITY_EDITOR
                currentSaveFolder = Directory.GetParent(Application.dataPath)?.FullName + "\\Flashbacks";
#else
			    currentSaveFolder = Application.persistentDataPath + "\\Flashbacks";
#endif
            }

            if (!Directory.Exists(currentSaveFolder))
                Directory.CreateDirectory(currentSaveFolder);

            folderNumberedGifCount = Directory.GetFiles(currentSaveFolder, currentFilePrefix + "-????.gif").Length + 1;
        }

        public void ComputeHeight()
        {
            switch (aspectRatio) {
                case CamcorderAspectRatio.Square:
                    height = width;
                    break;
                case CamcorderAspectRatio.FromCamera:
#if UNITY_EDITOR
                    height = Mathf.RoundToInt(width / GetComponent<Camera>().aspect);
#else
                    height = Mathf.RoundToInt(width / attachedCamera.aspect);
#endif
                    break;
                case CamcorderAspectRatio.Custom:
                default:
                    // Set by hand
                    break;
            }
        }

        private static void Flush(UnityObject obj)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
#else
            Destroy(obj);
#endif
        }

        public void OnBeforeSerialize()
        {
            width = Mathf.Max(MinWidth, width);
            height = Mathf.Max(MinHeight, height);
            framesPerSecond = Mathf.Clamp(framesPerSecond, 1, MaxFps);
            quality = Mathf.Clamp(quality, 1, 100);
            repeat = Mathf.Max(-1, repeat);
            frameBufferSize = Mathf.Max(0.1f, frameBufferSize);
            longPressDelay = Mathf.Clamp(longPressDelay, 0f, 2f);
            ComputeHeight();
        }

        public void OnAfterDeserialize() => ComputeHeight();
    }
}