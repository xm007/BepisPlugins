﻿using alphaShot;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepisPlugins;
using Illusion.Game;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
#if KK
using StrayTech;
#endif

namespace Screencap
{
    /// <summary>
    /// Plugin for taking high quality screenshots.
    /// </summary>
    public partial class ScreenshotManager
    {
        public const string GUID = "com.bepis.bepinex.screenshotmanager";
        public const string PluginName = "Screenshot Manager";
        public const string Version = Metadata.PluginsVersion;
        internal static new ManualLogSource Logger;
        private const int ScreenshotSizeMax = 4096;
        private const int ScreenshotSizeMin = 2;

        public static ScreenshotManager Instance { get; private set; }

        private readonly string screenshotDir = Path.Combine(Paths.GameRootPath, @"UserData\cap\");
        internal AlphaShot2 currentAlphaShot;

        #region Config properties

        public static SavedKeyboardShortcut KeyCapture { get; private set; }
        public static SavedKeyboardShortcut KeyCaptureAlpha { get; private set; }
        public static SavedKeyboardShortcut KeyCapture360 { get; private set; }
        public static SavedKeyboardShortcut KeyGui { get; private set; }
        public static SavedKeyboardShortcut KeyCaptureAlphaIn3D { get; private set; }
        public static SavedKeyboardShortcut KeyCapture360in3D { get; private set; }

        [AcceptableValueRange(ScreenshotSizeMin, ScreenshotSizeMax, false)]
        public static ConfigWrapper<int> ResolutionX { get; private set; }

        [AcceptableValueRange(ScreenshotSizeMin, ScreenshotSizeMax, false)]
        public static ConfigWrapper<int> ResolutionY { get; private set; }

        [AcceptableValueList(new object[] { 1024, 2048, 4096, 8192 })]
        public static ConfigWrapper<int> Resolution360 { get; private set; }

        [AcceptableValueRange(1, 4, false)]
        public static ConfigWrapper<int> DownscalingRate { get; private set; }

        [AcceptableValueRange(1, 4, false)]
        public static ConfigWrapper<int> CardDownscalingRate { get; private set; }

        public static ConfigWrapper<bool> CaptureAlpha { get; private set; }

        public static ConfigWrapper<bool> ScreenshotMessage { get; private set; }

        [AcceptableValueRange(0.01f, 0.5f, false)]
        public static ConfigWrapper<float> EyeSeparation { get; private set; }

        [AcceptableValueRange(0f, 1f)]
        public static ConfigWrapper<float> ImageSeparationOffset { get; private set; }

        public static ConfigWrapper<bool> UseJpg { get; private set; }

        [AcceptableValueRange(1, 100, true)]
        public static ConfigWrapper<int> JpgQuality { get; private set; }

        //TODO:public static ConfigWrapper<NameFormat> ScreenshotNameFormat { get; private set; }

        [Advanced(true)]
        public static ConfigWrapper<string> ScreenshotNameOverride { get; private set; }

        protected void Awake()
        {
            if (Instance)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            Logger = base.Logger;

            KeyCapture = new SavedKeyboardShortcut(Config, "Take UI screenshot", "Capture a simple \"as you see it\" screenshot of the game. Not affected by settings for rendered screenshots.", new KeyboardShortcut(KeyCode.F9));
            KeyCaptureAlpha = new SavedKeyboardShortcut(Config, "Take rendered screenshot", null, new KeyboardShortcut(KeyCode.F11));
            KeyCapture360 = new SavedKeyboardShortcut(Config, "Take 360 screenshot", "Captures a 360 screenshot around current camera. The created image is in equirectangular format and can be viewed by most 360 image viewers (e.g. Google Cardboard).", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl));
            KeyGui = new SavedKeyboardShortcut(Config, "Open settings window", null, new KeyboardShortcut(KeyCode.F11, KeyCode.LeftShift));

            KeyCaptureAlphaIn3D = new SavedKeyboardShortcut(Config, "Take rendered 3D screenshot", "Capture a high quality screenshot without UI in stereoscopic 3D (2 captures for each eye in one image). These images can be viewed by crossing your eyes or any stereoscopic image viewer.", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftAlt));
            KeyCapture360in3D = new SavedKeyboardShortcut(Config, "Take 360 3D screenshot", "Captures a 360 screenshot around current camera in stereoscopic 3D (2 captures for each eye in one image). These images can be viewed by image viewers supporting 3D stereo format (e.g. VR Media Player - 360° Viewer).", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl, KeyCode.LeftShift));

            Resolution360 = Config.Wrap("360 Screenshots", "360 screenshot resolution", "Horizontal resolution (width) of 360 degree/panorama screenshots. Decrease if you have issues. WARNING: Memory usage can get VERY high - 4096 needs around 4GB of free RAM/VRAM to create, 8192 will need much more.", 4096);

