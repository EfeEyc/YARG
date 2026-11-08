using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YARG.Core.Logging;

namespace YARG.Helpers.UI
{
    /// <summary>
    /// Manages full-screen adaptive aspect ratio scaling, high-refresh-rate frame pacing,
    /// and viewport layout across ultra-wide and square/tablet display aspect ratios.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class AdaptiveDisplayManager : MonoBehaviour
    {
        private static AdaptiveDisplayManager _instance;

        private const float MIN_COMPACT_ASPECT = 0.65f;
        private const float MAX_COMPACT_ASPECT = 1.55f;

        /// <summary>
        /// Normalized NDC center offset for each half of a 2-player split layout.
        /// </summary>
        public const float SPLIT_PANEL_CENTER_NDC = 0.52f;

        /// <summary>
        /// Single-player highway height percentage on tall/square displays.
        /// </summary>
        public const float ADAPTIVE_SINGLE_PLAYER_HEIGHT_PERCENT = 0.84f;

        /// <summary>
        /// Single-player highway width percentage on tall/square displays.
        /// </summary>
        public const float ADAPTIVE_SINGLE_PLAYER_WIDTH_PERCENT = 0.88f;

        /// <summary>
        /// Two-player highway height percentage on tall/square displays.
        /// </summary>
        public const float ADAPTIVE_TWO_PLAYER_HEIGHT_PERCENT = 0.76f;

        public static bool IsCompactOrTabletAspect
        {
            get
            {
                if (Screen.height <= 0 || Screen.width <= 0)
                {
                    return false;
                }

                float aspect = (float) Math.Max(Screen.width, Screen.height) /
                               Math.Min(Screen.width, Screen.height);
                return aspect >= MIN_COMPACT_ASPECT && aspect <= MAX_COMPACT_ASPECT;
            }
        }

        public static float CurrentAspectRatio =>
            Screen.height > 0 ? (float) Screen.width / Screen.height : 16f / 9f;

        public static event Action<bool> OnDisplayModeChanged;

        private int _lastWidth;
        private int _lastHeight;
        private bool _lastCompactMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("AdaptiveDisplayManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AdaptiveDisplayManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            ConfigureDisplayAndOrientation();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void ConfigureDisplayAndOrientation()
        {
#if UNITY_IOS
            Screen.fullScreen = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;

            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = IsCompactOrTabletAspect;
            Screen.autorotateToPortraitUpsideDown = IsCompactOrTabletAspect;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyFullScreenCanvasScaling();
        }

        private void Update()
        {
            if (Screen.width != _lastWidth || Screen.height != _lastHeight)
            {
                _lastWidth = Screen.width;
                _lastHeight = Screen.height;

                bool currentCompactMode = IsCompactOrTabletAspect;
                ConfigureDisplayAndOrientation();
                ApplyFullScreenCanvasScaling();

                if (currentCompactMode != _lastCompactMode)
                {
                    _lastCompactMode = currentCompactMode;
                    YargLogger.LogFormatInfo(
                        "Display mode updated: {0}x{1} (aspect {2:0.00})",
                        Screen.width, Screen.height, CurrentAspectRatio);
                    OnDisplayModeChanged?.Invoke(currentCompactMode);
                }
            }
        }

        public static void ApplyFullScreenCanvasScaling()
        {
            var scalers = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var scaler in scalers)
            {
                if (scaler == null)
                {
                    continue;
                }

                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                }
            }
        }

        public static Rect GetEnvelopeUvRect(int textureWidth, int textureHeight, bool flipVertically = true)
        {
            if (textureWidth <= 0 || textureHeight <= 0 || Screen.width <= 0 || Screen.height <= 0)
            {
                return flipVertically ? new Rect(0f, 0f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            }

            float screenAspect = (float) Screen.width / Screen.height;
            float texAspect = (float) textureWidth / textureHeight;

            float uvWidth = 1f;
            float uvHeight = 1f;
            float uvX = 0f;
            float uvY = 0f;

            if (screenAspect > texAspect)
            {
                uvHeight = texAspect / screenAspect;
                uvY = (1f - uvHeight) * 0.5f;
            }
            else if (screenAspect < texAspect)
            {
                uvWidth = screenAspect / texAspect;
                uvX = (1f - uvWidth) * 0.5f;
            }

            if (flipVertically)
            {
                return new Rect(uvX, uvY, uvWidth, -uvHeight);
            }

            return new Rect(uvX, uvY, uvWidth, uvHeight);
        }
    }
}
