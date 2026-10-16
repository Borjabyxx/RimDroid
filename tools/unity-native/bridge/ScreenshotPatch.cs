using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RimDroid
{
    // On Android, Unity's ScreenCapture.CaptureScreenshot appends the file name to
    // Application.persistentDataPath even when it is a full path: RimWorld's F10 (ScreenshotTaker) asked
    // for .../instances/<name>/.../Screenshots/screenshotN.png, Unity tried
    // /storage/emulated/0/Android/data/com.rimdroid/files/data/user/0/..., logged "Failed to store
    // screen shot" and nothing was saved, while the game said it was (player report, 2026-10-15). As a
    // Harmony prefix on its managed overloads: a full path becomes the same file relative to
    // persistentDataPath, "../" up to the root, so the path Unity builds leads back to the file asked for.
    static class ScreenshotPatch
    {
        public static void BeforeCaptureScreenshot(ref string __0)
        {
            try
            {
                if (string.IsNullOrEmpty(__0) || !Path.IsPathRooted(__0)) return;
                string[] parts = Application.persistentDataPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var up = new StringBuilder();
                for (int i = 0; i < parts.Length; i++) up.Append("../");
                __0 = up + __0.TrimStart('/');
            }
            catch (Exception e)
            {
                Debug.LogWarning("RIMDROID bridge: screenshot path left as it is: " + e.GetBaseException().Message);
            }
        }
    }
}
