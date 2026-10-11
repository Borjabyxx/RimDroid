package com.rimdroid.nativeunity;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.view.InputEvent;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.FrameLayout;

import com.unity3d.player.IUnityPlayerLifecycleEvents;
import com.unity3d.player.UnityPlayer;

/**
 * Hosts Unity's native Android player in its own process (":unity"). The game's C# runs on RimDroid's
 * ARM64 Mono through il2mono (libil2cpp.so). Ported from ValDroid's activity (Unity 6) to the Unity
 * 2022.3 player API RimWorld 1.6 needs: there UnityPlayer is itself the FrameLayout and takes every
 * lifecycle and input callback, as Unity's own 2022.3 UnityPlayerActivity forwards them.
 */
public class NativeUnityActivity extends Activity implements IUnityPlayerLifecycleEvents {

    // Read by Unity's native code under this exact name.
    protected UnityPlayer mUnityPlayer;

    /** Intent extra: zip (stored) with assets/bin/Data/... the player mounts instead of this APK. */
    public static final String EXTRA_DATA_ARCHIVE = "rimdroid.dataArchive";
    /** Intent extra: String[] of "KEY=VALUE" set in this process's environment (il2mono's paths). */
    public static final String EXTRA_ENV = "rimdroid.env";
    /**
     * Intent extra: class name of a View (public constructor taking a Context) laid over the player:
     * the launcher's on-screen controls. By name, because the launcher depends on this module and not
     * the other way round. Touches the overlay does not take reach the player.
     */
    public static final String EXTRA_OVERLAY = "rimdroid.overlay";

    // The overlay; a physical keyboard's and gamepad's events are offered to it first
    // (View.OnKeyListener / OnGenericMotionListener).
    private View overlay;

    @Override protected void onCreate(Bundle savedInstanceState) {
        requestWindowFeature(Window.FEATURE_NO_TITLE);
        super.onCreate(savedInstanceState);
        // Before the player loads libmain/libunity/il2mono: il2mono reads its paths from the environment.
        applyEnvironment(getIntent().getStringArrayExtra(EXTRA_ENV));
        // The player reads its command line from the "unity" extra.
        getIntent().putExtra("unity", getIntent().getStringExtra("unity"));

        mUnityPlayer = new UnityPlayer(this, this);
        setContentView(mUnityPlayer);
        mUnityPlayer.requestFocus();
        overlay = createOverlay(getIntent().getStringExtra(EXTRA_OVERLAY));
        if (overlay != null) {
            mUnityPlayer.addView(overlay, new FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        }
        keepScreenOn();
        hideSystemBars();   // after setContentView: the decor view / insets controller now exist
    }

    // The player hides the bars itself (unity.launch-fullscreen), but not for good: on Samsung the
    // gesture pill stays over the game. The same as GameActivity: hidden, a swipe shows them for a
    // moment, then they go again.
    private void hideSystemBars() {
        android.view.WindowInsetsController c = getWindow().getInsetsController();
        if (c != null) {
            c.hide(android.view.WindowInsets.Type.systemBars());
            c.setSystemBarsBehavior(
                    android.view.WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        }
    }

    private View createOverlay(String className) {
        if (className == null) return null;
        try {
            return (View) Class.forName(className).getConstructor(Context.class).newInstance(this);
        } catch (ReflectiveOperationException | ClassCastException e) {
            android.util.Log.e("NativeUnity", "overlay " + className + " failed", e);
            return null;
        }
    }

    private static void applyEnvironment(String[] env) {
        if (env == null) return;
        for (String entry : env) {
            int eq = entry.indexOf('=');
            if (eq <= 0) continue;
            try {
                android.system.Os.setenv(entry.substring(0, eq), entry.substring(eq + 1), true);
            } catch (android.system.ErrnoException e) {
                android.util.Log.w("NativeUnity", "setenv " + entry.substring(0, eq) + " failed", e);
            }
        }
    }

    // Unity mounts the archive at getPackageCodePath() as its data source (assets/bin/Data). Pointing it
    // at an archive the launcher builds from the game instance keeps every game file out of the APK.
    // Native libraries are loaded from nativeLibraryDir and are not affected.
    @Override public String getPackageCodePath() {
        Intent intent = getIntent();
        String archive = intent != null ? intent.getStringExtra(EXTRA_DATA_ARCHIVE) : null;
        if (archive != null && new java.io.File(archive).isFile()) return archive;
        return super.getPackageCodePath();
    }

    // RimWorld is landscape only. The player applies the orientation from the game's player settings
    // at start-up, and the Linux build's settings allow portrait; keep sensor landscape whatever it asks.
    @Override public void setRequestedOrientation(int requestedOrientation) {
        super.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
    }

    // Desktop games never ask for this; il2mono also maps Screen.sleepTimeout to NeverSleep.
    private void keepScreenOn() {
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }

    @Override public void onUnityPlayerUnloaded() { moveTaskToBack(true); }

    @Override public void onUnityPlayerQuitted() { }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        mUnityPlayer.newIntent(intent);
    }

    @Override protected void onDestroy() {
        mUnityPlayer.destroy();
        super.onDestroy();
    }

    @Override protected void onPause() { super.onPause(); mUnityPlayer.pause(); }
    @Override protected void onResume() { super.onResume(); mUnityPlayer.resume(); }

    @Override public void onLowMemory() {
        super.onLowMemory();
        mUnityPlayer.lowMemory();
    }

    @Override public void onTrimMemory(int level) {
        super.onTrimMemory(level);
        if (level == TRIM_MEMORY_RUNNING_CRITICAL) mUnityPlayer.lowMemory();
    }

    @Override public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        mUnityPlayer.configurationChanged(newConfig);
    }

    @Override public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        mUnityPlayer.windowFocusChanged(hasFocus);
        if (hasFocus) {
            keepScreenOn();     // the player resets the flag on start
            hideSystemBars();   // re-hide after dialogs / focus regain
        }
    }

    /** The overlay's input (mouse and keyboard events made from the on-screen controls) into the player. */
    public boolean injectInputEvent(InputEvent event) {
        return mUnityPlayer != null && mUnityPlayer.injectEvent(event);
    }

    // The NDK does not deliver ACTION_MULTIPLE key events; inject them.
    @Override public boolean dispatchKeyEvent(KeyEvent event) {
        if (event.getAction() == KeyEvent.ACTION_MULTIPLE) return mUnityPlayer.injectEvent(event);
        if (overlay instanceof View.OnKeyListener
                && ((View.OnKeyListener) overlay).onKey(overlay, event.getKeyCode(), event)) return true;
        return super.dispatchKeyEvent(event);
    }

    @Override public boolean dispatchGenericMotionEvent(MotionEvent event) {
        if (overlay instanceof View.OnGenericMotionListener
                && ((View.OnGenericMotionListener) overlay).onGenericMotion(overlay, event)) return true;
        return super.dispatchGenericMotionEvent(event);
    }

    // Events no view handled go straight to the player.
    @Override public boolean onKeyUp(int keyCode, KeyEvent event) { return mUnityPlayer.injectEvent(event); }
    @Override public boolean onKeyDown(int keyCode, KeyEvent event) { return mUnityPlayer.injectEvent(event); }
    @Override public boolean onTouchEvent(MotionEvent event) { return mUnityPlayer.injectEvent(event); }
    @Override public boolean onGenericMotionEvent(MotionEvent event) { return mUnityPlayer.injectEvent(event); }
}
