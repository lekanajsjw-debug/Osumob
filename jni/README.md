# JNI Bridge Build Instructions

## Requirements
- Android NDK (r21+)
- Mono (bundled with Xamarin.Android)

## Quick Build

### Option 1: Using ndk-build
```bash
cd jni
ndk-build
```

### Option 2: Manual
```bash
# Set NDK path
export NDK_HOME=/path/to/ndk

# Build for each ABI
$NDK_HOME/toolchains/llvm/prebuilt/*/bin/aarch64-linux-android21-clang++ \
  -shared \
  -fPIC \
  -I$MONO_HOME/include/mono-2.0 \
  src/native_bridge.cpp \
  -o libs/arm64-v8a/libxamarinhooks.so
```

## Output
- `libs/armeabi-v7a/libxamarinhooks.so`
- `libs/arm64-v8a/libxamarinhooks.so`
- `libs/x86/libxamarinhooks.so`
- `libs/x86_64/libxamarinhooks.so`

## Copy to project
Place .so files in:
- `XamarinPosed/libs/<abi>/`

## API

### Java Side
```java
// Initialize native bridge
Loader.nativeInit("app_name");

// Register hook
Loader.nativeRegisterHook("hook_name", delegate);

// Trigger hook
Loader.nativeTriggerHook("hook_name", args);
```

### C# Side
```csharp
Loader.RegisterHook("event_name", (args) => {
    // Your code
});

Loader.HookMethod("ClassName", "MethodName", "(I)V", (args) => {
    // Hook specific method
});
```