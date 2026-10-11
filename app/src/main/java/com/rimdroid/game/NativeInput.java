package com.rimdroid.game;

/**
 * The native engine's numbers for the overlay, over JNI into il2mono (libil2cpp.so, pad.c). Only in
 * the ":unity" process, where the player has loaded that library.
 */
public final class NativeInput {
    static {
        System.loadLibrary("il2cpp");   // already loaded by the player; this binds the JNI methods
    }

    private NativeInput() {}

    private static native long nativeGetFrameCount();

    /** Frames the engine presented so far (vkshim counts them), for the FPS counter. */
    public static long frameCount() {
        return nativeGetFrameCount();
    }
}
