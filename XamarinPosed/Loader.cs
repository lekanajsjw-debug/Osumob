using System;
using System.IO;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using DE.Robv.Android.Xposed;
using DE.Robv.Android.Xposed.Callbacks;

namespace XamarinPosed
{
    /// <summary>
    /// Main Xposed module entry point for Xamarin apps
    /// </summary>
    [Register("xamarin/posed/Loader")]
    public class Loader : Java.Lang.Object, IXposedHookLoadPackage, IXposedHookZygoteInit, IXposedHookInitPackageResources
    {
        private const string Tag = "XamarinPosed";
        private const string ModuleBaseDir = "/data/data/de.robv.hook.xposed.injec";
        
        private static bool _initialized = false;
        private static readonly object _lock = new object();
        
        // Configuration
        public static bool EnableLogging = true;
        public static bool HookAllXamarinApps = true;
        
        // Detected apps tracker
        private static readonly Java.Util.HashSet _hookedPackages = new Java.Util.HashSet();
        
        #region Constructor
        public Loader() : base(IntPtr.Zero, JniHandleOwnership.DoNotTransfer)
        {
            var handle = JniConstructorReferences.CreateInstance("()V", this);
            base.SetHandle(handle, JniHandleOwnership.DoNotTransfer);
            LogInfo("Loader initialized");
        }

        public Loader(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }
        #endregion
        
        #region IXposedHookLoadPackage
        public void HandleLoadPackage(XC_LoadPackage.LoadPackageParam? param)
        {
            if (param == null) return;
            
            try
            {
                LogInfo($"HandleLoadPackage: {param.PackageName}");
                
                // Check if this is a Xamarin Android app
                bool isXamarinApp = DetectXamarinApp(param);
                
                if (isXamarinApp || HookAllXamarinApps)
                {
                    lock (_lock)
                    {
                        if (_hookedPackages.Contains(param.PackageName))
                        {
                            LogInfo($"Package {param.PackageName} already hooked");
                            return;
                        }
                        _hookedPackages.Add(param.PackageName);
                    }
                    
                    HookXamarinApp(param);
                }
            }
            catch (Exception ex)
            {
                LogError($"HandleLoadPackage error: {ex}");
            }
        }
        #endregion
        
        #region IXposedHookZygoteInit
        public void InitZygote(IXposedHookZygoteInit.StartupParam? param)
        {
            if (param == null) return;
            
            try
            {
                LogInfo($"InitZygote: {param.ModulePath}");
                _initialized = true;
            }
            catch (Exception ex)
            {
                LogError($"InitZygote error: {ex}");
            }
        }
        #endregion
        
        #region IXposedHookInitPackageResources  
        public void HandleInitPackageResources(XC_InitPackageResources.InitPackageResourcesParam? param)
        {
            if (param == null) return;
            
            try
            {
                LogInfo($"HandleInitPackageResources: {param.PackageName}");
            }
            catch (Exception ex)
            {
                LogError($"HandleInitPackageResources error: {ex}");
            }
        }
        #endregion
        
        #region Xamarin Detection
        /// <summary>
        /// Detect if the app is a Xamarin Android app by checking native libraries
        /// </summary>
        private bool DetectXamarinApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                var nativeDir = param.AppInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(nativeDir))
                {
                    LogInfo("Native dir is null");
                    return false;
                }
                
                if (!Directory.Exists(nativeDir))
                {
                    return false;
                }
                
                string[] xamarinLibs = new string[]
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
                    foreach (var xamarinLib in xamarinLibs)
                    {
                        if (libName.Equals(xamarinLib, StringComparison.OrdinalIgnoreCase))
                        {
                            LogInfo($"Detected Xamarin app: {param.PackageName} via {libName}");
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                LogError($"DetectXamarinApp error: {ex}");
                return false;
            }
        }
        #endregion
        
        #region Hook Methods
        /// <summary>
        /// Hook into a Xamarin Android app to intercept method calls
        /// </summary>
        private void HookXamarinApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                LogInfo($"Hooking Xamarin app: {param.PackageName}");
                LogInfo($"Process: {param.ProcessName}, AppInfo: {param.AppInfo}");
                
                // Hook Application class if available
                HookApplicationClass(param);
                
                // Hook Context class for additional intercept capabilities
                HookContextClass(param);
                
                // Hook common Activity methods
                HookActivityMethods(param);
                
