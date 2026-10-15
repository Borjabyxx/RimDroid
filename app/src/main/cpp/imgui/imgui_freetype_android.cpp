// libimgui_freetype.so for RimWorld 1.7 on Android: the game asks ImGui's FreeType font loader
// (GetFontLoader, P/Invoke from RimWorld.dll). Until FreeType is built for Android, hand back Dear
// ImGui's own stb_truetype loader — same ImFontLoader interface, slightly different hinting.
//
// The game believes the loader is FreeType and pokes its per-font data: ImGuiHost.
// ForgetFreeTypeActivatedSizes writes a null pointer at offset 16 of every src->FontLoaderData
// (FreeType's BakedLastActivated, to force FT_Activate_Size). In stb_truetype's data that spot is
// stbtt_fontinfo.fontstart/numGlyphs; numGlyphs = 0 makes every glyph loaded afterwards empty, which
// left only the few letters drawn before the first reset. So the stb data is kept behind a small
// FreeType-shaped header whose offset 16 is free for the game to overwrite.
#include "imgui_brutal.h"
#include <stddef.h>

namespace {

struct Shim {
    void* ftFace;              // 0   FreeType: FT_Face
    int userFlags, loadFlags;  // 8   FreeType: UserFlags, LoadFlags
    void* bakedLastActivated;  // 16  FreeType: BakedLastActivated — written by the game
    void* stbData;             // 24  stb_truetype's own data
};
static_assert(offsetof(Shim, bakedLastActivated) == 16, "the game writes at offset 16");

const ImFontLoader* g_stb;

// Run an stb callback with src->FontLoaderData pointing at stb's own data.
struct Swap {
    ImFontConfig* src; void* saved;
    explicit Swap(ImFontConfig* s) : src(s), saved(s->FontLoaderData) {
        if (saved) src->FontLoaderData = static_cast<Shim*>(saved)->stbData;
    }
    ~Swap() { src->FontLoaderData = saved; }
};

bool SrcInit(ImFontAtlas* atlas, ImFontConfig* src) {
    if (!g_stb->FontSrcInit(atlas, src)) return false;
    Shim* shim = IM_NEW(Shim)();
    shim->stbData = src->FontLoaderData;
    src->FontLoaderData = shim;
    return true;
}
void SrcDestroy(ImFontAtlas* atlas, ImFontConfig* src) {
    Shim* shim = static_cast<Shim*>(src->FontLoaderData);
    if (shim) src->FontLoaderData = shim->stbData;
    if (g_stb->FontSrcDestroy) g_stb->FontSrcDestroy(atlas, src);
    if (shim) IM_DELETE(shim);
}
bool SrcContainsGlyph(ImFontAtlas* atlas, ImFontConfig* src, ImWchar c) {
    Swap s(src); return g_stb->FontSrcContainsGlyph(atlas, src, c);
}
bool BakedInit(ImFontAtlas* atlas, ImFontConfig* src, ImFontBaked* baked, void* data) {
    Swap s(src); return g_stb->FontBakedInit ? g_stb->FontBakedInit(atlas, src, baked, data) : true;
}
void BakedDestroy(ImFontAtlas* atlas, ImFontConfig* src, ImFontBaked* baked, void* data) {
    Swap s(src); if (g_stb->FontBakedDestroy) g_stb->FontBakedDestroy(atlas, src, baked, data);
}
bool BakedLoadGlyph(ImFontAtlas* atlas, ImFontConfig* src, ImFontBaked* baked, void* data, ImWchar c,
                    ImFontGlyph* out, float* adv) {
    Swap s(src); return g_stb->FontBakedLoadGlyph(atlas, src, baked, data, c, out, adv);
}

}  // namespace

extern "C" RDW const ImFontLoader* GetFontLoader() {
    static ImFontLoader loader;
    if (!g_stb) {
        g_stb = ImFontAtlasGetFontLoaderForStbTruetype();
        loader = *g_stb;
        loader.FontSrcInit = SrcInit;
        loader.FontSrcDestroy = SrcDestroy;
        loader.FontSrcContainsGlyph = g_stb->FontSrcContainsGlyph ? SrcContainsGlyph : nullptr;
        loader.FontBakedInit = BakedInit;
        loader.FontBakedDestroy = BakedDestroy;
        loader.FontBakedLoadGlyph = BakedLoadGlyph;
    }
    return &loader;
}
