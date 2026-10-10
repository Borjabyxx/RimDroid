package com.rimdroid.input;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.util.LruCache;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.EnumMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * Icon packs for the on-screen buttons (ported from ValDroid's): a set of pictures named after
 * RimWorld actions, put on a whole layout in one go (each button gets the icon of what it does) or
 * picked one by one in the editor.
 *
 * <p>A pack is a zip of PNG files, white on a transparent background (the button tints them like
 * its label), named by action — see {@link #ACTIONS}: click, right_click, middle_click, zoom_in,
 * zoom_out, menu, pause, accept, speed_1..speed_4, architect, work, schedule, assign, animals,
 * wildlife, research, quests, world, history, rotate_left, rotate_right, queue, forbid,
 * prev_colonist, next_colonist, camera_up/down/left/right, keyboard, toggle_controls — plus any other
 * PNG, kept as an icon to pick by hand. An optional pack.json gives the name:
 * {"format": 1, "name": "...", "author": "..."}; "tint": false marks colour pictures, shown in their
 * own colours. Imported packs live in the app's home (control_icon_packs/&lt;id&gt;/), built-in ones in
 * the APK's assets. An icon put on a button is copied into control_icons/ (shared by every layout),
 * so the layout keeps working if its pack is deleted.
 */
public final class IconPacks {
    private IconPacks() {}

    public static final String ASSET_DIR = "control_icon_packs";
    private static final String PACKS_DIR = "control_icon_packs";
    private static final String ICONS_DIR = "control_icons";
    private static final int MAX_ICON_PX = 256;

    public static final class Pack {
        public final String id;
        public final String name;
        public final boolean builtIn;
        /** False for colour pictures (pack.json "tint": false): shown untinted. */
        public final boolean tint;
        /** Icon names (file names without .png), sorted. */
        public final List<String> icons;

        Pack(String id, String name, boolean builtIn, boolean tint, List<String> icons) {
            this.id = id;
            this.name = name;
            this.builtIn = builtIn;
            this.tint = tint;
            this.icons = icons;
        }
    }

    private static File home() {
        return new File(com.rimdroid.AppStorage.requireSingleton().getHomePath());
    }

    private static File importedDir() { return new File(home(), PACKS_DIR); }

    /** Where the icons put on buttons live; a layout refers to them by file name. */
    public static File iconsDir() { return new File(home(), ICONS_DIR); }

    /** The built-in packs, then the imported ones. */
    @NonNull
    public static List<Pack> list(@NonNull Context context) {
        List<Pack> packs = new ArrayList<>();
        try {
            String[] ids = context.getAssets().list(ASSET_DIR);
            if (ids != null) {
                for (String id : ids) {
                    String[] files = context.getAssets().list(ASSET_DIR + "/" + id);
                    if (files == null || files.length == 0) continue;
                    byte[] json = null;
                    try (InputStream in = context.getAssets().open(ASSET_DIR + "/" + id + "/pack.json")) {
                        json = readAll(in);
                    } catch (IOException ignored) {
                    }
                    packs.add(new Pack(id, packName(json, id), true, packTint(json), iconNames(files)));
                }
            }
        } catch (IOException ignored) {
        }
        File[] dirs = importedDir().listFiles(File::isDirectory);
        if (dirs != null) {
            Arrays.sort(dirs);
            for (File d : dirs) {
                if (d.getName().endsWith(".importing")) continue;
                byte[] json = null;
                File jsonFile = new File(d, "pack.json");
                if (jsonFile.isFile()) {
                    try (InputStream in = new java.io.FileInputStream(jsonFile)) {
                        json = readAll(in);
                    } catch (IOException ignored) {
                    }
                }
                String[] files = d.list();
                packs.add(new Pack(d.getName(), packName(json, d.getName()), false, packTint(json),
                        iconNames(files != null ? files : new String[0])));
            }
        }
        return packs;
    }

    private static List<String> iconNames(String[] files) {
        List<String> icons = new ArrayList<>();
        for (String f : files)
            if (f.toLowerCase(Locale.ROOT).endsWith(".png")) icons.add(f.substring(0, f.length() - 4));
        java.util.Collections.sort(icons);
        return icons;
    }

    private static boolean packTint(byte[] json) {
        if (json == null) return true;
        try {
            return new JSONObject(new String(json, StandardCharsets.UTF_8)).optBoolean("tint", true);
        } catch (Exception e) {
            return true;
        }
    }

    private static String packName(byte[] json, String fallback) {
        if (json == null) return fallback;
        try {
            String n = new JSONObject(new String(json, StandardCharsets.UTF_8)).optString("name", "").trim();
            return n.isEmpty() ? fallback : n;
        } catch (Exception e) {
            return fallback;
        }
    }

    @Nullable
    public static Bitmap load(@NonNull Context context, @NonNull Pack pack, @NonNull String icon) {
        if (pack.builtIn) {
            try (InputStream in = context.getAssets().open(ASSET_DIR + "/" + pack.id + "/" + icon + ".png")) {
                return BitmapFactory.decodeStream(in);
            } catch (IOException e) {
                return null;
            }
        }
        File f = new File(new File(importedDir(), pack.id), icon + ".png");
        return f.isFile() ? BitmapFactory.decodeFile(f.getAbsolutePath()) : null;
    }

    /**
     * Imports a pack zip: every PNG (scaled down to 256 px, transparent borders trimmed) and
     * pack.json. Its id comes from the zip's file name; importing the same name again replaces it.
     * Blocking — run it off the UI thread. Returns the pack.
     */
    @NonNull
    public static Pack importZip(@NonNull Context context, @NonNull Uri uri, @Nullable String displayName)
            throws IOException {
        String base = displayName != null ? displayName.replaceAll("(?i)\\.zip$", "") : "pack";
        String id = base.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9_-]+", "_").replaceAll("^_+|_+$", "");
        if (id.isEmpty()) id = "pack";
        File dir = new File(importedDir(), id);
        File tmp = new File(importedDir(), id + ".importing");
        deleteTree(tmp);
        if (!tmp.mkdirs()) throw new IOException("cannot create " + tmp);
        int count = 0;
        try (InputStream raw = context.getContentResolver().openInputStream(uri)) {
            if (raw == null) throw new IOException("cannot open the file");
            ZipInputStream zip = new ZipInputStream(raw);
            ZipEntry e;
            while ((e = zip.getNextEntry()) != null) {
                if (e.isDirectory()) continue;
                // Only the file name: an entry path must never leave the pack folder (zip slip).
                String name = new File(e.getName()).getName();
                String lower = name.toLowerCase(Locale.ROOT);
                if (lower.equals("pack.json")) {
                    writeFile(new File(tmp, "pack.json"), readAll(zip));
                } else if (lower.endsWith(".png")) {
                    String icon = lower.substring(0, lower.length() - 4).replaceAll("[^a-z0-9_-]+", "_");
                    Bitmap bmp = decodeScaled(readAll(zip));
                    if (bmp == null || icon.isEmpty()) continue;
                    try (FileOutputStream out = new FileOutputStream(new File(tmp, icon + ".png"))) {
                        trimTransparentBorder(bmp).compress(Bitmap.CompressFormat.PNG, 100, out);
                    }
                    count++;
                }
            }
        } catch (IOException e) {
            deleteTree(tmp);
            throw e;
        }
        if (count == 0) {
            deleteTree(tmp);
            throw new IOException("no PNG pictures in the zip");
        }
        deleteTree(dir);
        if (!tmp.renameTo(dir)) {
            deleteTree(tmp);
            throw new IOException("cannot store the pack");
        }
        for (Pack p : list(context))
            if (!p.builtIn && p.id.equals(id)) return p;
        throw new IOException("the pack did not store");
    }

    public static boolean delete(@NonNull Pack pack) {
        return !pack.builtIn && deleteTree(new File(importedDir(), pack.id));
    }

    // ---- which icon a button gets ----------------------------------------------------------

    // RimWorld 1.6's own default keys (Core KeyBindings.xml and MainButtons.xml hotkeys). A button
    // bound to one of these gets the icon of that action.
    private static final Map<Binding, String> ACTIONS = new EnumMap<>(Binding.class);

    static {
        ACTIONS.put(Binding.MOUSE_LEFT, "click");
        ACTIONS.put(Binding.MOUSE_RIGHT, "right_click");
        ACTIONS.put(Binding.MOUSE_MIDDLE, "middle_click");
        ACTIONS.put(Binding.SCROLL_UP, "zoom_in");
        ACTIONS.put(Binding.SCROLL_DOWN, "zoom_out");
        ACTIONS.put(Binding.KEY_ESCAPE, "menu");
        ACTIONS.put(Binding.KEY_SPACE, "pause");
        ACTIONS.put(Binding.KEY_ENTER, "accept");
        ACTIONS.put(Binding.KEY_1, "speed_1");
        ACTIONS.put(Binding.KEY_2, "speed_2");
        ACTIONS.put(Binding.KEY_3, "speed_3");
        ACTIONS.put(Binding.KEY_4, "speed_4");
        ACTIONS.put(Binding.KEY_TAB, "architect");
        ACTIONS.put(Binding.KEY_F1, "work");
        ACTIONS.put(Binding.KEY_F2, "schedule");
        ACTIONS.put(Binding.KEY_F3, "assign");
        ACTIONS.put(Binding.KEY_F4, "animals");
        ACTIONS.put(Binding.KEY_F5, "wildlife");
        ACTIONS.put(Binding.KEY_F6, "research");
        ACTIONS.put(Binding.KEY_F7, "quests");
        ACTIONS.put(Binding.KEY_F8, "world");
        ACTIONS.put(Binding.KEY_F9, "history");
        ACTIONS.put(Binding.KEY_Q, "rotate_left");
        ACTIONS.put(Binding.KEY_E, "rotate_right");
        ACTIONS.put(Binding.KEY_LSHIFT, "queue");
        ACTIONS.put(Binding.KEY_F, "forbid");
        ACTIONS.put(Binding.KEY_COMMA, "prev_colonist");
        ACTIONS.put(Binding.KEY_PERIOD, "next_colonist");
        ACTIONS.put(Binding.KEY_UP, "camera_up");
        ACTIONS.put(Binding.KEY_DOWN, "camera_down");
        ACTIONS.put(Binding.KEY_LEFT, "camera_left");
        ACTIONS.put(Binding.KEY_RIGHT, "camera_right");
        ACTIONS.put(Binding.TOGGLE_KEYBOARD, "keyboard");
        ACTIONS.put(Binding.TOGGLE_CONTROLS, "toggle_controls");
    }

    /** The action a binding performs, as an icon name, or null when it has no icon of its own. */
    @Nullable
    public static String actionFor(@Nullable Binding b) {
        return b == null ? null : ACTIONS.get(b);
    }

    /**
     * Puts the pack on every button whose action it has an icon for; the others keep what they show.
     * Returns how many buttons got an icon.
     */
    public static int apply(@NonNull Context context, @NonNull Pack pack, @NonNull InputControlsView view) {
        int applied = 0;
        for (ControlElement el : view.getElements()) {
            if (!(el instanceof ButtonElement)) continue;
            String icon = actionFor(((ButtonElement) el).getBinding());
            if (icon != null && pack.icons.contains(icon) && setIcon(context, pack, icon, (ButtonElement) el)) applied++;
        }
        view.invalidate();
        return applied;
    }

    /** Copies one pack icon into control_icons/ and shows it on the button. */
    public static boolean setIcon(@NonNull Context context, @NonNull Pack pack, @NonNull String icon,
                                  @NonNull ButtonElement button) {
        File dir = iconsDir();
        if (!dir.isDirectory() && !dir.mkdirs()) return false;
        Bitmap bmp = load(context, pack, icon);
        if (bmp == null) return false;
        String fileName = "pack_" + pack.id + "_" + icon + ".png";
        try (FileOutputStream out = new FileOutputStream(new File(dir, fileName))) {
            bmp.compress(Bitmap.CompressFormat.PNG, 100, out);
        } catch (IOException e) {
            return false;
        }
        CACHE.remove(fileName);   // a re-imported pack may have changed the picture
        button.setIcon(fileName, !pack.tint);
        return true;
    }

    // ---- pictures -------------------------------------------------------------------------

    // Decoded button icons by file name; a layout has a few dozen at most.
    private static final LruCache<String, Bitmap> CACHE = new LruCache<>(64);

    /** A button icon by its file name in control_icons/, or null when it is missing. */
    @Nullable
    public static Bitmap loadButtonIcon(@Nullable String fileName) {
        if (fileName == null || fileName.isEmpty()) return null;
        Bitmap b = CACHE.get(fileName);
        if (b != null) return b;
        File f = new File(iconsDir(), new File(fileName).getName());
        if (!f.isFile()) return null;
        b = BitmapFactory.decodeFile(f.getAbsolutePath());
        if (b != null) CACHE.put(fileName, b);
        return b;
    }

    @Nullable
    private static Bitmap decodeScaled(byte[] data) {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray(data, 0, data.length, bounds);
        int sample = 1;
        while (bounds.outWidth / sample > MAX_ICON_PX || bounds.outHeight / sample > MAX_ICON_PX) sample *= 2;
        BitmapFactory.Options opts = new BitmapFactory.Options();
        opts.inSampleSize = sample;
        return BitmapFactory.decodeByteArray(data, 0, data.length, opts);
    }

    /**
     * Crops fully/near-transparent borders off an icon so its visible content fills the button's
     * icon box. Returns the original bitmap if there is nothing to trim or it is entirely transparent.
     */
    public static Bitmap trimTransparentBorder(Bitmap src) {
        if (src == null) return null;
        int w = src.getWidth(), h = src.getHeight();
        if (w <= 0 || h <= 0) return src;
        int[] px = new int[w * h];
        src.getPixels(px, 0, w, 0, 0, w, h);
        final int ALPHA_MIN = 8; // treat alpha <= 8 as transparent
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++) {
            int row = y * w;
            for (int x = 0; x < w; x++) {
                int a = (px[row + x] >>> 24) & 0xff;
                if (a > ALPHA_MIN) {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < minX || maxY < minY) return src;
        if (minX == 0 && minY == 0 && maxX == w - 1 && maxY == h - 1) return src;
        return Bitmap.createBitmap(src, minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ---- files ----------------------------------------------------------------------------

    private static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16384];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        return out.toByteArray();
    }

    private static void writeFile(File f, byte[] data) throws IOException {
        try (FileOutputStream out = new FileOutputStream(f)) {
            out.write(data);
        }
    }

    private static boolean deleteTree(File f) {
        File[] children = f.listFiles();
        if (children != null) for (File c : children) deleteTree(c);
        return !f.exists() || f.delete();
    }
}