            DownscalingRate = Config.Wrap("Render Settings", "Screenshot upsampling ratio", "Capture screenshots in a higher resolution and then downscale them to desired size. Prevents aliasing, perserves small details and gives a smoother result, but takes longer to create.", 2);
            CardDownscalingRate = Config.Wrap("Render Settings", "Card image upsampling ratio", "Capture character card images in a higher resolution and then downscale them to desired size. Prevents aliasing, perserves small details and gives a smoother result, but takes longer to create.", 3);
            CaptureAlpha = Config.Wrap("Render Settings", "Transparency in rendered screenshots", "Replaces background with transparency in rendered image. Works only if there are no 3D objects covering the background (e.g. the map). Works well in character creator and studio.", true);
            ScreenshotMessage = Config.Wrap("General", "Show messages on screen", "Whether screenshot messages will be displayed on screen. Messages will still be written to the log.", true);
            ResolutionX = Config.Wrap("Render Output Resolution", "Horizontal", "Horizontal size (width) of rendered screenshots in pixels. Doesn't affect UI and 360 screenshots.", Screen.width);
            ResolutionY = Config.Wrap("Render Output Resolution", "Vertical", "Vertical size (height) of rendered screenshots in pixels. Doesn't affect UI and 360 screenshots.", Screen.height);
            EyeSeparation = Config.Wrap("3D Settings", "3D screenshot eye separation", "Distance between the two captured stereoscopic screenshots in arbitrary units.", 0.18f);
            ImageSeparationOffset = Config.Wrap("3D Settings", "3D screenshot image separation offset", "Move images in stereoscopic screenshots closer together by this percentage (discards overlapping parts). Useful for viewing with crossed eyes. Does not affect 360 stereoscopic screenshots.", 0.25f);
            UseJpg = Config.Wrap("JPG Settings", "Save screenshots as .jpg instead of .png", "Save screenshots in lower quality in return for smaller file sizes. Transparency is NOT supported in .jpg screenshots. Strongly consider not using this option if you want to share your work.", false);
            JpgQuality = Config.Wrap("3D Settings", "Quality of .jpg files", "Lower quality = lower file sizes. Even 100 is worse than a .png file.", 100);
            //TODO:ScreenshotNameFormat = Config.Wrap("General", "Screenshot filename format", "Screenshots will be saved with names of the selected format. Name stands for the current game name (CharaStudio, Koikatu, etc.)", NameFormat.NameDateType);
            ScreenshotNameOverride = Config.Wrap("General", "Screenshot filename Name override", "Forces the Name part of the filename to always be this instead of varying depending on the name of the current game. Use \"Koikatsu\" to get the old filename behaviour.", "");

            ResolutionX.SettingChanged += (sender, args) => ResolutionXBuffer = ResolutionX.Value.ToString();
            ResolutionY.SettingChanged += (sender, args) => ResolutionYBuffer = ResolutionY.Value.ToString();

            SceneManager.sceneLoaded += (s, a) => InstallSceenshotHandler();
            InstallSceenshotHandler();

            if (!Directory.Exists(screenshotDir))
                Directory.CreateDirectory(screenshotDir);

            Hooks.InstallHooks();

