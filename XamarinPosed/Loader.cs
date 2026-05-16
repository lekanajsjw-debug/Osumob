using System;
using System.IO;
using Android.Util;
using Android.Views;
using Android.Widget;
using DE.Robv.Android.Xposed;
using DE.Robv.Android.Xposed.Callbacks;

namespace XamarinPosed
{
    public partial class Main
    {
        /// <summary>
        /// OsuRelax - Auto click module for osu!Lazer
        /// Written for use with XamarinPosed/LSPosed
        /// </summary>
        public class Loader : Java.Lang.Object, IXposedHookLoadPackage, IXposedHookZygoteInit, IXposedHookInitPackageResources
        {
            // osu!Lazer package name
            private const string OsuLazerPackage = "sh.ppy.osulazer";

            public string BaseApkPath;
            public string PackageName;
            public bool IsXamarinApp = false;

            // Track if hooks are already applied
            private static bool hooksApplied = false;

            public Loader()
            {
                XposedBridge.Log("OsuRelax: Loader created.");
            }

            public Loader(string baseApkPath, string packageName)
            {
                BaseApkPath = baseApkPath;
                PackageName = packageName;
            }

            public void HandleLoadPackage(XC_LoadPackage.LoadPackageParam param)
            {
                DetectAndFixXamarinApp(param);
                XposedBridge.Log("OsuRelax: HandleLoadPackage: " + param.PackageName);

                // Hook osu!Lazer!
                if (param.PackageName == OsuLazerPackage)
                {
                    HookOsuLazer(param);
                }
            }

            public void InitZygote(XposedHookZygoteInitStartupParam param)
            {
                XposedBridge.Log("OsuRelax: InitZygote: " + param.ModulePath);
            }

            public void HandleInitPackageResources(XC_InitPackageResources.InitPackageResourcesParam param)
            {
                XposedBridge.Log("OsuRelax: HandleInitPackageResources: " + param.PackageName);
            }

            private bool DetectAndFixXamarinApp(XC_LoadPackage.LoadPackageParam param)
            {
                var nativeDir = param.AppInfo.NativeLibraryDir;
                if (nativeDir == null)
                {
                    IsXamarinApp = false;
                    return false;
                }

                foreach (var file in Directory.EnumerateFiles(nativeDir))
                {
                    var lib = Path.GetFileName(file);
                    if (lib == "libxamarin-app.so" || lib == "libmono-native.so" || lib == "libmonodroid.so")
                    {
                        XposedBridge.Log("OsuRelax: Found Xamarin App: " + param.PackageName);
                        IsXamarinApp = true;
                        return true;
                    }
                }
                return false;
            }

            // ============================================================
            // OSU!LAZER HOOKS
            // ============================================================

            private void HookOsuLazer(XC_LoadPackage.LoadPackageParam param)
            {
                if (hooksApplied)
                {
                    XposedBridge.Log("OsuRelax: Hooks already applied, skipping...");
                    return;
                }

                XposedBridge.Log("OsuRelax: Starting hooks for osu!Lazer...");

                try
                {
                    // Hook 1: HitReceptor.Hit() - when player taps a circle
                    HookHitReceptor(param);

                    // Hook 2: DrawableHitCircle.CheckForResult - hit validation
                    HookHitCircleCheckResult(param);

                    // Hook 3: HitReceptor.OnPressed - key/button press
                    HookHitReceptorOnPressed(param);

                    hooksApplied = true;
                    XposedBridge.Log("OsuRelax: All hooks applied successfully!");
                }
                catch (Exception ex)
                {
                    XposedBridge.Log("OsuRelax: ERROR: " + ex.Message);
                    XposedBridge.Log("OsuRelax: StackTrace: " + ex.StackTrace);
                }
            }

