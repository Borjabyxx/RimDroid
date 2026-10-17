package com.rimdroid.fragments;

import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.SeekBar;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;

import androidx.fragment.app.Fragment;

import com.rimdroid.InstanceSettings;
import com.rimdroid.LauncherPreferences;
import com.rimdroid.LauncherPreferences.VulkanDriverOption;
import com.rimdroid.R;
import com.rimdroid.game.GameInstance;
import com.rimdroid.game.GameInstanceManager;

import java.util.List;

public class SettingsFragment extends Fragment {
    // While the native engine is chosen the renderer group shows Vulkan and ignores its own checks.
    private boolean rendererLocked;
    private boolean waylithInstance;   // RimWorld 1.7: its own Vulkan renderer, none of the box64 video options

    /** Pass the instance name via arguments so this page edits THAT instance's settings. */
    public static final String ARG_INSTANCE = "instance";

    private LauncherPreferences prefs;
    private InstanceSettings inst;   // per-instance: renderer / driver / debug / interpreter / scale / controls
    private String instanceName;     // the instance this page edits

    @Override
    public View onCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState) {
        return inflater.inflate(R.layout.fragment_settings, container, false);
    }

    @Override
    public void onViewCreated(View view, Bundle savedInstanceState) {
        super.onViewCreated(view, savedInstanceState);
        prefs = LauncherPreferences.requireSingleton();

        // Which instance are we editing? Argument (from the launcher card's gear) → last launched →
        // first installed → a "default" profile (degenerate case when no instances exist yet).
        String instName = getArguments() != null ? getArguments().getString(ARG_INSTANCE) : null;
        if (instName == null || instName.isEmpty()) instName = prefs.getLastInstanceName();
        if (instName == null || instName.isEmpty()) {
            List<GameInstance> all = GameInstanceManager.requireSingleton().getInstances();
            if (!all.isEmpty()) instName = all.get(0).getName();
        }
        if (instName == null || instName.isEmpty()) instName = "default";
        instanceName = instName;
        inst = new InstanceSettings(instName);
        requireActivity().setTitle(getString(R.string.nav_settings) + " — " + instName);

        Switch swDebug        = view.findViewById(R.id.sw_debug);
        Switch swDragPan      = view.findViewById(R.id.sw_drag_pan);
        Switch swReverse      = view.findViewById(R.id.sw_reverse_landscape);
        Switch swCompat       = view.findViewById(R.id.sw_compat_mode);
        Switch swHaptic       = view.findViewById(R.id.sw_haptic);
        final android.widget.Button btnSteamDl = view.findViewById(R.id.btn_steam_dl);
        final TextView tvSteamDlStatus = view.findViewById(R.id.tv_steam_dl_status);

        // Renderer chooser: ZINK_ZFA (GPU via Vulkan, default) or MOBILEGLUES (GPU via the phone's
        // own GLES driver — zero Vulkan; first full 1.5 session 2026-08-09, 62 fps on the S25).
        // MobileGlues replaced the short-lived softpipe (CPU, 1-2 fps) entry the same day; the
        // softpipe path itself was removed later as it never became usable.
        // GL4ES / ZINK_OSMESA stay hidden; any instance set to a non-UI renderer migrates to ZFA.
        android.widget.RadioGroup rgRenderer = view.findViewById(R.id.rg_renderer);
        android.widget.RadioButton rbZinkZfa = view.findViewById(R.id.rb_zink_zfa);
        android.widget.RadioButton rbMobileGlues = view.findViewById(R.id.rb_mobileglues);
        switch (inst.getRenderer()) {
            case MOBILEGLUES:
                rbMobileGlues.setChecked(true);
                break;
            case ZINK_ZFA:
            default:
                rbZinkZfa.setChecked(true);
                inst.setRenderer(LauncherPreferences.Renderer.ZINK_ZFA);
                break;
        }
        rgRenderer.setOnCheckedChangeListener((group, checkedId) -> {
            if (rendererLocked) return;   // shown for the native engine, not the player's choice
            if (checkedId == R.id.rb_zink_zfa) {
                inst.setRenderer(LauncherPreferences.Renderer.ZINK_ZFA);
            } else if (checkedId == R.id.rb_mobileglues) {
                inst.setRenderer(LauncherPreferences.Renderer.MOBILEGLUES);
            }
        });
        swDebug.setChecked(inst.isDebug());
        swDragPan.setChecked(inst.isDragPan());
        swDragPan.setOnCheckedChangeListener((btn, checked) -> inst.setDragPan(checked));
        // Mirrored landscape: opt-in for USB-C gamepad cradles that hold the phone the other way up.
        // Takes effect on the next launch (orientation is requested once in GameActivity.onCreate).
        swReverse.setChecked(inst.isReverseLandscape());
        swReverse.setOnCheckedChangeListener((btn, checked) -> inst.setReverseLandscape(checked));
        // Compatibility mode: box64 FP/barrier tuning (WEAKBARRIER=2 + X87DOUBLE=1) that lets the game launch
        // on devices hit by the deep "won't start / black screen" bug (Adreno 610/725, weak-Vulkan Mali).
        swCompat.setChecked(inst.isCompatibilityMode());
        // No warning dialog on enabling it any more: it sent players to the RimDroidSaveFix mod, and
        // the save bug that mod worked around is fixed at the root (box64 qsort). The switch's own
        // hint already says it is slower and only helps some devices.
        swCompat.setOnCheckedChangeListener((btn, checked) -> inst.setCompatibilityMode(checked));
        // Engine: full emulation / box64 + native Mono / native engine. An option this APK or instance
        // cannot run is hidden: native Mono needs the runtime packaged and a 1.6 instance
        // (NativeMono.isSupported), the native engine the player module and 1.6 (NativeEngine.isSupported).
        // With only one left there is nothing to choose.
        GameInstance engineInstance = null;
        for (GameInstance candidate : GameInstanceManager.requireSingleton().getInstances()) {
            if (candidate.getName().equals(instanceName)) engineInstance = candidate;
        }
        boolean nativeMonoOffered = com.rimdroid.game.NativeMono.isSupported(engineInstance);
        boolean nativeEngineOffered = com.rimdroid.game.NativeEngine.isSupported(engineInstance);
        waylithInstance = engineInstance != null && com.rimdroid.game.DotnetEngine.isWaylith(engineInstance);
        RadioGroup rgEngine = view.findViewById(R.id.rg_engine);
        view.findViewById(R.id.rb_engine_box64_mono).setVisibility(nativeMonoOffered ? View.VISIBLE : View.GONE);
        view.findViewById(R.id.tv_engine_box64_mono_hint).setVisibility(nativeMonoOffered ? View.VISIBLE : View.GONE);
        view.findViewById(R.id.rb_engine_native).setVisibility(nativeEngineOffered ? View.VISIBLE : View.GONE);
        view.findViewById(R.id.tv_engine_native_hint).setVisibility(nativeEngineOffered ? View.VISIBLE : View.GONE);
        int engineChoices = 1 + (nativeMonoOffered ? 1 : 0) + (nativeEngineOffered ? 1 : 0);
        view.findViewById(R.id.card_engine).setVisibility(engineChoices > 1 ? View.VISIBLE : View.GONE);
        int engine = inst.getEngine();
        if (engine == InstanceSettings.ENGINE_NATIVE && !nativeEngineOffered) engine = InstanceSettings.ENGINE_BOX64_MONO;
        if (engine == InstanceSettings.ENGINE_BOX64_MONO && !nativeMonoOffered) engine = InstanceSettings.ENGINE_BOX64;
        rgEngine.check(engine == InstanceSettings.ENGINE_NATIVE ? R.id.rb_engine_native
                : engine == InstanceSettings.ENGINE_BOX64_MONO ? R.id.rb_engine_box64_mono : R.id.rb_engine_box64);
        applyEngineState(view, engine);
        rgEngine.setOnCheckedChangeListener((group, checkedId) -> {
            int chosen = checkedId == R.id.rb_engine_native ? InstanceSettings.ENGINE_NATIVE
                    : checkedId == R.id.rb_engine_box64_mono ? InstanceSettings.ENGINE_BOX64_MONO
                    : InstanceSettings.ENGINE_BOX64;
            inst.setEngine(chosen);
            applyEngineState(view, chosen);
            // Leaving native Mono for full emulation is the player's way around a problem we would
            // otherwise never hear about.
            if (chosen == InstanceSettings.ENGINE_BOX64) {
                new com.google.android.material.dialog.MaterialAlertDialogBuilder(requireContext())
                        .setMessage(R.string.native_mono_off_msg)
                        .setPositiveButton(android.R.string.ok, null)
                        .show();
            }
        });
        // Vulkan compatibility (native engine): Auto / On / Off, in the order of the VK_COMPAT_ values.
        // Auto says what it comes to on this phone.
        Spinner spVkCompat = view.findViewById(R.id.sp_vk_compat);
        ArrayAdapter<String> vkCompatAdapter = new ArrayAdapter<>(requireContext(),
                android.R.layout.simple_spinner_item, new String[]{
                        getString(com.rimdroid.game.NativeEngine.needsVulkanCompat()
                                ? R.string.vk_compat_auto_on : R.string.vk_compat_auto_off),
                        getString(R.string.vk_compat_on), getString(R.string.vk_compat_off) });
        vkCompatAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spVkCompat.setAdapter(vkCompatAdapter);
        spVkCompat.setSelection(inst.getVulkanCompat());
        spVkCompat.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View v, int position, long id) {
                inst.setVulkanCompat(position);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {}
        });
        swHaptic.setChecked(inst.isHapticFeedback());
        swHaptic.setOnCheckedChangeListener((btn, checked) -> inst.setHapticFeedback(checked));
        // In-game overlay — GLOBAL: off / classic FPS counter / full performance bar (ported from
        // ValDroid). Shows the true presented frame rate; the bar adds what it is up against.
        // Takes effect next game launch.
        android.widget.RadioGroup rgHud = view.findViewById(R.id.rg_hud);
        switch (prefs.getHudMode()) {
            case LauncherPreferences.HUD_FULL: rgHud.check(R.id.rb_hud_full); break;
            case LauncherPreferences.HUD_FPS:  rgHud.check(R.id.rb_hud_fps);  break;
            default:                           rgHud.check(R.id.rb_hud_off);  break;
        }
        rgHud.setOnCheckedChangeListener((group, checkedId) -> prefs.setHudMode(
                checkedId == R.id.rb_hud_full ? LauncherPreferences.HUD_FULL
              : checkedId == R.id.rb_hud_fps  ? LauncherPreferences.HUD_FPS
              : LauncherPreferences.HUD_OFF));
        // Audio has no UI: it's always on. Raw Vorbis decodes clean since the box64 qsort_r fix, so the
        // launcher loads the libasound→AAudio shim on every launch (GameLauncher) — no toggle, no pack.

        // Advanced: per-instance extra env vars (KEY=VALUE, space-separated). Applied last in
        // GameLauncher so they override the box64 defaults. Diagnostic/perf knob (e.g.
        // BOX64_DYNAREC_ALIGNED_ATOMICS=1 for the Mali/Cortex save bug, or BOX64_DYNAREC_STRONGMEM=2).
        final android.widget.EditText etEnv = view.findViewById(R.id.et_env_vars);
        String envCur = inst.getEnvVars();
        etEnv.setText(envCur != null ? envCur : "");
        etEnv.addTextChangedListener(new android.text.TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(android.text.Editable s) {
                inst.setEnvVars(s.toString().replace('\n', ' '));   // newlines → spaces (KEY=VALUE list)
            }
        });
        // The whole Advanced card is collapsed by default (power-user diagnostics — too many testers
        // wandered in). Tap the header to expand/collapse; the arrow flips to match.
        final TextView advHeader = view.findViewById(R.id.tv_advanced_header);
        final View advContent = view.findViewById(R.id.ll_advanced_content);
        advHeader.setOnClickListener(v -> {
            boolean show = advContent.getVisibility() != View.VISIBLE;
            advContent.setVisibility(show ? View.VISIBLE : View.GONE);
            advHeader.setCompoundDrawablesWithIntrinsicBounds(0, 0,
                    show ? android.R.drawable.arrow_up_float : android.R.drawable.arrow_down_float, 0);
        });

        // (The old Steam auth-only spike + on-device sound-pack generator shared this button; both are
        //  gone now — Steam auth moved to the Download screen, and audio is always-on raw Vorbis.)

        // Anon-mod-download spike — REMOVED (the real anonymous downloader shipped). Button hidden.
        // The old Steam downloader spike button (full game download, 1.5 only) stays hidden: the
        // "Download game (Steam)" screen replaced it.
        btnSteamDl.setVisibility(View.GONE);
        tvSteamDlStatus.setVisibility(View.GONE);

        // (Steam Cloud saves moved out of debug into its own screen: drawer -> "Steam Cloud saves"
        //  = CloudSavesFragment. The old debug spike button here was removed.)

        // "Upload your custom driver" link (under the driver picker) → the device-global import screen.
        View tvUpload = view.findViewById(R.id.tv_upload_driver);
        tvUpload.setOnClickListener(v ->
                androidx.navigation.Navigation.findNavController(v).navigate(R.id.action_open_custom_driver));

        swDebug.setOnCheckedChangeListener((btn, checked) -> {
            inst.setDebug(checked);   // per-instance debug
            // The Steam spike buttons
            // (btnSteamDl/btnSteamSpike — superseded by the Download screen) all stay fully hidden.
        });


        // --- Vulkan driver picker ---
        Spinner spDriver = view.findViewById(R.id.spinner_vulkan_driver);
        // The "Custom driver (imported)" entry is only usable once a driver has been imported
        // (App settings → Import custom Vulkan driver). Until then it is shown greyed-out and
        // cannot be selected.
        final boolean customAvailable = com.rimdroid.CustomDriverInstaller.isInstalled();
        ArrayAdapter<VulkanDriverOption> driverAdapter = new ArrayAdapter<VulkanDriverOption>(
                requireContext(), android.R.layout.simple_spinner_item,
                LauncherPreferences.VULKAN_DRIVERS) {
            private boolean isCustom(int pos) {
                return com.rimdroid.C.deps.CUSTOM_DRIVER_FILENAME.equals(
                        LauncherPreferences.VULKAN_DRIVERS.get(pos).soName);
            }
            @Override public boolean areAllItemsEnabled() { return customAvailable; }
            @Override public boolean isEnabled(int pos) { return customAvailable || !isCustom(pos); }
            @Override public View getDropDownView(int pos, View convertView, android.view.ViewGroup parent) {
                View v = super.getDropDownView(pos, convertView, parent);
                boolean enabled = isEnabled(pos);
                if (v instanceof TextView) ((TextView) v).setEnabled(enabled);
                v.setEnabled(enabled);
                v.setAlpha(enabled ? 1f : 0.4f);
                return v;
            }
        };
        driverAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spDriver.setAdapter(driverAdapter);

        // If the saved choice was the custom driver but none is imported, fall back to System.
        int driverIdx = inst.getVulkanDriverIndex();
        if (!customAvailable && com.rimdroid.C.deps.CUSTOM_DRIVER_FILENAME.equals(
                LauncherPreferences.VULKAN_DRIVERS.get(driverIdx).soName)) {
            driverIdx = 0;
            inst.setVulkanDriverSo(LauncherPreferences.VULKAN_DRIVERS.get(0).soName);
        }
        spDriver.setSelection(driverIdx);
        spDriver.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View v, int pos, long id) {
                if (!customAvailable && com.rimdroid.C.deps.CUSTOM_DRIVER_FILENAME.equals(
                        LauncherPreferences.VULKAN_DRIVERS.get(pos).soName)) {
                    spDriver.setSelection(0);   // disabled entry — revert to System
                    return;
                }
                inst.setVulkanDriverSo(LauncherPreferences.VULKAN_DRIVERS.get(pos).soName);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) {}
        });

        // --- Recommended driver: detect the GPU and pick the matching bundled driver ---
        View btnRecommend = view.findViewById(R.id.btn_recommend_driver);
        btnRecommend.setOnClickListener(v -> {
            btnRecommend.setEnabled(false);
            new Thread(() -> {
                com.rimdroid.GpuInfo gpu = com.rimdroid.GpuInfo.query();
                String so = gpu.recommendedDriverSo();
                int idx = 0;
                for (int i = 0; i < LauncherPreferences.VULKAN_DRIVERS.size(); i++) {
                    if (LauncherPreferences.VULKAN_DRIVERS.get(i).soName.equals(so)) { idx = i; break; }
                }
                final int fIdx = idx;
                final String label = LauncherPreferences.VULKAN_DRIVERS.get(idx).label;
                if (!isAdded()) return;
                requireActivity().runOnUiThread(() -> {
                    spDriver.setSelection(fIdx);   // fires onItemSelected → persists the choice
                    btnRecommend.setEnabled(true);
                    android.widget.Toast.makeText(requireContext(),
                            getString(R.string.driver_recommend_result, gpu.displayName(), label),
                            android.widget.Toast.LENGTH_LONG).show();
                });
            }, "rd-gpu-detect").start();
        });

        // --- Gamepad button mapping (fix swapped/inverted controllers) ---
        view.findViewById(R.id.btn_gamepad_mapper).setOnClickListener(v ->
            startActivity(new android.content.Intent(requireContext(), com.rimdroid.GamepadMapperActivity.class)));
        Switch swSwapSticks = view.findViewById(R.id.sw_swap_sticks);
        swSwapSticks.setChecked(com.rimdroid.input.GamepadMapping.swapSticks(requireContext()));
        swSwapSticks.setOnCheckedChangeListener((b, checked) ->
                com.rimdroid.input.GamepadMapping.setSwapSticks(requireContext(), checked));

        // --- Render resolution (Video card): vertical radios, just the resolution text. Per-device
        // presets from a ~720p floor up to native. Lower = more FPS on weak GPUs + a bigger (blurrier)
        // UI; native is sharpest. The floor keeps render >= ~1280x768 (RimWorld UI minimum), per-device.
        // Applied at the next launch.
        android.widget.RadioGroup rgRes = view.findViewById(R.id.rg_render_res);
        android.graphics.Rect bounds =
                requireActivity().getWindowManager().getCurrentWindowMetrics().getBounds();
        final int sLong  = Math.max(bounds.width(), bounds.height());   // landscape width
        final int sShort = Math.min(bounds.width(), bounds.height());   // landscape height = native render height
        final int MIN = LauncherPreferences.minRenderScalePercent(sLong, sShort);
        final int MID = Math.max(MIN + 1, Math.min(99, (MIN + 100) / 2));
        java.util.LinkedHashSet<Integer> pctSet = new java.util.LinkedHashSet<>();
        pctSet.add(MIN); pctSet.add(MID); pctSet.add(100);
        final java.util.List<Integer> pcts = new java.util.ArrayList<>(pctSet);
        java.util.Collections.sort(pcts);   // ascending; displayed native (high) → floor (low)

        final int curPct = Math.max(MIN, inst.getRenderScalePercent());
        final boolean curFixed = inst.getFixedResMode() != com.rimdroid.InstanceSettings.FIXED_NONE;
        int nearestPct = pcts.get(0), nearestD = Integer.MAX_VALUE;   // relative preset closest to the stored %
        for (int p : pcts) { int d = Math.abs(p - curPct); if (d < nearestD) { nearestD = d; nearestPct = p; } }

        final int FIXED_TAG = -1;   // sentinel tag = the fixed 1366x768 letterboxed radio (not a percent)
        for (int i = pcts.size() - 1; i >= 0; i--) {   // native first, down to the floor
            int p = pcts.get(i);
            int W = Math.round(sLong * p / 100f), H = Math.round(sShort * p / 100f);
            android.widget.RadioButton rb = new android.widget.RadioButton(requireContext());
            rb.setId(View.generateViewId());
            rb.setText(W + "×" + H + " (" + getString(R.string.render_res_fullscreen) + ")");
            rb.setTag(p);
            rgRes.addView(rb);
            if (!curFixed && p == nearestPct) rb.setChecked(true);
        }
        android.widget.RadioButton rbFixed = new android.widget.RadioButton(requireContext());
        rbFixed.setId(View.generateViewId());
        rbFixed.setText("1366×768 (" + getString(R.string.render_res_black_margins) + ")");
        rbFixed.setTag(FIXED_TAG);
        rgRes.addView(rbFixed);
        if (curFixed) rbFixed.setChecked(true);

        rgRes.setOnCheckedChangeListener((group, checkedId) -> {   // set after the initial checks → no spurious writes
            View rb = group.findViewById(checkedId);
            if (rb == null || rb.getTag() == null) return;
            int tag = (Integer) rb.getTag();
            if (tag == FIXED_TAG) {
                inst.setFixedResMode(com.rimdroid.InstanceSettings.FIXED_720_16_9);
            } else {
                inst.setFixedResMode(com.rimdroid.InstanceSettings.FIXED_NONE);
                inst.setRenderScalePercent(tag);   // while fixed mode is off, GameActivity uses this %
            }
        });

        // Texture compression tier: No / Low / Ultra low (see InstanceSettings.getTexTier).
        // Takes effect on the next launch.
        android.widget.RadioGroup rgTexq = view.findViewById(R.id.rg_texq);
        switch (inst.getTexTier()) {
            case com.rimdroid.InstanceSettings.TEX_ULTRA: rgTexq.check(R.id.rb_texq_ultra); break;
            case com.rimdroid.InstanceSettings.TEX_LOW:   rgTexq.check(R.id.rb_texq_low);   break;
            default:                                     rgTexq.check(R.id.rb_texq_no);    break;
        }
        rgTexq.setOnCheckedChangeListener((group, checkedId) -> {
            int tier = (checkedId == R.id.rb_texq_ultra) ? com.rimdroid.InstanceSettings.TEX_ULTRA
                     : (checkedId == R.id.rb_texq_low)   ? com.rimdroid.InstanceSettings.TEX_LOW
                     : com.rimdroid.InstanceSettings.TEX_NONE;
            inst.setTexTier(tier);
        });

        // Text size (RimWorld 1.7 only): 100 / 120 / 130 / 140 %. Takes effect on the next launch.
        boolean waylith = engineInstance != null && com.rimdroid.game.DotnetEngine.isWaylith(engineInstance);
        view.findViewById(R.id.card_text_scale).setVisibility(waylith ? View.VISIBLE : View.GONE);
        Spinner spText = view.findViewById(R.id.sp_text_scale);
        final int[] textScales = com.rimdroid.InstanceSettings.TEXT_SCALES;
        String[] textLabels = new String[textScales.length];
        int textSel = 0;
        for (int i = 0; i < textScales.length; i++) {
            textLabels[i] = textScales[i] + " %";
            if (textScales[i] == inst.getTextScale()) textSel = i;
        }
        ArrayAdapter<String> textAdapter = new ArrayAdapter<>(requireContext(), android.R.layout.simple_spinner_item, textLabels);
        textAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spText.setAdapter(textAdapter);
        spText.setSelection(textSel);
        spText.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View v, int position, long id) {
                inst.setTextScale(textScales[position]);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {}
        });

        // FPS cap: three radio buttons — 30 / 60 / No limit (0 = off). Takes effect on next launch.
        android.widget.RadioGroup rgFps = view.findViewById(R.id.rg_fps_cap);
        int curCap = inst.getFpsCap();
        if (curCap == 30)      rgFps.check(R.id.rb_fps_30);
        else if (curCap == 60) rgFps.check(R.id.rb_fps_60);
        else                   rgFps.check(R.id.rb_fps_off);
        rgFps.setOnCheckedChangeListener((group, checkedId) -> {
            int cap = (checkedId == R.id.rb_fps_30) ? 30
                    : (checkedId == R.id.rb_fps_60) ? 60 : 0;
            inst.setFpsCap(cap);
        });
    }

    /**
     * What the engine choice decides below it: the native engine always renders with Vulkan directly,
     * so the renderer, the Vulkan driver and box64's compatibility mode are of no use to it and go
     * grey, with a note in their place; its own Vulkan compatibility setting shows instead.
     * RimWorld 1.7 is the same: its engine renders with Vulkan itself, whatever these say (a Mali
     * tester switched to MobileGlues to fix broken icons, 2026-10-17).
     */
    private void applyEngineState(View view, int engine) {
        boolean nativeEngine = engine == InstanceSettings.ENGINE_NATIVE && !waylithInstance;
        boolean vulkanOnly = nativeEngine || waylithInstance;
        RadioGroup rgRenderer = view.findViewById(R.id.rg_renderer);
        rendererLocked = true;
        if (vulkanOnly) rgRenderer.check(R.id.rb_zink_zfa);
        else rgRenderer.check(inst.getRenderer() == LauncherPreferences.Renderer.MOBILEGLUES
                ? R.id.rb_mobileglues : R.id.rb_zink_zfa);
        rendererLocked = false;
        for (int i = 0; i < rgRenderer.getChildCount(); i++) rgRenderer.getChildAt(i).setEnabled(!vulkanOnly);
        for (int id : new int[]{ R.id.spinner_vulkan_driver, R.id.btn_recommend_driver, R.id.sw_compat_mode }) {
            View v = view.findViewById(id);
            if (v != null) v.setEnabled(!vulkanOnly);
        }
        android.widget.TextView note = view.findViewById(R.id.tv_native_renderer_note);
        note.setText(waylithInstance ? R.string.waylith_renderer_note : R.string.engine_native_renderer_note);
        note.setVisibility(vulkanOnly ? View.VISIBLE : View.GONE);
        view.findViewById(R.id.ll_vk_compat).setVisibility(nativeEngine ? View.VISIBLE : View.GONE);
    }
}
