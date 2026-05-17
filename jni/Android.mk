LOCAL_PATH := $(call my-dir)

# Shared library for JNI bridge
include $(CLEAR_VARS)

LOCAL_MODULE    := xamarinhooks
LOCAL_SRC_FILES := src/native_bridge.cpp
LOCAL_C_INCLUDES := $(LOCAL_PATH)/include
LOCAL_LDLIBS    := -llog -landroid -lmono

# Use system mono if available, otherwise assume bundled
LOCAL_CFLAGS   := -DANDROID

include $(BUILD_SHARED_LIBRARY)