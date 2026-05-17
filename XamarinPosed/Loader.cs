using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using DE.Robv.Android.Xposed;
using DE.Robv.Android.Xposed.Callbacks;

namespace XamarinPosed
{
    /// <summary>
    /// Main Xposed module entry point for Xamarin Android apps
    /// Supports both Xamarin.Android and .NET MAUI apps
    /// </summary>
    [Register("xamarin/posed/Loader")]
    public class Loader : Java.Lang.Object, IXposedHookLoadPackage, IXposedHookZygoteInit, IXposedHookInitPackageResources
    {
        private const string Tag = "XamarinPosed";
        
        // Thread-safe initialization
        private static volatile bool _initialized = false;
        private static readonly object _lock = new object();
        
        // Configuration (can be changed via Xposed TP)
        public static bool EnableLogging = true;
        public static bool HookAllApps = false;
        public static bool EnableActivityHooks = true;
        public static bool EnableViewHooks = true;
        public static bool EnableContextHooks = true;
        public static bool EnableClassLoaderHooks = true;
        
        // Tracked packages (thread-safe)
        private static readonly HashSet<string> _hookedPackages = new HashSet<string>();
        
        #region Constructors
        public Loader() : base(IntPtr.Zero, JniHandleOwnership.DoNotTransfer)
        {
            try
            {
                var handle = JniConstructorReferences.CreateInstance("()V", this);
                base.SetHandle(handle, JniHandleOwnership.DoNotTransfer);
                LogInfo("Loader v4.0 initialized");
            }
            catch (Exception ex)
            {
                LogError($"Constructor error: {ex.Message}");
            }
        }

        public Loader(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }
        #endregion
        
        #region IXposedHookLoadPackage
        public void HandleLoadPackage(XC_LoadPackage.LoadPackageParam? param)
        {
            if (param == null) return;
            
            try
            {
                string packageName = param.PackageName;
                LogInfo($"Loading: {packageName}");
                
                bool shouldHook = HookAllApps || IsXamarinApp(param) || IsMauiApp(param) || IsNetAndroidApp(param);
                
                if (shouldHook)
                {
                    lock (_lock)
                    {
                        if (_hookedPackages.Contains(packageName))
                        {
                            return;
                        }
                        _hookedPackages.Add(packageName);
                    }
                    
                    LogInfo($"Hooking: {packageName}");
                    HookXamarinApp(param);
                }
            }
            catch (Exception ex)
            {
                LogError($"HandleLoadPackage: {ex.Message}");
            }
        }
        #endregion
        
        #region IXposedHookZygoteInit
        public void InitZygote(IXposedHookZygoteInit.StartupParam? param)
        {
            if (param == null) return;
            
            try
            {
                _initialized = true;
                LogInfo($"Zygote ready: {param.ModulePath}");
            }
            catch (Exception ex)
            {
                LogError($"InitZygote: {ex.Message}");
            }
        }
        #endregion
        
        #region IXposedHookInitPackageResources  
        public void HandleInitPackageResources(XC_InitPackageResources.InitPackageResourcesParam? param)
        {
            if (param == null) return;
            
            try
            {
                LogInfo($"Resources: {param.PackageName}");
            }
            catch (Exception ex)
            {
                LogError($"HandleInitPackageResources: {ex.Message}");
            }
        }
        #endregion
        
        #region App Detection
        /// <summary>
        /// Detect Xamarin.Android apps by native libraries
        /// </summary>
        private bool IsXamarinApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                var nativeDir = param.AppInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(nativeDir) || !Directory.Exists(nativeDir))
                    return false;
                
                string[] xamarinLibs = 
                {
                    "libxamarin-app.so",
                    "libmono-native.so", 
                    "libmonodroid.so",
                    "libmonosgen-2.0.so",
                    "libxamarin-debug-app-helper.so"
                };
                
