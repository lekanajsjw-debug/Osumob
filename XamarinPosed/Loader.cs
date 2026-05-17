using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    /// Supports both Java hooks and C# hooks via embedded Mono
    /// 
    /// C# hooks work by intercepting calls at Mono runtime level
    /// </summary>
    [Register("xamarin/posed/Loader")]
    public class Loader : Java.Lang.Object, IXposedHookLoadPackage, IXposedHookZygoteInit, IXposedHookInitPackageResources
    {
        private const string Tag = "XamarinPosed";
        
        // Thread-safe initialization
        private static volatile bool _initialized = false;
        private static readonly object _lock = new object();
        
        // ============= CONFIGURATION =============
        // Toggle hooks on/off
        public static bool EnableLogging = true;
        public static bool HookAllApps = false;
        
        // Java level hooks (Xposed - works out of box)
        public static bool EnableJavaHooks = true;
        
        // C# level hooks (experimental - requires native libxamarinhooks.so)
        public static bool EnableCSharpHooks = true; // Try to enable by default
        
        // Native library loaded?
        private static bool _nativeLibLoaded = false;
        
        // Tracked packages (thread-safe)
        private static readonly HashSet<string> _hookedPackages = new HashSet<string>();
        
        // ============= C# HOOK REGISTRY =============
        // Register your C# hooks here
        // These will be called from Java hooks via JNI bridge (when available)
        private static readonly Dictionary<string, HookDelegate> _csharpHooks = new Dictionary<string, HookDelegate>();
        
        public delegate void HookDelegate(object[] args);
        
        #region Constructors
        public Loader() : base(IntPtr.Zero, JniHandleOwnership.DoNotTransfer)
        {
            try
            {
                var handle = JniConstructorReferences.CreateInstance("()V", this);
                base.SetHandle(handle, JniHandleOwnership.DoNotTransfer);
                LogInfo("XamarinPosed v4.1 loaded");
                
                // Try to load native library (if present)
                TryLoadNativeLib();
            }
            catch (Exception ex)
            {
                LogError($"Init error: {ex.Message}");
            }
        }

        public Loader(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }
        
        private void TryLoadNativeLib()
        {
            try
            {
                System.LoadLibrary("xamarinhooks");
                _nativeLibLoaded = true;
                LogInfo("Native lib loaded");
                
                // Initialize native Mono runtime
                var result = NativeInit("XamarinPosed");
                if (result == 0)
                    LogInfo("Native Mono initialized");
                else
                    LogError("Native Mono init failed: " + result);
            }
            catch (Exception ex)
            {
                // Native lib not available - C# hooks disabled
                LogInfo("Native lib not found - C# hooks via JNI disabled");
                _nativeLibLoaded = false;
            }
        }
        #endregion
        
        #region IXposedHookLoadPackage
        public void HandleLoadPackage(XC_LoadPackage.LoadPackageParam? param)
        {
            if (param == null) return;
            
            try
            {
                string packageName = param.PackageName;
                LogInfo($"Load: {packageName}");
                
                // Detect app type
                bool shouldHook = HookAllApps || IsTargetApp(param);
                
                if (shouldHook)
                {
                    lock (_lock)
                    {
                        if (_hookedPackages.Contains(packageName))
                            return;
                        _hookedPackages.Add(packageName);
                    }
                    
                    LogInfo($"Hooking: {packageName}");
                    
                    // Java hooks (always available)
                    if (EnableJavaHooks)
                        HookWithJava(param);
                    
                    // C# hooks (needs native lib)
                    if (EnableCSharpHooks && _nativeLibLoaded)
                        HookWithCSharp(param);
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
                LogInfo($"Zygote: {param.ModulePath}");
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
                LogError($"InitPackageResources: {ex.Message}");
            }
        }
        #endregion
        
        #region App Detection
        private bool IsTargetApp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                var nativeDir = param.AppInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(nativeDir) || !Directory.Exists(nativeDir))
                    return false;
                
                // Check for Xamarin/Mono/MAUI libs
                string[] targetLibs = 
                {
                    "libxamarin-app.so",
                    "libmono-native.so", 
                    "libmonodroid.so",
                    "libmonosgen-2.0.so",
                    "libmaui.so",
                    "libmaui-native.so"
                };
                
                foreach (var file in Directory.EnumerateFiles(nativeDir))
                {
                    var libName = Path.GetFileName(file);
                    foreach (var lib in targetLibs)
                    {
                        if (libName.Equals(lib, StringComparison.OrdinalIgnoreCase))
                        {
                            LogInfo($"Target: {libName}");
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
        #endregion
        
        #region Java Hooks (Xposed - works)
        private void HookWithJava(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                LogInfo("Setting up Java hooks...");
                
                // Hook Application lifecycle
                XposedHelpers.FindAndHookMethod(
                    "android.app.Application", 
                    param.ClassLoader,
                    "attach",
                    "android.content.Context",
                    new JavaHooks.ApplicationAttachHook(CallCSharpHooks));
                
                XposedHelpers.FindAndHookMethod(
                    "android.app.Application",
                    param.ClassLoader,
                    "onCreate",
                    new JavaHooks.ApplicationOnCreateHook(CallCSharpHooks));
                
                // Hook Activity lifecycle
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onCreate",
                    "android.os.Bundle",
                    new JavaHooks.ActivityOnCreateHook(CallCSharpHooks));
                
                XposedHelpers.FindAndHookMethod(
                    "android.app.Activity",
                    param.ClassLoader,
                    "onResume",
                    new JavaHooks.ActivityOnResumeHook(CallCSharpHooks));
                
                // Hook Views
                XposedHelpers.FindAndHookMethod(
                    "android.view.View",
                    param.ClassLoader,
                    "setOnClickListener",
                    "android.view.View$OnClickListener",
                    new JavaHooks.ViewSetOnClickListenerHook(CallCSharpHooks));
                
                LogInfo("Java hooks active");
            }
            catch (Exception ex)
            {
                LogError($"Java hooks: {ex.Message}");
            }
        }
        
        // Pass C# hook data to Java hooks
        private void CallCSharpHooks(string hookName, object[] args)
        {
            if (!EnableCSharpHooks || !_nativeLibLoaded) return;
            
            try
            {
                if (_csharpHooks.TryGetValue(hookName, out var del))
                {
                    del.Invoke(args);
                }
            }
            catch (Exception ex)
            {
                LogError($"C# hook '{hookName}': {ex.Message}");
            }
        }
        
        #endregion
        
        #region C# Hooks (needs native lib)
        private void HookWithCSharp(XC_LoadPackage.LoadPackageParam param)
        {
            try
            {
                // Initialize native bridge
                NativeInit(param.PackageName);
                LogInfo("Native bridge initialized");
                
                // Load our hooks assembly
                NativeLoadAssembly("/data/data/" + param.PackageName + "/files/hooks.dll");
                
                LogInfo("C# hooks loaded");
            }
            catch (Exception ex)
            {
                LogError($"C# hooks: {ex.Message}");
            }
        }
        
        // ============= NATIVE METHODS (JNI) =============
        // These call into libxamarinhooks.so
        
        // Initialize native Mono runtime
        private static native int NativeInit(string appName);
        
        // Load a C# assembly
        private static native int NativeLoadAssembly(string assemblyPath);
        
        // Register a C# hook delegate (takes Mono method reference)
        private static native void NativeRegisterHook(string hookName, object hookDelegate);
        
        // Unregister a hook
        private static native void NativeUnregisterHook(string hookName);
        
        // Trigger a registered hook
        private static native void NativeTriggerHook(string hookName, object[] args);
        
        // Hook a specific C# method
        private static native void NativeHookMethod(string className, string methodName, string methodSig, object hookDelegate);
        
        // Cleanup native resources
        private static native void NativeCleanup();
        #endregion
        
        #region Public API - Register C# Hooks
        /// <summary>
        /// Register a C# hook method
        /// </summary>
        public static void RegisterHook(string name, HookDelegate callback)
        {
            _csharpHooks[name] = callback;
            
            // Also register with native bridge if available
            if (_nativeLibLoaded)
            {
                // The callback would need to be converted to a Java object for JNI
                // NativeRegisterHook(name, callback);
            }
            
            LogInfo($"Registered C# hook: {name}");
        }
        
        /// <summary>
        /// Load a custom C# assembly with hooks
        /// </summary>
        public static int LoadHooksAssembly(string assemblyPath)
        {
            if (!_nativeLibLoaded)
            {
                LogError("Native lib not loaded");
                return -1;
            }
            
            try
            {
                return NativeLoadAssembly(assemblyPath);
            }
            catch (Exception ex)
            {
                LogError($"Load assembly: {ex.Message}");
                return -1;
            }
        }
        
        /// <summary>
        /// Hook a specific C# method in target app
        /// </summary>
        public static void HookMethod(string className, string methodName, string methodSig, HookDelegate callback)
        {
            if (!_nativeLibLoaded)
            {
                LogError("Native lib not loaded");
                return;
            }
            
            _csharpHooks[className + "." + methodName] = callback;
            
            LogInfo($"Registered method hook: {className}.{methodName}");
        }
        
        /// <summary>
        /// Unregister a C# hook
        /// </summary>
        public static void UnregisterHook(string name)
        {
            _csharpHooks.Remove(name);
            
            if (_nativeLibLoaded)
            {
                NativeUnregisterHook(name);
            }
            
            LogInfo($"Unregistered C# hook: {name}");
        }
        
        /// <summary>
        /// Get all registered hooks
        /// </summary>
        public static string[] GetRegisteredHooks()
        {
            var hooks = new string[_csharpHooks.Count];
            _csharpHooks.Keys.CopyTo(hooks, 0);
            return hooks;
        }
        
        /// <summary>
        /// Check if native bridge is available
        /// </summary>
        public static bool IsNativeBridgeAvailable() => _nativeLibLoaded;
        #endregion
        
        #region Logging
        private static void LogInfo(string message)
        {
            if (EnableLogging)
                Log.Info(Tag, message);
        }
        
        private static void LogError(string message)
        {
            Log.E(Tag, message);
        }
        #endregion
    }
    
    // ============= JAVA HOOKS =============
    namespace JavaHooks
    {
        // Delegate for calling C# hooks from Java
        public delegate void CSharpHookCallback(string hookName, object[] args);
        
        class ApplicationAttachHook : XC_MethodHook
        {
            private CSharpHookCallback _callback;
            
            public ApplicationAttachHook(CSharpHookCallback callback)
            {
                _callback = callback;
            }
            
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var context = param?.Args?[0];
                    _callback?.Invoke("Application.attach", new[] { context });
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
        
        class ApplicationOnCreateHook : XC_MethodHook
        {
            private CSharpHookCallback _callback;
            
            public ApplicationOnCreateHook(CSharpHookCallback callback)
            {
                _callback = callback;
            }
            
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    _callback?.Invoke("Application.onCreate", null);
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnCreateHook : XC_MethodHook
        {
            private CSharpHookCallback _callback;
            
            public ActivityOnCreateHook(CSharpHookCallback callback)
            {
                _callback = callback;
            }
            
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    var bundle = param?.Args?[0];
                    _callback?.Invoke("Activity.onCreate", new[] { activity, bundle });
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ActivityOnResumeHook : XC_MethodHook
        {
            private CSharpHookCallback _callback;
            
            public ActivityOnResumeHook(CSharpHookCallback callback)
            {
                _callback = callback;
            }
            
            protected override void AfterHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var activity = param?.ThisObject;
                    _callback?.Invoke("Activity.onResume", new[] { activity });
                }
                catch { }
                base.AfterHookedMethod(param);
            }
        }
        
        class ViewSetOnClickListenerHook : XC_MethodHook
        {
            private CSharpHookCallback _callback;
            
            public ViewSetOnClickListenerHook(CSharpHookCallback callback)
            {
                _callback = callback;
            }
            
            protected override void BeforeHookedMethod(MethodHookParam? param)
            {
                try
                {
                    var view = param?.ThisObject;
                    var listener = param?.Args?[0];
                    _callback?.Invoke("View.setOnClickListener", new[] { view, listener });
                }
                catch { }
                base.BeforeHookedMethod(param);
            }
        }
    }
}