            I360Render.Init();
        }
        #endregion

        private string GetUniqueFilename(string capType)
        {
            string filename;

            // Replace needed for Koikatu Party to get ride of the space
            var productName = Application.productName.Replace(" ", "");
            if (!string.IsNullOrEmpty(ScreenshotNameOverride.Value))
                productName = ScreenshotNameOverride.Value;

            var extension = UseJpg.Value ? "jpg" : "png";

            //TODO:
            filename = $"{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{capType}.{extension}";
            //switch (ScreenshotNameFormat.Value)
            //{
            //    case NameFormat.NameDate:
            //        filename = $"{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
            //        break;
            //    case NameFormat.NameTypeDate:
            //        filename = $"{productName}-{capType}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
            //        break;
            //    case NameFormat.NameDateType:
            //        filename = $"{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{capType}.{extension}";
            //        break;
            //    case NameFormat.TypeDate:
            //        filename = $"{capType}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
            //        break;
            //    case NameFormat.TypeNameDate:
            //        filename = $"{capType}-{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
            //        break;
            //    case NameFormat.Date:
            //        filename = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException("Unhandled screenshot filename format - " + ScreenshotNameFormat.Value);
            //}

            return Path.GetFullPath(Path.Combine(screenshotDir, filename));
        }

        private static byte[] EncodeToFile(Texture2D result) => UseJpg.Value ? result.EncodeToJPG(JpgQuality.Value) : result.EncodeToPNG();

        private static byte[] EncodeToXmpFile(Texture2D result) => UseJpg.Value ? I360Render.InsertXMPIntoTexture2D_JPEG(result, JpgQuality.Value) : I360Render.InsertXMPIntoTexture2D_PNG(result);

        private void InstallSceenshotHandler()
        {
            if (!Camera.main || !Camera.main.gameObject) return;
            currentAlphaShot = Camera.main.gameObject.GetOrAddComponent<AlphaShot2>();
        }

        protected void Update()
        {
            if (KeyGui.IsDown())
            {
                uiShow = !uiShow;
                ResolutionXBuffer = ResolutionX.Value.ToString();
                ResolutionYBuffer = ResolutionY.Value.ToString();
            }
            else if (KeyCaptureAlpha.IsDown()) StartCoroutine(TakeCharScreenshot(false));
            else if (KeyCapture.IsDown()) TakeScreenshot();
            else if (KeyCapture360.IsDown()) StartCoroutine(Take360Screenshot(false));
            else if (KeyCaptureAlphaIn3D.IsDown()) StartCoroutine(TakeCharScreenshot(true));
            else if (KeyCapture360in3D.IsDown()) StartCoroutine(Take360Screenshot(true));
        }

        private void TakeScreenshot()
        {
            var filename = GetUniqueFilename("UI");
#if KK
            Application.CaptureScreenshot(filename);
#elif EC
            ScreenCapture.CaptureScreenshot(filename);
#endif

            StartCoroutine(TakeScreenshotLog(filename));
        }

        private IEnumerator TakeScreenshotLog(string filename)
        {
            yield return new WaitForEndOfFrame();
            Utils.Sound.Play(SystemSE.photo);
            Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"UI screenshot saved to {filename}");
        }

        private IEnumerator TakeCharScreenshot(bool in3D)
        {
            if (currentAlphaShot == null)
            {
                Logger.Log(LogLevel.Message, "Can't render a screenshot here, try UI screenshot instead");
                yield break;
            }

            if (!in3D)
            {
                yield return new WaitForEndOfFrame();
                var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);

                var filename = GetUniqueFilename("Render");
                File.WriteAllBytes(filename, EncodeToFile(capture));
                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"Character screenshot saved to {filename}");

                Destroy(capture);
            }
            else
            {
                var targetTr = Camera.main.transform;

                ToggleCameraControllers(targetTr, false);
                Time.timeScale = 0.01f;
                yield return new WaitForEndOfFrame();

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;
                // Let the game render at the new position
                yield return new WaitForEndOfFrame();
                var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);

                targetTr.position -= targetTr.right * EyeSeparation.Value;
                yield return new WaitForEndOfFrame();
                var capture2 = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;

                ToggleCameraControllers(targetTr, true);
                Time.timeScale = 1;

                var result = StitchImages(capture, capture2, ImageSeparationOffset.Value);

                var filename = GetUniqueFilename("3D-Render");
                File.WriteAllBytes(filename, EncodeToFile(result));

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"3D Character screenshot saved to {filename}");

                Destroy(capture);
                Destroy(capture2);
                Destroy(result);
            }

            Utils.Sound.Play(SystemSE.photo);
        }

        private IEnumerator Take360Screenshot(bool in3D)
        {
            yield return new WaitForEndOfFrame();

            if (!in3D)
            {
                yield return new WaitForEndOfFrame();

                var output = I360Render.CaptureTex(Resolution360.Value);
                var capture = EncodeToXmpFile(output);

                var filename = GetUniqueFilename("360");
                File.WriteAllBytes(filename, capture);

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"360 screenshot saved to {filename}");

                Destroy(output);
            }
            else
            {
                var targetTr = Camera.main.transform;

                ToggleCameraControllers(targetTr, false);
                Time.timeScale = 0.01f;
                yield return new WaitForEndOfFrame();

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;
                // Let the game render at the new position
                yield return new WaitForEndOfFrame();
                var capture = I360Render.CaptureTex(Resolution360.Value);

                targetTr.position -= targetTr.right * EyeSeparation.Value;
                yield return new WaitForEndOfFrame();
                var capture2 = I360Render.CaptureTex(Resolution360.Value);

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;

                ToggleCameraControllers(targetTr, true);
                Time.timeScale = 1;

                // Overlap is useless for these so don't use
                var result = StitchImages(capture, capture2, 0);

                var filename = GetUniqueFilename("3D-360");
                File.WriteAllBytes(filename, EncodeToXmpFile(result));

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"3D 360 screenshot saved to {filename}");

                Destroy(result);
                Destroy(capture);
                Destroy(capture2);
            }

            Utils.Sound.Play(SystemSE.photo);
        }

        /// <summary>
        /// Need to disable camera controllers because they prevent changes to position
        /// </summary>
        private static void ToggleCameraControllers(Transform targetTr, bool enabled)
        {
#if KK
            foreach (var controllerType in new[] { typeof(Studio.CameraControl), typeof(BaseCameraControl_Ver2), typeof(BaseCameraControl) })
            {
                var cc = targetTr.GetComponent(controllerType);
                if (cc is MonoBehaviour mb)
                    mb.enabled = enabled;
            }

            var actionScene = GameObject.Find("ActionScene/CameraSystem");
            if (actionScene != null) actionScene.GetComponent<CameraSystem>().ShouldUpdate = enabled;
#endif
        }

        private static Texture2D StitchImages(Texture2D capture, Texture2D capture2, float overlapOffset)
        {
            var xAdjust = (int)(capture.width * overlapOffset);
            var result = new Texture2D((capture.width - xAdjust) * 2, capture.height, TextureFormat.ARGB32, false);
            for (int x = 0; x < result.width; x++)
            {
                var first = x < result.width / 2;
                var targetX = first ? x : x - capture.width + xAdjust * 2;
                var targetTex = first ? capture : capture2;
                for (int y = 0; y < result.height; y++)
                {
                    result.SetPixel(x, y, targetTex.GetPixel(targetX, y));
                }
            }
            result.Apply();
            return result;
        }