                foreach (var file in Directory.EnumerateFiles(nativeDir))
                {
                    var libName = Path.GetFileName(file);
                    foreach (var lib in xamarinLibs)
                    {
                        if (libName.Equals(lib, StringComparison.OrdinalIgnoreCase))
                        {
                            LogInfo($"Xamarin.Android detected: {libName}");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Detect .NET MAUI apps
        /// </summary>
        private bool IsMauiApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                var nativeDir = param.AppInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(nativeDir) || !Directory.Exists(nativeDir))
                    return false;
                
                string[] mauiLibs = 
                {
                    "libmaui.so",
                    "libmaui-native.so"
                };
                
                foreach (var file in Directory.EnumerateFiles(nativeDir))
                {
                    var libName = Path.GetFileName(file);
                    foreach (var lib in mauiLibs)
                    {
                        if (libName.Equals(lib, StringComparison.OrdinalIgnoreCase))
                        {
                            LogInfo($".NET MAUI detected: {libName}");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Detect NET Android apps (older Xamarin)
        /// </summary>
        private bool IsNetAndroidApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                var apkPath = param.AppInfo?.SourceDir;
                if (string.IsNullOrEmpty(apkPath))
                    return false;
                
                // Check for specific class names in APK
                if (apkPath.Contains("netandroid") || apkPath.Contains("xamarin"))
                {
                    LogInfo($"NET Android app: {param.PackageName}");
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        #endregion
        
        #region Hook Methods
        /// <summary>
        /// Main hook orchestration
        /// </summary>
        private void HookXamarinApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                LogInfo($"Hooking {param.PackageName}");
                
                // Hook Application
                HookApplicationClass(param);
                
                // Hook Context
                if (EnableContextHooks)
                    HookContextClass(param);
                
                // Hook ClassLoader
                if (EnableClassLoaderHooks)
                    HookClassLoader(param);
                
                // Hook Activity lifecycle
                if (EnableActivityHooks)
                    HookActivityMethods(param);
                
                // Hook View interactions
                if (EnableViewHooks)
                    HookViewListeners(param);
                
                // Hook Content providers
                HookContentProviders(param);
                
                LogInfo($"Hooks enabled for: {param.PackageName}");
            }
            catch (Exception ex)
            {
                LogError($"HookXamarinApp: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook Application lifecycle
        /// </summary>
        private void HookApplicationClass(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Application.attach()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Application", 
                    param.ClassLoader,
                    "attach",
                    "android.content.Context",
                    new ApplicationAttachHook());
                    
                // Application.onCreate()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Application",
                    param.ClassLoader,
                    "onCreate",
                    new ApplicationOnCreateHook());
                    
                LogInfo("Application hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookApplicationClass: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook Context methods
        /// </summary>
        private void HookContextClass(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Context.getClassLoader()
                XposedHelpers.FindAndHookMethod(
                    "android.content.Context",
                    param.ClassLoader,
                    "getClassLoader",
                    new ContextGetClassLoaderHook());
                    
                // Context.getPackageName()
                XposedHelpers.FindAndHookMethod(
                    "android.content.Context",
                    param.ClassLoader,
                    "getPackageName",
                    new ContextGetPackageNameHook());
                    
                LogInfo("Context hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookContextClass: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook ClassLoader
        /// </summary>
        private void HookClassLoader(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // BaseDexClassLoader
                XposedHelpers.FindAndHookConstructor(
                    "dalvik.system.BaseDexClassLoader",
                    param.ClassLoader,
                    new Java.Lang.String(),
                    new Java.IO.File(),
                    new Java.Lang.ClassLoader(),
                    new DefClassDexClassLoaderHook());
                    
                LogInfo("ClassLoader hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookClassLoader: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook Activity lifecycle
        /// </summary>
        private void HookActivityMethods(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Activity.onCreate()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onCreate",
                    "android.os.Bundle",
                    new ActivityOnCreateHook());
                    
                // Activity.onStart()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onStart",
                    new ActivityOnStartHook());
                    
                // Activity.onResume()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onResume",
                    new ActivityOnResumeHook());
                    
                // Activity.onPause()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onPause",
                    new ActivityOnPauseHook());
                    
                // Activity.onStop()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onStop",
                    new ActivityOnStopHook());
                    
                // Activity.onDestroy()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onDestroy",
                    new ActivityOnDestroyHook());
                    
                LogInfo("Activity hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookActivityMethods: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook View interactions
        /// </summary>
        private void HookViewListeners(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // View.setOnClickListener()
                XposedHelpers.FindAndHookMethod(
                    "android.view.View",
                    param.ClassLoader,
                    "setOnClickListener",
                    "android.view.View$OnClickListener",
                    new ViewSetOnClickListenerHook());
                    
                // View.setOnLongClickListener()
                XposedHelpers.FindAndHookMethod(
                    "android.view.View",
                    param.ClassLoader,
                    "setOnLongClickListener",
                    "android.view.View$OnLongClickListener",
                    new ViewSetOnLongClickListenerHook());
                    
                // View.performClick()
                XposedHelpers.FindAndHookMethod(
                    "android.view.View",
                    param.ClassLoader,
                    "performClick",
                    new ViewPerformClickHook());
                    
                LogInfo("View hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookViewListeners: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hook Content Providers
        /// </summary>
        private void HookContentProviders(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // ContentProvider.attachInfo()
                XposedHelpers.FindAndHookMethod(
                    "android.content.ContentProvider",
                    param.ClassLoader,
                    "attachInfo",
                    "android.content.Context",
                    "android.content.pm.ProviderInfo",
                    new ContentProviderAttachInfoHook());
                    
                LogInfo("ContentProvider hooks OK");
            }
            catch (Exception ex)
            {
                LogError($"HookContentProviders: {ex.Message}");
            }
        }
        #endregion
        
        #region Hook Classes - Application
        class ApplicationAttachHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var context = param?.Args?[0];
                    LogInfo($"App.attach: {context?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        
        class ApplicationOnCreateHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var app = param?.ThisObject;
                    LogInfo($"App.onCreate: {app?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        #endregion
        
        #region Hook Classes - Context
        class ContextGetClassLoaderHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var cl = param?.Result;
                    LogInfo($"Context.getClassLoader: {cl?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ContextGetPackageNameHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var pkgName = param?.Result;
                    LogInfo($"Context.getPackageName: {pkgName}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        #endregion
        
        #region Hook Classes - ClassLoader
        class DefClassDexClassLoaderHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    LogInfo($"DexClassLoader created");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        #endregion
        
        #region Hook Classes - Activity
        class ActivityOnCreateHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onCreate: {activity?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnStartHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onStart: {activity?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnResumeHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onResume: {activity?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnPauseHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onPause: {activity?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        
        class ActivityOnStopHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onStop: {activity?.GetType()?.Name}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnDestroyHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onDestroy: {activity?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        #endregion
        
        #region Hook Classes - View
        class ViewSetOnClickListenerHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var view = param?.ThisObject;
                    var listener = param?.Args?[0];
                    LogInfo($"View.setOnClick: {view?.GetType()?.Name} <- {listener?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        
        class ViewSetOnLongClickListenerHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var view = param?.ThisObject;
                    var listener = param?.Args?[0];
                    LogInfo($"View.setOnLongClick: {view?.GetType()?.Name} <- {listener?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        
        class ViewPerformClickHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var view = param?.ThisObject;
                    var result = param?.Result;
                    LogInfo($"View.performClick: {view?.GetType()?.Name} = {result}");
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        #endregion
        
        #region Hook Classes - ContentProvider
        class ContentProviderAttachInfoHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var context = param?.Args?[0];
                    var info = param?.Args?[1];
                    LogInfo($"ContentProvider.attachInfo: {context?.GetType()?.Name}, {info?.GetType()?.Name}");
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        #endregion
        
        #region Logging
        private static void LogInfo(string message)
        {
            if (EnableLogging)
            {
                Log.Info(Tag, message);
            }
        }
        
        private static void LogError(string message)
        {
            Log.E(tag, message);
        }
        #endregion
    }
}