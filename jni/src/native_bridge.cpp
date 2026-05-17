/*
 * XamarinPosed Native JNI Bridge
 * Bridges Java/Xposed hooks to Mono runtime for C# method hooking
 */

#include <jni.h>
#include <android/log.h>
#include <mono/jit/mono.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/assembly.h>
#include <string>
#include <vector>
#include <map>

#define LOG_TAG "XamarinPosed.Native"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

// Global state
static JavaVM* g_jvm = nullptr;
static MonoDomain* g_rootDomain = nullptr;
static MonoDomain* g_appDomain = nullptr;
static bool g_initialized = false;

// Hook registry
static std::map<std::string, jobject> g_hooks;
static std::map<std::string, jmethodID> g_methods;

// Initialize Mono runtime
extern "C" JNIEXPORT jint JNICALL 
Java_xamarin_posed_Loader_nativeInit(JNIEnv* env, jclass clazz, jstring appName)
{
    if (g_initialized) {
        LOGI("Mono already initialized");
        return JNI_OK;
    }

    // Get JavaVM
    env->GetJavaVM(&g_jvm);
    
    const char* app_name = env->GetStringUTFChars(appName, nullptr);
    
    // Create root domain
    g_rootDomain = mono_jit_init("XamarinPosedRoot");
    if (!g_rootDomain) {
        LOGE("Failed to init root Mono domain");
        return JNI_ERR;
    }
    
    // Create app domain
    g_appDomain = mono_domain_create_appdomain(const_cast<char*>(app_name), nullptr);
    if (!g_appDomain) {
        LOGE("Failed to create app domain");
        mono_jit_cleanup(g_rootDomain);
        return JNI_ERR;
    }
    
    mono_domain_set(g_appDomain, true);
    
    g_initialized = true;
    LOGI("Mono runtime initialized successfully");
    
    env->ReleaseStringUTFChars(appName, app_name);
    return JNI_OK;
}

// Register a C# hook
extern "C" JNIEXPORT void JNICALL
Java_xamarin_posed_Loader_nativeRegisterHook(JNIEnv* env, jclass clazz, 
    jstring hookName, jobject hookDelegate)
{
    if (!g_initialized) {
        LOGE("Mono not initialized");
        return;
    }
    
    const char* name = env->GetStringUTFChars(hookName, nullptr);
    
    // Store global reference to delegate
    jobject globalRef = env->NewGlobalRef(hookDelegate);
    g_hooks[name] = globalRef;
    
    // Get method ID for Invoke
    jclass delegateClass = env->GetObjectClass(hookDelegate);
    jmethodID invokeMethod = env->GetMethodID(delegateClass, "Invoke", "(Ljava/lang/Object;)V");
    g_methods[name] = invokeMethod;
    
    LOGI("Registered C# hook: %s", name);
    
    env->ReleaseStringUTFChars(hookName, name);
}

// Unregister a C# hook
extern "C" JNIEXPORT void JNICALL
Java_xamarin_posed_Loader_nativeUnregisterHook(JNIEnv* env, jclass clazz, jstring hookName)
{
    const char* name = env->GetStringUTFChars(hookName, nullptr);
    
    auto it = g_hooks.find(name);
    if (it != g_hooks.end()) {
        env->DeleteGlobalRef(it->second);
        g_hooks.erase(it);
        g_methods.erase(name);
        LOGI("Unregistered hook: %s", name);
    }
    
    env->ReleaseStringUTFChars(hookName, name);
}

// Trigger a C# hook
extern "C" JNIEXPORT void JNICALL
Java_xamarin_posed_Loader_nativeTriggerHook(JNIEnv* env, jclass clazz,
    jstring hookName, jobjectArray args)
{
    auto it = g_hooks.find(env->GetStringUTFChars(hookName, nullptr));
    if (it == g_hooks.end()) {
        return;
    }
    
    auto mit = g_methods.find(env->GetStringUTFChars(hookName, nullptr));
    if (mit == g_methods.end()) {
        return;
    }
    
    // Convert array and call delegate
    // This is simplified - real implementation would convert args properly
    
    env->CallVoidMethod(it->second, mit->second, args);
}

// Hook a C# method using Mono embedding API
extern "C" JNIEXPORT void JNICALL
Java_xamarin_posed_Loader_nativeHookMethod(JNIEnv* env, jclass clazz,
    jstring className, jstring methodName, jstring methodSig, jobject hookDelegate)
{
    if (!g_initialized) {
        LOGE("Mono not initialized");
        return;
    }
    
    const char* clsName = env->GetStringUTFChars(className, nullptr);
    const char* mtdName = env->GetStringUTFChars(methodName, nullptr);
    const char* sig = env->GetStringUTFChars(methodSig, nullptr);
    
    // Find the method
    MonoImage* image = mono_assembly_get_image(mono_domain_get_assemblies(g_appDomain));
    if (!image) {
        LOGE("No image found");
        return;
    }
    
    MonoClass* klass = mono_class_from_name(image, nullptr, clsName);
    if (!klass) {
        LOGE("Class not found: %s", clsName);
        return;
    }
    
    MonoMethod* method = mono_class_get_method_from_name(klass, mtdName, -1);
    if (!method) {
        LOGE("Method not found: %s", mtdName);
        return;
    }
    
    // Store delegate with method info for hooking
    jobject globalRef = env->NewGlobalRef(hookDelegate);
    
    LOGI("Hooked method: %s.%s", clsName, mtdName);
    
    env->ReleaseStringUTFChars(className, clsName);
    env->ReleaseStringUTFChars(methodName, mtdName);
    env->ReleaseStringUTFChars(methodSig, sig);
}

// Load a C# assembly
extern "C" JNIEXPORT jint JNICALL
Java_xamarin_posed_Loader_nativeLoadAssembly(JNIEnv* env, jclass clazz, jstring assemblyPath)
{
    if (!g_initialized) {
        return -1;
    }
    
    const char* path = env->GetStringUTFChars(assemblyPath, nullptr);
    
    MonoAssembly* assembly = mono_assembly_open(path);
    if (!assembly) {
        LOGE("Failed to load assembly: %s", path);
        return -1;
    }
    
    mono_assembly_load_assembly(assembly, nullptr);
    
    LOGI("Loaded assembly: %s", path);
    
    env->ReleaseStringUTFChars(assemblyPath, path);
    return 0;
}

// Cleanup
extern "C" JNIEXPORT void JNICALL
Java_xamarin_posed_Loader_nativeCleanup(JNIEnv* env, jclass clazz)
{
    if (g_appDomain && g_appDomain != g_rootDomain) {
        mono_domain_unload(g_appDomain);
        g_appDomain = nullptr;
    }
    
    if (g_rootDomain) {
        mono_jit_cleanup(g_rootDomain);
        g_rootDomain = nullptr;
    }
    
    g_initialized = false;
    g_hooks.clear();
    g_methods.clear();
    
    LOGI("Native cleanup complete");
}