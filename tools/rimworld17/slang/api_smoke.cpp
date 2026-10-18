// Exercise the real session API, reflection and SPIR-V generation. This does not
// assert that a particular Waylith managed binding has the same ABI.
#include "slang.h"
#include "slang-com-ptr.h"
#include <cstdio>
#include <cstring>
#include <cstdint>

static void diagnostics(slang::IBlob* blob) {
    if (blob) std::fprintf(stderr, "%.*s", int(blob->getBufferSize()),
                          static_cast<const char*>(blob->getBufferPointer()));
}

int main() {
    if (std::strcmp(spGetBuildTagString(), "2026.19")) {
        std::fprintf(stderr, "Unexpected Slang build tag: %s\n", spGetBuildTagString());
        return 1;
    }
    SlangGlobalSessionDesc desc = {};
    desc.enableGLSL = true;
    Slang::ComPtr<slang::IGlobalSession> global;
    if (SLANG_FAILED(slang_createGlobalSession2(&desc, global.writeRef())) || !global) return 2;
    slang::TargetDesc target = {};
    target.format = SLANG_SPIRV;
    target.profile = global->findProfile("spirv_1_3");
    slang::SessionDesc sessionDesc = {};
    sessionDesc.targets = &target;
    sessionDesc.targetCount = 1;
    Slang::ComPtr<slang::ISession> session;
    if (SLANG_FAILED(global->createSession(sessionDesc, session.writeRef()))) return 3;
    Slang::ComPtr<slang::IBlob> diag;
    const char* source = R"(#version 450
layout(location=0) in vec3 position;
layout(location=0) out vec4 color;
layout(set=2,binding=0) uniform Material { vec4 tint; } material;
void main() { gl_Position=vec4(position,1); color=material.tint; }
)";
    auto module = session->loadModuleFromSourceString("probe", "probe.glsl", source, diag.writeRef());
    diagnostics(diag);
    if (!module) return 4;
    Slang::ComPtr<slang::IEntryPoint> entry;
    if (SLANG_FAILED(module->findAndCheckEntryPoint("main", SLANG_STAGE_VERTEX,
                                                  entry.writeRef(), diag.writeRef()))) {
        diagnostics(diag); return 5;
    }
    slang::IComponentType* components[] = {module, entry.get()};
    Slang::ComPtr<slang::IComponentType> program, linked;
    if (SLANG_FAILED(session->createCompositeComponentType(components, 2,
                program.writeRef(), diag.writeRef()))) { diagnostics(diag); return 6; }
    if (SLANG_FAILED(program->link(linked.writeRef(), diag.writeRef()))) { diagnostics(diag); return 7; }
    auto layout = linked->getLayout(0, diag.writeRef());
    if (!layout || layout->getEntryPointCount() != 1 || layout->getParameterCount() == 0) return 8;
    Slang::ComPtr<slang::IBlob> code;
    if (SLANG_FAILED(linked->getEntryPointCode(0, 0, code.writeRef(), diag.writeRef()))) {
        diagnostics(diag); return 9;
    }
    uint32_t magic = 0;
    if (!code || code->getBufferSize() < 20 || code->getBufferSize() % 4) return 10;
    std::memcpy(&magic, code->getBufferPointer(), sizeof(magic));
    if (magic != 0x07230203) return 11;
    // An implementation that simply returns success/null must also fail this test.
    diag.setNull();
    auto invalid = session->loadModuleFromSourceString("invalid", "invalid.glsl",
                  "#version 450\nvoid main() { this is invalid; }", diag.writeRef());
    if (invalid || !diag || !diag->getBufferSize()) return 12;
    std::printf("Slang %s: session, GLSL, reflection, %zu SPIR-V bytes, invalid-source rejection OK\n",
                spGetBuildTagString(), code->getBufferSize());
    return 0;
}
