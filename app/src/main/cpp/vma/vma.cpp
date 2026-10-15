// libvma.so for Android — Vulkan Memory Allocator 3.4.0 (AMD GPUOpen, MIT, vk_mem_alloc.h unmodified)
// with its C API exported, for RimWorld 1.7's engine (Brutal.Vulkan.Vma P/Invokes 73 vma* functions).
//
// The game's own VMA links the Vulkan loader's functions statically. Here every Vulkan function is
// fetched at run time (VMA_DYNAMIC_VULKAN_FUNCTIONS): the NDK's libvulkan stubs for our minSdk lack the
// Vulkan 1.2/1.3 entry points VMA wants. vmaCreateAllocator is wrapped to fill vkGetInstanceProcAddr /
// vkGetDeviceProcAddr when the caller did not pass them, which is what dynamic loading needs.
//
// Diagnosis (2026-10-14): the game's video memory reached 4.5 GB with a few big mods, against ~0.3 GB
// of compressed textures in its cache. The allocation entry points are wrapped to count what holds the
// memory: images by format and kind, buffers by use, VMA's blocks against what is allocated in them;
// a summary goes to logcat (RimDroid/vma) at most every 5 seconds while allocations happen.
#include <android/log.h>
#include <dlfcn.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <vulkan/vulkan.h>

#include <algorithm>
#include <map>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#define VMA_STATIC_VULKAN_FUNCTIONS 0
#define VMA_DYNAMIC_VULKAN_FUNCTIONS 1
// No asserts even in debug builds: after a lost device the engine tears the allocator down with
// allocations still in it, and VMA's assert then aborted the process on the way out ("... were not
// freed before destruction of this memory block!" failed, seen on her screen 2026-10-14).
#define VMA_ASSERT(expr)
#define VMA_CALL_PRE __attribute__((visibility("default")))
#define vmaCreateAllocator vmaCreateAllocator_impl
#define vmaCreateImage vmaCreateImage_impl
#define vmaCreateDedicatedImage vmaCreateDedicatedImage_impl
#define vmaAllocateMemoryForImage vmaAllocateMemoryForImage_impl
#define vmaCreateBuffer vmaCreateBuffer_impl
#define vmaCreateBufferWithAlignment vmaCreateBufferWithAlignment_impl
#define vmaCreateDedicatedBuffer vmaCreateDedicatedBuffer_impl
#define vmaAllocateMemoryForBuffer vmaAllocateMemoryForBuffer_impl
#define vmaAllocateMemory vmaAllocateMemory_impl
#define vmaAllocateDedicatedMemory vmaAllocateDedicatedMemory_impl
#define vmaDestroyImage vmaDestroyImage_impl
#define vmaDestroyBuffer vmaDestroyBuffer_impl
#define vmaFreeMemory vmaFreeMemory_impl
#define VMA_IMPLEMENTATION
#include "vk_mem_alloc.h"
#undef vmaCreateAllocator
#undef vmaCreateImage
#undef vmaCreateDedicatedImage
#undef vmaAllocateMemoryForImage
#undef vmaCreateBuffer
#undef vmaCreateBufferWithAlignment
#undef vmaCreateDedicatedBuffer
#undef vmaAllocateMemoryForBuffer
#undef vmaAllocateMemory
#undef vmaAllocateDedicatedMemory
#undef vmaDestroyImage
#undef vmaDestroyBuffer
#undef vmaFreeMemory

#define API extern "C" __attribute__((visibility("default")))
#define TAG "RimDroid/vma"

API VkResult vmaCreateAllocator(const VmaAllocatorCreateInfo* pCreateInfo, VmaAllocator* pAllocator) {
    VmaAllocatorCreateInfo info = *pCreateInfo;
    VmaVulkanFunctions fns = {};
    // Copy only the members the game's VMA has. 3.4.0 appends vkGetPhysicalDeviceProperties2KHR after
    // vkGetMemoryWin32HandleKHR; the game's struct ends before it (27 members). Copying the whole
    // struct read one pointer past the game's table — garbage that VMA then called (RimWorld 1.7.4977,
    // the first build that passes its own table: SIGBUS at 0x100000009). The rest is fetched dynamically.
    size_t gameSize = sizeof(VmaVulkanFunctions);
#if VMA_GET_PHYSICAL_DEVICE_PROPERTIES2
    gameSize = offsetof(VmaVulkanFunctions, vkGetPhysicalDeviceProperties2KHR);
#endif
    if (info.pVulkanFunctions) memcpy(&fns, info.pVulkanFunctions, gameSize);
    if (!fns.vkGetInstanceProcAddr || !fns.vkGetDeviceProcAddr) {
        static void* vk = dlopen("libvulkan.so", RTLD_NOW | RTLD_LOCAL);
        if (vk) {
            if (!fns.vkGetInstanceProcAddr)
                fns.vkGetInstanceProcAddr = (PFN_vkGetInstanceProcAddr)dlsym(vk, "vkGetInstanceProcAddr");
            if (!fns.vkGetDeviceProcAddr)
                fns.vkGetDeviceProcAddr = (PFN_vkGetDeviceProcAddr)dlsym(vk, "vkGetDeviceProcAddr");
        }
    }
    info.pVulkanFunctions = &fns;
    return vmaCreateAllocator_impl(&info, pAllocator);
}

