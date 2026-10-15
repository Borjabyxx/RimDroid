package com.rimdroid;

import android.content.Context;
import android.text.InputType;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.BaseInputConnection;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.widget.FrameLayout;

import com.rimdroid.input.InputControlsView;
import com.rimdroid.input.InputSink;

/**
 * Input for RimWorld 1.7 (ClrActivity): the on-screen controls of the other engines, the soft
 * keyboard and a physical keyboard, delivered to the game through our GLFW (glfw_android.c).
 *
 * The controls speak SDL (scancodes in their bindings, buttons 1 left / 2 middle / 3 right); GLFW has
 * its own key codes, so both are translated here. Typed text goes in as Unicode characters — the
 * game's ImGui takes any script, so unlike 1.6 Cyrillic and the rest work too.
 */
final class ClrInput implements InputSink.Backend {
    final InputControlsView controls;
    private final KeyboardCatcher keyboard;

    ClrInput(Context context, FrameLayout root, String instanceName) {
        InputSink.setBackend(this);
        controls = new InputControlsView(context, 1f, instanceName);   // the game fills the screen
        root.addView(controls, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));
        keyboard = new KeyboardCatcher(context);
        root.addView(keyboard, new FrameLayout.LayoutParams(1, 1));   // 1 px, invisible
    }

    /** The game's window size in view pixels (the surface fills the screen). */
    void setSize(int w, int h) { controls.setGameRect(0, 0, w, h); }

    // ---- InputSink.Backend (from the on-screen controls; positions are view pixels) ----

    @Override public void touch(int x, int y) { ClrActivity.glfwTouch(2, x, y); }

    @Override public void button(int button, boolean down, int x, int y) {
        int b = button == 1 ? 0 : button == 3 ? 1 : button == 2 ? 2 : -1;
        if (b >= 0) ClrActivity.glfwMouse(b, down ? 1 : 0, x, y);
    }

    @Override public void scroll(int x, int y, int dy) {
        ClrActivity.glfwTouch(2, x, y);
        ClrActivity.glfwScroll(0, dy);
    }

    @Override public void key(int sdlScancode, int sdlKeycode, boolean down) {
        int k = fromSdl(sdlScancode);
        if (k > 0) ClrActivity.glfwKey(k, down ? 1 : 0, 0);
    }

    @Override public void text(String text) { typeText(text); }

    @Override public void toggleKeyboard() { keyboard.toggle(); }

    static void typeText(String text) {
        if (text == null) return;
        text.codePoints().forEach(cp -> { if (cp >= 0x20 && cp != 0x7f) ClrActivity.glfwChar(cp); });
    }

    static void tapKey(int glfwKey) {
        ClrActivity.glfwKey(glfwKey, 1, 0);
        ClrActivity.glfwKey(glfwKey, 0, 0);
    }

    // ---- a physical keyboard (ClrActivity.dispatchKeyEvent) and the soft keyboard's key events ----

    /** True if the event was a key the game takes. */
    static boolean onKeyEvent(KeyEvent e) {
        int k = fromAndroid(e.getKeyCode());
        if (k <= 0 || (e.getAction() != KeyEvent.ACTION_DOWN && e.getAction() != KeyEvent.ACTION_UP)) return false;
        boolean down = e.getAction() == KeyEvent.ACTION_DOWN;
        if (!down || e.getRepeatCount() == 0) ClrActivity.glfwKey(k, down ? 1 : 0, 0);
        if (down) {
            int cp = e.getUnicodeChar(e.getMetaState());
            if (cp > 0 && (cp & KeyCharacterMapFlags.COMBINING) == 0) typeText(new String(Character.toChars(cp)));
        }
        return true;
    }

    private static final class KeyCharacterMapFlags {
        static final int COMBINING = android.view.KeyCharacterMap.COMBINING_ACCENT;
    }

    // GLFW key codes (glfw3.h)
    static final int SPACE = 32, ESCAPE = 256, ENTER = 257, TAB = 258, BACKSPACE = 259, INSERT = 260,
            DELETE = 261, RIGHT = 262, LEFT = 263, DOWN = 264, UP = 265, PAGE_UP = 266, PAGE_DOWN = 267,
            HOME = 268, END = 269, CAPS_LOCK = 280, F1 = 290, KP_0 = 320, LEFT_SHIFT = 340,
            LEFT_CONTROL = 341, LEFT_ALT = 342, LEFT_SUPER = 343, RIGHT_SHIFT = 344, RIGHT_CONTROL = 345,
            RIGHT_ALT = 346, RIGHT_SUPER = 347;

    /** SDL scancode (USB HID usage, the controls' Binding.code) → GLFW key; 0 if none. */
    static int fromSdl(int sc) {
        if (sc >= 4 && sc <= 29) return 'A' + (sc - 4);
        if (sc >= 30 && sc <= 38) return '1' + (sc - 30);
        if (sc >= 58 && sc <= 69) return F1 + (sc - 58);
        if (sc >= 89 && sc <= 97) return KP_0 + 1 + (sc - 89);
        switch (sc) {
            case 39: return '0';
            case 40: return ENTER;
            case 41: return ESCAPE;
            case 42: return BACKSPACE;
            case 43: return TAB;
            case 44: return SPACE;
            case 45: return '-';
            case 46: return '=';
            case 47: return '[';
            case 48: return ']';
            case 49: return '\\';
            case 51: return ';';
            case 52: return '\'';
            case 53: return '`';
            case 54: return ',';
            case 55: return '.';
            case 56: return '/';
            case 57: return CAPS_LOCK;
            case 73: return INSERT;
            case 74: return HOME;
            case 75: return PAGE_UP;
            case 76: return DELETE;
            case 77: return END;
            case 78: return PAGE_DOWN;
            case 79: return RIGHT;
            case 80: return LEFT;
            case 81: return DOWN;
            case 82: return UP;
            case 98: return KP_0;
            case 224: return LEFT_CONTROL;
            case 225: return LEFT_SHIFT;
            case 226: return LEFT_ALT;
            case 227: return LEFT_SUPER;
            case 228: return RIGHT_CONTROL;
            case 229: return RIGHT_SHIFT;
            case 230: return RIGHT_ALT;
            case 231: return RIGHT_SUPER;
            default: return 0;
        }
    }

    /** Android key code → GLFW key; 0 if none. */
    static int fromAndroid(int kc) {
        if (kc >= KeyEvent.KEYCODE_A && kc <= KeyEvent.KEYCODE_Z) return 'A' + (kc - KeyEvent.KEYCODE_A);
        if (kc >= KeyEvent.KEYCODE_0 && kc <= KeyEvent.KEYCODE_9) return '0' + (kc - KeyEvent.KEYCODE_0);
        if (kc >= KeyEvent.KEYCODE_F1 && kc <= KeyEvent.KEYCODE_F12) return F1 + (kc - KeyEvent.KEYCODE_F1);
        if (kc >= KeyEvent.KEYCODE_NUMPAD_0 && kc <= KeyEvent.KEYCODE_NUMPAD_9) return KP_0 + (kc - KeyEvent.KEYCODE_NUMPAD_0);
        switch (kc) {
            case KeyEvent.KEYCODE_SPACE: return SPACE;
            case KeyEvent.KEYCODE_ESCAPE: return ESCAPE;
            case KeyEvent.KEYCODE_ENTER: case KeyEvent.KEYCODE_NUMPAD_ENTER: return ENTER;
            case KeyEvent.KEYCODE_TAB: return TAB;
            case KeyEvent.KEYCODE_DEL: return BACKSPACE;
            case KeyEvent.KEYCODE_FORWARD_DEL: return DELETE;
            case KeyEvent.KEYCODE_INSERT: return INSERT;
            case KeyEvent.KEYCODE_DPAD_RIGHT: return RIGHT;
            case KeyEvent.KEYCODE_DPAD_LEFT: return LEFT;
            case KeyEvent.KEYCODE_DPAD_DOWN: return DOWN;
            case KeyEvent.KEYCODE_DPAD_UP: return UP;
            case KeyEvent.KEYCODE_PAGE_UP: return PAGE_UP;
            case KeyEvent.KEYCODE_PAGE_DOWN: return PAGE_DOWN;
            case KeyEvent.KEYCODE_MOVE_HOME: return HOME;
            case KeyEvent.KEYCODE_MOVE_END: return END;
            case KeyEvent.KEYCODE_CAPS_LOCK: return CAPS_LOCK;
            case KeyEvent.KEYCODE_MINUS: return '-';
            case KeyEvent.KEYCODE_EQUALS: return '=';
            case KeyEvent.KEYCODE_LEFT_BRACKET: return '[';
            case KeyEvent.KEYCODE_RIGHT_BRACKET: return ']';
            case KeyEvent.KEYCODE_BACKSLASH: return '\\';
            case KeyEvent.KEYCODE_SEMICOLON: return ';';
            case KeyEvent.KEYCODE_APOSTROPHE: return '\'';
            case KeyEvent.KEYCODE_GRAVE: return '`';
            case KeyEvent.KEYCODE_COMMA: return ',';
            case KeyEvent.KEYCODE_PERIOD: return '.';
            case KeyEvent.KEYCODE_SLASH: return '/';
            case KeyEvent.KEYCODE_SHIFT_LEFT: return LEFT_SHIFT;
            case KeyEvent.KEYCODE_SHIFT_RIGHT: return RIGHT_SHIFT;
            case KeyEvent.KEYCODE_CTRL_LEFT: return LEFT_CONTROL;
            case KeyEvent.KEYCODE_CTRL_RIGHT: return RIGHT_CONTROL;
            case KeyEvent.KEYCODE_ALT_LEFT: return LEFT_ALT;
            case KeyEvent.KEYCODE_ALT_RIGHT: return RIGHT_ALT;
            case KeyEvent.KEYCODE_META_LEFT: return LEFT_SUPER;
            case KeyEvent.KEYCODE_META_RIGHT: return RIGHT_SUPER;
            default: return 0;
        }
    }

    /**
     * The soft keyboard: an invisible 1-px view that takes its text. Words a keyboard composes
     * (Gboard and most others) are followed as they change: added characters are typed, removed ones
     * are Backspaces, so the game's text field always matches what the keyboard shows.
     */
    private static final class KeyboardCatcher extends View {
        private boolean accepting;

        KeyboardCatcher(Context c) {
            super(c);
            setFocusable(true);
            setFocusableInTouchMode(true);
        }

        void toggle() {
            InputMethodManager imm = (InputMethodManager) getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm == null) return;
            accepting = !accepting;
            requestFocus();
            imm.restartInput(this);
            if (accepting) imm.showSoftInput(this, InputMethodManager.SHOW_FORCED);
            else imm.hideSoftInputFromWindow(getWindowToken(), 0);
        }

        @Override public boolean onCheckIsTextEditor() { return accepting; }

        @Override public boolean onKeyDown(int keyCode, KeyEvent event) { return onKeyEvent(event) || super.onKeyDown(keyCode, event); }
        @Override public boolean onKeyUp(int keyCode, KeyEvent event) { return onKeyEvent(event) || super.onKeyUp(keyCode, event); }

        @Override public InputConnection onCreateInputConnection(EditorInfo outAttrs) {
            if (!accepting) return null;
            outAttrs.inputType = InputType.TYPE_CLASS_TEXT;
            outAttrs.imeOptions = EditorInfo.IME_FLAG_NO_FULLSCREEN | EditorInfo.IME_FLAG_NO_EXTRACT_UI
                    | EditorInfo.IME_ACTION_DONE;
            return new BaseInputConnection(this, false) {
                private String composing = "";

                private void backspace(int n) { for (int i = 0; i < n; i++) tapKey(BACKSPACE); }

                /** The keyboard's current word replaces what it composed before: the common prefix stays. */
                private void edit(String text) {
                    int keep = 0;
                    while (keep < composing.length() && keep < text.length() && composing.charAt(keep) == text.charAt(keep)) keep++;
                    backspace(composing.codePointCount(keep, composing.length()));
                    typeText(text.substring(keep));
                }

                @Override public boolean setComposingText(CharSequence text, int newCursorPosition) {
                    String s = text == null ? "" : text.toString();
                    edit(s);
                    composing = s;
                    return true;
                }

                @Override public boolean commitText(CharSequence text, int newCursorPosition) {
                    String s = text == null ? "" : text.toString();
                    if (s.equals("\n")) { edit(""); composing = ""; tapKey(ENTER); return true; }
                    edit(s);
                    composing = "";
                    return true;
                }

                @Override public boolean finishComposingText() { composing = ""; return true; }

                // The dummy editor holds no text, so a delete would remove nothing: send Backspace.
                @Override public boolean deleteSurroundingText(int before, int after) {
                    backspace(before);
                    return true;
                }

                @Override public boolean performEditorAction(int action) {
                    tapKey(ENTER);
                    return true;
                }

                @Override public boolean sendKeyEvent(KeyEvent event) { return onKeyEvent(event); }
            };
        }
    }
}
