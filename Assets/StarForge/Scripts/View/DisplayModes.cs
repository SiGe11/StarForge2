// DisplayModes.cs — switching between full screen and a window without
// wrecking the aspect ratio.
//
// Setting Screen.fullScreenMode on its own keeps whatever resolution was last
// in force. Leaving full screen asked for a window the size of the display,
// which macOS shrank to fit under the menu bar (2816x1526 on the Neo's
// 2816x1762 panel); going back to full screen then stretched that buffer over
// the whole display, and Unity saved the stretched size, so the next launch
// started stretched too. So a switch always names the resolution as well: the
// display's own for full screen, and for a window the last window size (or 80%
// of the display, same shape). The guard also checks at launch, and after
// switches made another way (the window's green button, the system shortcut),
// that full screen has the display's shape, and puts it right if not.
//
// -sfscreentest runs the switch a few times, logs what the player reports and quits.
using System.Collections;
using UnityEngine;

namespace StarForge.View
{
    public sealed class DisplayModes : MonoBehaviour
    {
        static int windowW, windowH;
        FullScreenMode lastMode;
        int settleFrames;

        public static bool IsFullScreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        public static void Toggle() => Set(!IsFullScreen);

        public static void Set(bool fullScreen)
        {
            var d = Screen.mainWindowDisplayInfo;
            int dw = d.width > 0 ? d.width : Screen.currentResolution.width;
            int dh = d.height > 0 ? d.height : Screen.currentResolution.height;
            if (fullScreen)
            {
                if (!IsFullScreen) { windowW = Screen.width; windowH = Screen.height; }
                Screen.SetResolution(dw, dh, FullScreenMode.FullScreenWindow);
            }
            else
            {
                int w = windowW, h = windowH;
                // A remembered window that does not fit, or none: 80% of the display.
                if (w < 640 || h < 400 || w > dw || h > dh)
                {
                    w = Mathf.RoundToInt(dw * 0.8f);
                    h = Mathf.RoundToInt(dh * 0.8f);
                }
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
        }

        void Awake()
        {
            lastMode = Screen.fullScreenMode;
            settleFrames = 5;   // a stretched size saved by an older build is corrected at launch
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-sfscreentest") StartCoroutine(SelfTest());
        }

        void Update()
        {
            if (Application.isEditor) return;
            if (Screen.fullScreenMode != lastMode)
            {
                lastMode = Screen.fullScreenMode;
                settleFrames = 10;
            }
            // Full screen must fill the display at its own shape. Checked a few
            // frames after a switch, once the window has finished animating.
            if (settleFrames > 0 && --settleFrames == 0 && IsFullScreen)
            {
                var d = Screen.mainWindowDisplayInfo;
                if (d.width > 0 && d.height > 0 &&
                    Mathf.Abs((float)Screen.width / Screen.height - (float)d.width / d.height) > 0.01f)
                    Screen.SetResolution(d.width, d.height, Screen.fullScreenMode);
            }
        }

        static void Log(string when)
        {
            var d = Screen.mainWindowDisplayInfo;
            Debug.Log($"[StarForge] screen {when}: mode {Screen.fullScreenMode}, {Screen.width}x{Screen.height} " +
                      $"(aspect {(float)Screen.width / Mathf.Max(1, Screen.height):0.000}), display {d.width}x{d.height} " +
                      $"(aspect {(float)d.width / Mathf.Max(1, d.height):0.000}), current {Screen.currentResolution.width}x{Screen.currentResolution.height}, " +
                      $"work area {d.workArea}, rendering {Display.main.renderingWidth}x{Display.main.renderingHeight}");
        }

        IEnumerator SelfTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            Log("start");
            for (int i = 0; i < 4; i++)
            {
                Toggle();
                yield return new WaitForSecondsRealtime(3f);
                Log($"after toggle {i + 1}");
            }
            Application.Quit();
        }
    }
}