#region UI
        private readonly int uiWindowHash = GUID.GetHashCode();
        private Rect uiRect = new Rect(20, Screen.height / 2 - 150, 160, 223);
        private bool uiShow = false;
        private string ResolutionXBuffer = "", ResolutionYBuffer = "";

        protected void OnGUI()
        {
            if (uiShow)
                uiRect = GUILayout.Window(uiWindowHash, uiRect, WindowFunction, "Screenshot settings");
        }

        private void WindowFunction(int windowID)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Output resolution (W/H)", new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = new GUIStyleState
                    {
                        textColor = Color.white
                    }
                });

                GUILayout.BeginHorizontal();
                {
                    GUI.SetNextControlName("X");
                    ResolutionXBuffer = GUILayout.TextField(ResolutionXBuffer);

                    GUILayout.Label("x", new GUIStyle
                    {
                        alignment = TextAnchor.LowerCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    }, GUILayout.ExpandWidth(false));

                    GUI.SetNextControlName("Y");
                    ResolutionYBuffer = GUILayout.TextField(ResolutionYBuffer);

                    var focused = GUI.GetNameOfFocusedControl();
                    if (focused != "X" && focused != "Y")
                    {
                        if (!int.TryParse(ResolutionXBuffer, out int x))
                            x = ResolutionX.Value;
                        if (!int.TryParse(ResolutionYBuffer, out int y))
                            y = ResolutionY.Value;
                        ResolutionXBuffer = (ResolutionX.Value = Mathf.Clamp(x, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                        ResolutionYBuffer = (ResolutionY.Value = Mathf.Clamp(y, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);

                    if (GUILayout.Button("Set to screen size"))
                    {
                        ResolutionX.Value = Screen.width;
                        ResolutionY.Value = Screen.height;
                    }

                    if (GUILayout.Button("Rotate 90 degrees"))
                    {
                        var curerntX = ResolutionX.Value;
                        ResolutionX.Value = ResolutionY.Value;
                        ResolutionY.Value = curerntX;
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUI.skin.box);
                {
                    GUILayout.Label("Screen upsampling rate", new GUIStyle
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    });

                    GUILayout.BeginHorizontal();
                    {
                        int downscale = (int)Math.Round(GUILayout.HorizontalSlider(DownscalingRate.Value, 1, 4));

                        GUILayout.Label($"{downscale}x", new GUIStyle
                        {
                            alignment = TextAnchor.UpperRight,
                            normal = new GUIStyleState
                            {
                                textColor = Color.white
                            }
                        }, GUILayout.ExpandWidth(false));
                        DownscalingRate.Value = downscale;
                    }
                    GUILayout.EndHorizontal();

                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUI.skin.box);
                {
                    GUILayout.Label("Card upsampling rate", new GUIStyle
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    });

                    GUILayout.BeginHorizontal();
                    {
                        int carddownscale = (int)Math.Round(GUILayout.HorizontalSlider(CardDownscalingRate.Value, 1, 4));

                        GUILayout.Label($"{carddownscale}x", new GUIStyle
                        {
                            alignment = TextAnchor.UpperRight,
                            normal = new GUIStyleState
                            {
                                textColor = Color.white
                            }
                        }, GUILayout.ExpandWidth(false));
                        CardDownscalingRate.Value = carddownscale;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();

                CaptureAlpha.Value = GUILayout.Toggle(CaptureAlpha.Value, "Transparent background");

                if (GUILayout.Button("Open screenshot dir"))
                    Process.Start(screenshotDir);

                GUILayout.Space(3);
                GUILayout.Label("More in Plugin Settings");

                GUI.DragWindow();
            }
#endregion
        }
    }
}