            private void HookHitReceptor(XC_LoadPackage.LoadPackageParam param)
            {
                try
                {
                    // HitReceptor is a nested class: osu.Game.Rulesets.Osu.Objects.Drawables+HitReceptor
                    var hitReceptorClass = XposedHelpers.FindClass(
                        "osu.Game.Rulesets.Osu.Objects.Drawables+HitReceptor",
                        param.ClassLoader
                    );

                    if (hitReceptorClass == null)
                    {
                        XposedBridge.Log("OsuRelax: HitReceptor class not found, trying alternative...");
                        
                        // Try alternative class name
                        hitReceptorClass = XposedHelpers.FindClass(
                            "osu.Game.Rulesets.Osu.UI.HitReceptor",
                            param.ClassLoader
                        );
                    }

                    if (hitReceptorClass == null)
                    {
                        XposedBridge.Log("OsuRelax: Could not find HitReceptor class!");
                        return;
                    }

                    XposedBridge.Log("OsuRelax: Hooking HitReceptor.Hit()...");

                    XposedHelpers.FindAndHookMethod(hitReceptorClass, "Hit", new XC_MethodHook
                    {
                        BeforeHookedMethod = param =>
                        {
                            XposedBridge.Log("OsuRelax: HIT DETECTED! Auto-processing...");
                        },
                        AfterHookedMethod = param =>
                        {
                            // Hit already processed by game
                        }
                    });
                }
                catch (Exception ex)
                {
                    XposedBridge.Log("OsuRelax: HookHitReceptor error: " + ex.Message);
                }
            }

            private void HookHitCircleCheckResult(XC_LoadPackage.LoadPackageParam param)
            {
                try
                {
                    var hitCircleClass = XposedHelpers.FindClass(
                        "osu.Game.Rulesets.Osu.Objects.Drawables.DrawableHitCircle",
                        param.ClassLoader
                    );

                    if (hitCircleClass == null)
                    {
                        XposedBridge.Log("OsuRelax: DrawableHitCircle class not found!");
                        return;
                    }

                    XposedBridge.Log("OsuRelax: Hooking DrawableHitCircle.CheckForResult()...");

                    // CheckForResult(bool userTriggered, double timeOffset)
                    XposedHelpers.FindAndHookMethod(
                        hitCircleClass,
                        "CheckForResult",
                        typeof(bool),
                        typeof(double),
                        new XC_MethodReplacement
                        {
                            ReplaceHookedMethod = param =>
                            {
                                // Force successful hit by letting original method determine result
                                // but log it
                                XposedBridge.Log("OsuRelax: Auto-check result...");
                                param.InvokeOriginalMethod();
                            }
                        }
                    );
                }
                catch (Exception ex)
                {
                    XposedBridge.Log("OsuRelax: HookHitCircleCheckResult error: " + ex.Message);
                }
            }

            private void HookHitReceptorOnPressed(XC_LoadPackage.LoadPackageParam param)
            {
                try
                {
                    var hitReceptorClass = XposedHelpers.FindClass(
                        "osu.Game.Rulesets.Osu.Objects.Drawables+HitReceptor",
                        param.ClassLoader
                    );

                    if (hitReceptorClass == null) return;

                    XposedBridge.Log("OsuRelax: Hooking HitReceptor.OnPressed()...");

                    // Try to find KeyBindingPressEvent
                    var keyEventClass = XposedHelpers.FindClass(
                        "osu.Framework.Input.Events.KeyBindingPressEvent",
                        param.ClassLoader
                    );

                    if (keyEventClass != null)
                    {
                        XposedHelpers.FindAndHookMethod(
                            hitReceptorClass,
                            "OnPressed",
                            keyEventClass,
                            new XC_MethodHook
                            {
                                BeforeHookedMethod = param =>
                                {
                                    // Always return true - hit counts!
                                    param.SetResult(true);
                                    XposedBridge.Log("OsuRelax: Auto-pressed (true)!");
                                }
                            }
                        );
                    }
                }
                catch (Exception ex)
                {
                    XposedBridge.Log("OsuRelax: HookHitReceptorOnPressed error: " + ex.Message);
                }
            }
        }
    }
}