                // Hook common View click listeners
                HookViewListeners(param);
                
                LogInfo($"Successfully hooked: {param.PackageName}");
            }
            catch (Exception ex)
            {
                LogError($"HookXamarinApp error: {ex}");
            }
        }
        
        /// <summary>
        /// Hook Application class
        /// </summary>
        private void HookApplicationClass(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Find and hook Application.attach()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Application", 
                    param.ClassLoader,
                    "attach",
                    "android.content.Context",
                    new ApplicationAttachHook());
                    
                LogInfo("Application.attach hooked");
            }
            catch (Exception ex)
            {
                LogError($"HookApplicationClass error: {ex}");
            }
        }
        
        /// <summary>
        /// Hook Context class
        /// </summary>
        private void HookContextClass(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Find and hook Context.getClassLoader()
                XposedHelpers.FindAndHookMethod(
                    "android.content.Context",
                    param.ClassLoader,
                    "getClassLoader",
                    new ContextGetClassLoaderHook());
                    
                LogInfo("Context.getClassLoader hooked");
            }
            catch (Exception ex)
            {
                LogError($"HookContextClass error: {ex}");
            }
        }
        
        /// <summary>
        /// Hook common Activity methods
        /// </summary>
        private void HookActivityMethods(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Hook Activity.onCreate()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onCreate",
                    "android.os.Bundle",
                    new ActivityOnCreateHook());
                    
                // Hook Activity.onResume()
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onResume",
                    new ActivityOnResumeHook());
                    
                LogInfo("Activity methods hooked");
            }
            catch (Exception ex)
            {
                LogError($"HookActivityMethods error: {ex}");
            }
        }
        
        /// <summary>
        /// Hook View click listeners
        /// </summary>
        private void HookViewListeners(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Hook View.setOnClickListener()
                XposedHelpers.FindAndHookMethod(
                    "android.view.View",
                    param.ClassLoader,
                    "setOnClickListener",
                    "android.view.View$OnClickListener",
                    new ViewSetOnClickListenerHook());
                    
                LogInfo("View listeners hooked");
            }
            catch (Exception ex)
            {
                LogError($"HookViewListeners error: {ex}");
            }
        }
        #endregion
        
        #region Hook Classes
        /// <summary>
        /// Hook for Application.attach() - intercept app initialization
        /// </summary>
        class ApplicationAttachHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var context = param?.Args?[0];
                    LogInfo($"Application.attach called with context: {context?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ApplicationAttachHook error: {ex}");
                }
                
                base.BeforeHookedMethod(param);
            }
            
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var context = param?.ThisObject;
                    LogInfo($"Application.attach completed, context: {context?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ApplicationAttachHook after error: {ex}");
                }
                
                base.AfterHookedMethod(param);
            }
        }
        
        /// <summary>
        /// Hook for Context.getClassLoader() - intercept class loading
        /// </summary>
        class ContextGetClassLoaderHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var classLoader = param?.Result;
                    LogInfo($"ClassLoader: {classLoader?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ContextGetClassLoaderHook error: {ex}");
                }
                
                base.AfterHookedMethod(param);
            }
        }
        
        /// <summary>
        /// Hook for Activity.onCreate()
        /// </summary>
        class ActivityOnCreateHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    var bundle = param?.Args?[0];
                    LogInfo($"Activity.onCreate: {activity?.GetType()?.Name}, bundle: {bundle?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ActivityOnCreateHook error: {ex}");
                }
                
                base.AfterHookedMethod(param);
            }
        }
        
        /// <summary>
        /// Hook for Activity.onResume()
        /// </summary>
        class ActivityOnResumeHook : XC_MethodHook
        {
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    LogInfo($"Activity.onResume: {activity?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ActivityOnResumeHook error: {ex}");
                }
                
                base.AfterHookedMethod(param);
            }
        }
        
        /// <summary>
        /// Hook for View.setOnClickListener()
        /// </summary>
        class ViewSetOnClickListenerHook : XC_MethodHook
        {
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var view = param?.ThisObject;
                    var listener = param?.Args?[0];
                    LogInfo($"View.setOnClickListener: view={view?.GetType()?.Name}, listener={listener?.GetType()?.Name}");
                }
                catch (Exception ex)
                {
                    LogError($"ViewSetOnClickListenerHook error: {ex}");
                }
                
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
            Log.E(Tag, message);
        }
        #endregion
    }
}