// ---- accounting -------------------------------------------------------------------------------------

namespace {

enum Kind { IMAGE, IMAGE_MEMORY, BUFFER, BUFFER_MEMORY, RAW, KINDS };
const char* const KIND_NAMES[KINDS] = { "images", "memory for images", "buffers", "memory for buffers", "raw memory" };

struct Rec {
    Kind kind;
    VkDeviceSize bytes;
    VkFormat format;
    uint32_t width, height, mips, layers, usage;
};

std::mutex g_mu;
std::unordered_map<VmaAllocation, Rec> g_live;
VmaAllocator g_allocator;
uint64_t g_lastLogMs;
VkDeviceSize g_peak;
int g_bigLogged;

uint64_t nowMs() {
    timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

const char* formatName(VkFormat f) {
    switch (f) {
        case VK_FORMAT_R8_UNORM: return "R8";
        case VK_FORMAT_R8G8_UNORM: return "RG8";
        case VK_FORMAT_R8G8B8A8_UNORM: return "RGBA8";
        case VK_FORMAT_R8G8B8A8_SRGB: return "RGBA8_SRGB";
        case VK_FORMAT_B8G8R8A8_UNORM: return "BGRA8";
        case VK_FORMAT_B8G8R8A8_SRGB: return "BGRA8_SRGB";
        case VK_FORMAT_R16G16B16A16_SFLOAT: return "RGBA16F";
        case VK_FORMAT_R32G32B32A32_SFLOAT: return "RGBA32F";
        case VK_FORMAT_R32_SFLOAT: return "R32F";
        case VK_FORMAT_D32_SFLOAT: return "D32";
        case VK_FORMAT_D24_UNORM_S8_UINT: return "D24S8";
        case VK_FORMAT_D32_SFLOAT_S8_UINT: return "D32S8";
        case VK_FORMAT_BC1_RGB_UNORM_BLOCK: case VK_FORMAT_BC1_RGBA_UNORM_BLOCK: return "BC1";
        case VK_FORMAT_BC1_RGB_SRGB_BLOCK: case VK_FORMAT_BC1_RGBA_SRGB_BLOCK: return "BC1_SRGB";
        case VK_FORMAT_BC3_UNORM_BLOCK: return "BC3";
        case VK_FORMAT_BC3_SRGB_BLOCK: return "BC3_SRGB";
        case VK_FORMAT_BC4_UNORM_BLOCK: return "BC4";
        case VK_FORMAT_BC5_UNORM_BLOCK: return "BC5";
        case VK_FORMAT_BC7_UNORM_BLOCK: return "BC7";
        case VK_FORMAT_BC7_SRGB_BLOCK: return "BC7_SRGB";
        default: return nullptr;
    }
}

// What an image is for: drawn into, written by compute, or only sampled.
const char* imageRole(uint32_t usage) {
    if (usage & (VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT)) return "target";
    if (usage & VK_IMAGE_USAGE_STORAGE_BIT) return "storage";
    return "texture";
}

const char* bufferRole(uint32_t usage) {
    if (usage & (VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT)) return "vertex/index";
    if (usage & VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT) return "uniform";
    if (usage & VK_BUFFER_USAGE_STORAGE_BUFFER_BIT) return "storage";
    if (usage & VK_BUFFER_USAGE_TRANSFER_SRC_BIT) return "staging (upload)";
    if (usage & VK_BUFFER_USAGE_TRANSFER_DST_BIT) return "readback";
    return "other";
}

unsigned mb(VkDeviceSize b) { return (unsigned)((b + (1 << 19)) >> 20); }

// Caller holds g_mu.
void summary(bool force) {
    uint64_t now = nowMs();
    if (!force && now - g_lastLogMs < 5000) return;
    g_lastLogMs = now;
    VkDeviceSize kind[KINDS] = {}, total = 0;
    unsigned kindCount[KINDS] = {};
    struct Group { VkDeviceSize bytes; unsigned count, big; };
    std::map<std::string, Group> images, buffers;
    for (const auto& e : g_live) {
        const Rec& r = e.second;
        kind[r.kind] += r.bytes;
        kindCount[r.kind]++;
        total += r.bytes;
        char key[96];
        if (r.kind == IMAGE) {
            const char* n = formatName(r.format);
            if (n) snprintf(key, sizeof key, "%s %s", n, imageRole(r.usage));
            else snprintf(key, sizeof key, "fmt%d %s", (int)r.format, imageRole(r.usage));
            Group& g = images[key];
            g.bytes += r.bytes; g.count++;
            if (r.width >= 2048 || r.height >= 2048) g.big++;
        } else if (r.kind == BUFFER) {
            Group& g = buffers[bufferRole(r.usage)];
            g.bytes += r.bytes; g.count++;
        }
    }
    if (total > g_peak) g_peak = total;
    VkDeviceSize blocks = 0, inBlocks = 0;
    if (g_allocator) {
        VmaTotalStatistics st;
        vmaCalculateStatistics(g_allocator, &st);
        blocks = st.total.statistics.blockBytes;
        inBlocks = st.total.statistics.allocationBytes;
    }
    __android_log_print(ANDROID_LOG_INFO, TAG,
        "live %u MB (peak %u MB); VMA blocks %u MB holding %u MB | images %u MB (%u), memory for images %u MB (%u), "
        "buffers %u MB (%u), memory for buffers %u MB (%u), raw %u MB (%u)",
        mb(total), mb(g_peak), mb(blocks), mb(inBlocks), mb(kind[IMAGE]), kindCount[IMAGE],
        mb(kind[IMAGE_MEMORY]), kindCount[IMAGE_MEMORY], mb(kind[BUFFER]), kindCount[BUFFER],
        mb(kind[BUFFER_MEMORY]), kindCount[BUFFER_MEMORY], mb(kind[RAW]), kindCount[RAW]);
    std::vector<std::pair<VkDeviceSize, std::string>> order;
    for (const auto& g : images) order.push_back({ g.second.bytes, g.first });
    std::sort(order.rbegin(), order.rend());
    for (size_t i = 0; i < order.size() && i < 8; i++) {
        const Group& g = images[order[i].second];
        __android_log_print(ANDROID_LOG_INFO, TAG, "  images %-20s %5u MB  %5u images, %u of them 2048+",
                            order[i].second.c_str(), mb(g.bytes), g.count, g.big);
    }
    for (const auto& g : buffers)
        __android_log_print(ANDROID_LOG_INFO, TAG, "  buffers %-19s %5u MB  %5u buffers", g.first.c_str(), mb(g.second.bytes), g.second.count);
}

void track(VmaAllocator allocator, VmaAllocation a, Kind kind, const VkImageCreateInfo* image, uint32_t bufferUsage) {
    if (!a) return;
    VmaAllocationInfo info;
    vmaGetAllocationInfo(allocator, a, &info);
    Rec r = {};
    r.kind = kind;
    r.bytes = info.size;
    if (image) {
        r.format = image->format;
        r.width = image->extent.width;
        r.height = image->extent.height;
        r.mips = image->mipLevels;
        r.layers = image->arrayLayers;
        r.usage = image->usage;
    } else {
        r.usage = bufferUsage;
    }
    std::lock_guard<std::mutex> lock(g_mu);
    g_allocator = allocator;
    g_live[a] = r;
    if (image && (r.width >= 4096 || r.height >= 4096) && g_bigLogged < 60) {
        g_bigLogged++;
        const char* n = formatName(r.format);
        __android_log_print(ANDROID_LOG_INFO, TAG, "big image %ux%u x%u layers, %u mips, %s (%d) %s, %u MB",
                            r.width, r.height, r.layers, r.mips, n ? n : "fmt", (int)r.format, imageRole(r.usage), mb(r.bytes));
    }
    summary(false);
}

void untrack(VmaAllocation a) {
    if (!a) return;
    std::lock_guard<std::mutex> lock(g_mu);
    g_live.erase(a);
    summary(false);
}

}  // namespace

API VkResult vmaCreateImage(VmaAllocator allocator, const VkImageCreateInfo* pImageCreateInfo,
                            const VmaAllocationCreateInfo* pAllocationCreateInfo, VkImage* pImage,
                            VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaCreateImage_impl(allocator, pImageCreateInfo, pAllocationCreateInfo, pImage, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, IMAGE, pImageCreateInfo, 0);
    return r;
}

API VkResult vmaCreateDedicatedImage(VmaAllocator allocator, const VkImageCreateInfo* pImageCreateInfo,
                                     const VmaAllocationCreateInfo* pAllocationCreateInfo, void* pMemoryAllocateNext,
                                     VkImage* pImage, VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaCreateDedicatedImage_impl(allocator, pImageCreateInfo, pAllocationCreateInfo, pMemoryAllocateNext,
                                              pImage, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, IMAGE, pImageCreateInfo, 0);
    return r;
}

API VkResult vmaAllocateMemoryForImage(VmaAllocator allocator, VkImage image, const VmaAllocationCreateInfo* pCreateInfo,
                                       VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaAllocateMemoryForImage_impl(allocator, image, pCreateInfo, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, IMAGE_MEMORY, nullptr, 0);
    return r;
}

API VkResult vmaCreateBuffer(VmaAllocator allocator, const VkBufferCreateInfo* pBufferCreateInfo,
                             const VmaAllocationCreateInfo* pAllocationCreateInfo, VkBuffer* pBuffer,
                             VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaCreateBuffer_impl(allocator, pBufferCreateInfo, pAllocationCreateInfo, pBuffer, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, BUFFER, nullptr, pBufferCreateInfo->usage);
    return r;
}

API VkResult vmaCreateBufferWithAlignment(VmaAllocator allocator, const VkBufferCreateInfo* pBufferCreateInfo,
                                          const VmaAllocationCreateInfo* pAllocationCreateInfo, VkDeviceSize minAlignment,
                                          VkBuffer* pBuffer, VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaCreateBufferWithAlignment_impl(allocator, pBufferCreateInfo, pAllocationCreateInfo, minAlignment,
                                                   pBuffer, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, BUFFER, nullptr, pBufferCreateInfo->usage);
    return r;
}

API VkResult vmaCreateDedicatedBuffer(VmaAllocator allocator, const VkBufferCreateInfo* pBufferCreateInfo,
                                      const VmaAllocationCreateInfo* pAllocationCreateInfo, void* pMemoryAllocateNext,
                                      VkBuffer* pBuffer, VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaCreateDedicatedBuffer_impl(allocator, pBufferCreateInfo, pAllocationCreateInfo, pMemoryAllocateNext,
                                               pBuffer, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, BUFFER, nullptr, pBufferCreateInfo->usage);
    return r;
}

API VkResult vmaAllocateMemoryForBuffer(VmaAllocator allocator, VkBuffer buffer, const VmaAllocationCreateInfo* pCreateInfo,
                                        VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaAllocateMemoryForBuffer_impl(allocator, buffer, pCreateInfo, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, BUFFER_MEMORY, nullptr, 0);
    return r;
}

API VkResult vmaAllocateMemory(VmaAllocator allocator, const VkMemoryRequirements* pVkMemoryRequirements,
                               const VmaAllocationCreateInfo* pCreateInfo, VmaAllocation* pAllocation,
                               VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaAllocateMemory_impl(allocator, pVkMemoryRequirements, pCreateInfo, pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, RAW, nullptr, 0);
    return r;
}

API VkResult vmaAllocateDedicatedMemory(VmaAllocator allocator, const VkMemoryRequirements* pVkMemoryRequirements,
                                        const VmaAllocationCreateInfo* pCreateInfo, void* pMemoryAllocateNext,
                                        VmaAllocation* pAllocation, VmaAllocationInfo* pAllocationInfo) {
    VkResult r = vmaAllocateDedicatedMemory_impl(allocator, pVkMemoryRequirements, pCreateInfo, pMemoryAllocateNext,
                                                 pAllocation, pAllocationInfo);
    if (r == VK_SUCCESS) track(allocator, *pAllocation, RAW, nullptr, 0);
    return r;
}

API void vmaDestroyImage(VmaAllocator allocator, VkImage image, VmaAllocation allocation) {
    untrack(allocation);
    vmaDestroyImage_impl(allocator, image, allocation);
}

API void vmaDestroyBuffer(VmaAllocator allocator, VkBuffer buffer, VmaAllocation allocation) {
    untrack(allocation);
    vmaDestroyBuffer_impl(allocator, buffer, allocation);
}

API void vmaFreeMemory(VmaAllocator allocator, VmaAllocation allocation) {
    untrack(allocation);
    vmaFreeMemory_impl(allocator, allocation);
}
