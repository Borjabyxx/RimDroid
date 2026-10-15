// libvulkan.so in the APK (RimWorld 1.7): forwards everything to the system Vulkan loader, except that
// it hides the display's rotation from the game. A landscape app on a phone whose screen is natively
// portrait gets a surface with currentTransform ROTATE_90/270. Waylith takes that transform for its
// swapchain without rotating what it draws, so the picture came out turned by 90 degrees. Here the
// surface reports IDENTITY and every swapchain is created with preTransform IDENTITY; Android's
// compositor does the rotation.
//
// .NET finds this file first (P/Invoke "vulkan" probes the app's native library folder), and the
// engine fetches every other function through the vkGetInstanceProcAddr exported here.
#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdio.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <vulkan/vulkan.h>

#define API __attribute__((visibility("default")))
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, "RimDroid/vkwrap", __VA_ARGS__)

static void* g_real;
static PFN_vkGetInstanceProcAddr g_gipa;
static PFN_vkGetDeviceProcAddr g_gdpa;
static PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR g_caps;
static PFN_vkCreateSwapchainKHR g_swapchain;

static void* real(const char* name) {
    if (!g_real) {
        g_real = dlopen("/system/lib64/libvulkan.so", RTLD_NOW | RTLD_LOCAL);
        if (!g_real) g_real = dlopen("libvulkan.so", RTLD_NOW | RTLD_LOCAL);   // never ourselves: different path
    }
    return g_real ? dlsym(g_real, name) : NULL;
}

// Android reports currentExtent already in the window's (landscape) size; only the transform says the
// display is turned. Reporting IDENTITY keeps the engine from passing ROTATE_90 as preTransform.
static VkResult VKAPI_CALL caps_wrap(VkPhysicalDevice pd, VkSurfaceKHR surface, VkSurfaceCapabilitiesKHR* caps) {
    VkResult r = g_caps(pd, surface, caps);
    if (r != VK_SUCCESS) return r;
    caps->currentTransform = VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR;
    caps->supportedTransforms |= VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR;
    return r;
}

// With preTransform != the display's transform Android answers VK_SUBOPTIMAL_KHR on every frame, and
// the engine then rebuilt its swapchain every frame. The image is still shown correctly (the
// compositor rotates it), so SUBOPTIMAL is reported as SUCCESS.
static PFN_vkQueuePresentKHR g_present;
static PFN_vkAcquireNextImageKHR g_acquire;
// libglfw.so (glfw_android.c): one tick per presented frame, read by the FPS overlay.
extern void rd_frame_tick(void) __attribute__((weak));

static VkResult VKAPI_CALL present_wrap(VkQueue q, const VkPresentInfoKHR* info) {
    VkResult r = g_present(q, info);
    if (rd_frame_tick) rd_frame_tick();
    if (info->pResults) for (uint32_t i = 0; i < info->swapchainCount; i++)
        if (info->pResults[i] == VK_SUBOPTIMAL_KHR) info->pResults[i] = VK_SUCCESS;
    return r == VK_SUBOPTIMAL_KHR ? VK_SUCCESS : r;
}
static VkResult VKAPI_CALL acquire_wrap(VkDevice d, VkSwapchainKHR sc, uint64_t timeout, VkSemaphore sem, VkFence f, uint32_t* idx) {
    VkResult r = g_acquire(d, sc, timeout, sem, f, idx);
    return r == VK_SUBOPTIMAL_KHR ? VK_SUCCESS : r;
}

// Hooks shared by the instance and device lookups.
static PFN_vkVoidFunction hook(const char* name, PFN_vkVoidFunction f);

// ---- "no BC" mode: pretend to be a GPU without BC texture formats ----------------------------------
// Mali and PowerVR phones have no BC1/BC7, and RimWorld 1.7's engine encodes only those. What it does
// without them (uncompressed textures? a crash?) can be seen on an Adreno phone by hiding BC here:
// features say no textureCompressionBC, the BC formats report no features, image-format queries and
// image creation for them fail. On when the file rd_no_bc exists in the game folder ($HOME, set by
// ClrActivity) or RIMDROID_VK_NO_BC=1.
static int g_noBc = -1;
static PFN_vkGetPhysicalDeviceFeatures g_getFeatures;
static PFN_vkGetPhysicalDeviceFeatures2 g_getFeatures2;
static PFN_vkGetPhysicalDeviceFormatProperties g_getFormatProps;
static PFN_vkGetPhysicalDeviceFormatProperties2 g_getFormatProps2;
static PFN_vkGetPhysicalDeviceImageFormatProperties g_getImageFormatProps;
static PFN_vkGetPhysicalDeviceImageFormatProperties2 g_getImageFormatProps2;
static unsigned g_bcImagesRefused;

static int no_bc(void) {
    if (g_noBc < 0) {
        const char* e = getenv("RIMDROID_VK_NO_BC");
        const char* home = getenv("HOME");
        char path[512];
        snprintf(path, sizeof path, "%s/rd_no_bc", home ? home : "");
        g_noBc = (e && e[0] == '1') || (home && access(path, F_OK) == 0);
        if (g_noBc) __android_log_print(ANDROID_LOG_WARN, "RimDroid/vkmem", "NO-BC MODE: BC texture formats hidden from the game (pretending to be a Mali/PowerVR GPU)");
    }
    return g_noBc;
}

static int is_bc(VkFormat f) { return f >= VK_FORMAT_BC1_RGB_UNORM_BLOCK && f <= VK_FORMAT_BC7_SRGB_BLOCK; }

static void VKAPI_CALL getfeatures_wrap(VkPhysicalDevice pd, VkPhysicalDeviceFeatures* f) {
    g_getFeatures(pd, f);
    if (no_bc()) f->textureCompressionBC = VK_FALSE;
}

static void VKAPI_CALL getfeatures2_wrap(VkPhysicalDevice pd, VkPhysicalDeviceFeatures2* f) {
    g_getFeatures2(pd, f);
    if (no_bc()) f->features.textureCompressionBC = VK_FALSE;
}

static void VKAPI_CALL getformatprops_wrap(VkPhysicalDevice pd, VkFormat format, VkFormatProperties* p) {
    g_getFormatProps(pd, format, p);
    if (no_bc() && is_bc(format)) { p->linearTilingFeatures = 0; p->optimalTilingFeatures = 0; p->bufferFeatures = 0; }
}

static void VKAPI_CALL getformatprops2_wrap(VkPhysicalDevice pd, VkFormat format, VkFormatProperties2* p) {
    g_getFormatProps2(pd, format, p);
    if (no_bc() && is_bc(format)) { p->formatProperties.linearTilingFeatures = 0; p->formatProperties.optimalTilingFeatures = 0; p->formatProperties.bufferFeatures = 0; }
}

static VkResult VKAPI_CALL getimageformatprops_wrap(VkPhysicalDevice pd, VkFormat format, VkImageType type, VkImageTiling tiling,
                                                    VkImageUsageFlags usage, VkImageCreateFlags flags, VkImageFormatProperties* p) {
    if (no_bc() && is_bc(format)) return VK_ERROR_FORMAT_NOT_SUPPORTED;
    return g_getImageFormatProps(pd, format, type, tiling, usage, flags, p);
}

static VkResult VKAPI_CALL getimageformatprops2_wrap(VkPhysicalDevice pd, const VkPhysicalDeviceImageFormatInfo2* info, VkImageFormatProperties2* p) {
    if (no_bc() && is_bc(info->format)) return VK_ERROR_FORMAT_NOT_SUPPORTED;
    return g_getImageFormatProps2(pd, info, p);
}

// ---- memory diagnosis (2026-10-14) ------------------------------------------------------------------
// The system counted 4.5 GB of GPU memory for the game, VMA (libvma.so) only 2.1 GB of it, and 2.6 GB
// were there before VMA held 0.4 GB. Counted here: every vkAllocateMemory (VMA's blocks and anything
// the engine allocates itself), descriptor pools as they are asked for, pipelines. RimDroid/vkmem.
#define MLOG(...) __android_log_print(ANDROID_LOG_INFO, "RimDroid/vkmem", __VA_ARGS__)
#define SLOTS 65536
#define TOMB (~0ULL)
static PFN_vkAllocateMemory g_allocMem;
static PFN_vkFreeMemory g_freeMem;
static PFN_vkCreateDescriptorPool g_descPool;
static PFN_vkCreateGraphicsPipelines g_gfxPipes;
static PFN_vkCreateComputePipelines g_compPipes;
static pthread_mutex_t g_mu = PTHREAD_MUTEX_INITIALIZER;
static struct { uint64_t mem; VkDeviceSize size; } g_mem[SLOTS];
static VkDeviceSize g_live, g_peak, g_largest;
static unsigned g_count, g_pipelines, g_pools;
static uint64_t g_lastLog;

static unsigned mb(VkDeviceSize b) { return (unsigned)((b + (1 << 19)) >> 20); }
static void cmd_summary(void);
static void pools_summary(void);
static void ring_add(const char* fmt, ...);
static unsigned g_nullWrites;
static void target_remember(VkImage img, uint32_t w, uint32_t h);
static uint64_t kgsl_mapped_bytes(void);

// Texture shrink state (see the texture shrink section below): an open-addressing hash set keyed by
// the image handle. A texture is created shrunk only once a slot is reserved for it: an untracked
// shrunk texture takes full-size copies past its edges (Ultra low on 1.7, 2026-10-14: thousands of
// 512+ textures overflowed the first, 512-entry table and the driver crashed in vkCmdCopyBufferToImage).
#define SHRUNK 65536                                   // power of two
#define SHRUNK_LIMIT (SHRUNK / 4 * 3)
#define SHRUNK_TOMB ((VkImage)(uintptr_t)~(uintptr_t)0)
typedef struct { VkImage image; uint32_t origMips, newMips; } ShrunkImage;
static pthread_mutex_t g_smu = PTHREAD_MUTEX_INITIALIZER;
static ShrunkImage g_shrunk[SHRUNK], g_shrunkSpare[SHRUNK];
static int g_shrunkUsed;          // set once anything was shrunk: the fast path for everything else
static unsigned g_shrunkLive, g_shrunkReserved, g_shrunkTombs;
static int g_shrinkOn = -1;       // -1: not read yet
static unsigned g_shrinkMin = 2048, g_shrinkCount, g_shrinkReadbacks;
static VkDeviceSize g_shrinkSavedApprox;

static void mem_summary(void) {   // g_mu held
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    uint64_t now = (uint64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
    if (now - g_lastLog < 5000) return;
    g_lastLog = now;
    MLOG("device memory live %u MB in %u allocations (peak %u MB, largest %u MB); %u pipelines, %u descriptor pools; "
         "kgsl mapped into the process %u MB", mb(g_live), g_count, mb(g_peak), mb(g_largest), g_pipelines, g_pools,
         mb(kgsl_mapped_bytes()));
    cmd_summary();
    pools_summary();
    if (g_shrinkCount) MLOG("textures shrunk: %u, ~%u MB saved, %u readbacks of a dropped level", g_shrinkCount, mb(g_shrinkSavedApprox), g_shrinkReadbacks);
    if (g_nullWrites) MLOG("descriptor writes with a null handle so far: %u", g_nullWrites);
}

static VkResult VKAPI_CALL allocmem_wrap(VkDevice d, const VkMemoryAllocateInfo* info, const VkAllocationCallbacks* a, VkDeviceMemory* out) {
    VkResult r = g_allocMem(d, info, a, out);
    if (r != VK_SUCCESS) {
        MLOG("vkAllocateMemory %u MB (type %u) failed: %d", mb(info->allocationSize), info->memoryTypeIndex, r);
        return r;
    }
    uint64_t key = (uint64_t)*out;
    pthread_mutex_lock(&g_mu);
    for (unsigned i = (unsigned)(key * 0x9E3779B97F4A7C15ULL >> 48), n = 0; n < SLOTS; i = (i + 1) & (SLOTS - 1), n++)
        if (g_mem[i].mem == 0 || g_mem[i].mem == TOMB) { g_mem[i].mem = key; g_mem[i].size = info->allocationSize; break; }
    g_live += info->allocationSize;
    g_count++;
    if (g_live > g_peak) g_peak = g_live;
    if (info->allocationSize > g_largest) g_largest = info->allocationSize;
    if (info->allocationSize >= (64u << 20)) MLOG("vkAllocateMemory %u MB (type %u)", mb(info->allocationSize), info->memoryTypeIndex);
    mem_summary();
    pthread_mutex_unlock(&g_mu);
    return r;
}

static void VKAPI_CALL freemem_wrap(VkDevice d, VkDeviceMemory m, const VkAllocationCallbacks* a) {
    uint64_t key = (uint64_t)m;
    if (key) {
        pthread_mutex_lock(&g_mu);
        for (unsigned i = (unsigned)(key * 0x9E3779B97F4A7C15ULL >> 48), n = 0; n < SLOTS && g_mem[i].mem; i = (i + 1) & (SLOTS - 1), n++)
            if (g_mem[i].mem == key) { g_live -= g_mem[i].size; g_count--; g_mem[i].mem = TOMB; break; }
        mem_summary();
        pthread_mutex_unlock(&g_mu);
    }
    g_freeMem(d, m, a);
}

// The GPU memory the kernel has mapped into this process (every kgsl mapping in /proc/self/maps):
// the driver's own allocations included, which no Vulkan call reports. Read around descriptor pool
// creation to see what each pool really costs (a reviewer's lead: five pools reserving 3.4 million
// descriptors; the process map showed five 360 MB mappings).
static uint64_t kgsl_mapped_bytes(void) {
    FILE* f = fopen("/proc/self/maps", "r");
    if (!f) return 0;
    char line[512];
    uint64_t total = 0;
    while (fgets(line, sizeof line, f)) {
        if (!strstr(line, "kgsl")) continue;
        unsigned long long lo, hi;
        if (sscanf(line, "%llx-%llx", &lo, &hi) == 2 && hi > lo) total += hi - lo;
    }
    fclose(f);
    return total;
}

// Measured 2026-10-14 (Adreno 830): the driver backs a descriptor pool with GPU memory for its whole
// declared capacity, ~23 KB per set. RimWorld 1.7's MeshBatchRenderer reserves five pools of 16384
// sets at startup (+375 MB each, 1.9 GB) and more of 2048 sets as it goes (+48 MB each): ~2.5 GB of
// the game's 4.5 GB of GPU memory sat in empty tables, which on a phone is RAM. Pools of 16384 sets
// are created with their capacity divided by RIMDROID_VK_POOL_DIVISOR (default 8, i.e. 2048 sets,
// the size the engine's later pools use; 1 = as declared). The sets each pool hands out are counted
// so that a pool running out (VK_ERROR_OUT_OF_POOL_MEMORY, which the engine's material set cache is
// said to catch and fall back from) is visible in the log, with its high-water mark.
#define POOLS 128
static PFN_vkAllocateDescriptorSets g_allocSets;
static PFN_vkFreeDescriptorSets g_freeSets;
static PFN_vkResetDescriptorPool g_resetDescPool;
static PFN_vkDestroyDescriptorPool g_destroyDescPool;
static struct { VkDescriptorPool pool; unsigned id, maxSets, live, peak, failed; } g_poolTable[POOLS];
static unsigned g_poolDivisor = 8;

static int pool_index(VkDescriptorPool p) {   // g_mu held
    for (int i = 0; i < POOLS; i++) if (g_poolTable[i].pool == p) return i;
    return -1;
}

static VkResult VKAPI_CALL descpool_wrap(VkDevice d, const VkDescriptorPoolCreateInfo* info, const VkAllocationCallbacks* a, VkDescriptorPool* out) {
    static int divisorRead;
    if (!divisorRead) {
        divisorRead = 1;
        const char* e = getenv("RIMDROID_VK_POOL_DIVISOR");
        if (e && atoi(e) >= 1) g_poolDivisor = (unsigned)atoi(e);
    }
    VkDescriptorPoolCreateInfo copy = *info;
    VkDescriptorPoolSize sizesCopy[32];
    // 16384-set pools: /8 (2048 sets, peak use seen 332); the engine's own 2048-set pools: /2 (peak 45).
    // Only those round capacities are reservations made up front. A pool sized to an exact count, like
    // the texture encoder's (one set per texture of a batch, e.g. 2724), needs every set: one short,
    // the engine drops the whole batch to its slower CPU encoder (13 times on a fresh 1.7 instance,
    // 2026-10-15). Those stay as declared; they live about a second.
    int reservation = (info->maxSets & (info->maxSets - 1)) == 0;
    unsigned divisor = g_poolDivisor > 1 && reservation && info->poolSizeCount <= 32
            ? (info->maxSets >= 16384 ? g_poolDivisor : info->maxSets >= 2048 ? 2 : 1) : 1;
    if (divisor > 1) {
        copy.maxSets = info->maxSets / divisor;
        for (uint32_t i = 0; i < info->poolSizeCount; i++) {
            sizesCopy[i] = info->pPoolSizes[i];
            sizesCopy[i].descriptorCount = info->pPoolSizes[i].descriptorCount / divisor;
            if (sizesCopy[i].descriptorCount == 0) sizesCopy[i].descriptorCount = 1;
        }
        copy.pPoolSizes = sizesCopy;
    }
    uint64_t before = kgsl_mapped_bytes();
    VkResult r = g_descPool(d, &copy, a, out);
    uint64_t after = kgsl_mapped_bytes();
    pthread_mutex_lock(&g_mu);
    g_pools++;
    if (r == VK_SUCCESS) {
        int i = pool_index(VK_NULL_HANDLE);
        if (i >= 0) { g_poolTable[i].pool = *out; g_poolTable[i].id = g_pools; g_poolTable[i].maxSets = copy.maxSets; g_poolTable[i].live = g_poolTable[i].peak = g_poolTable[i].failed = 0; }
    }
    char sizes[512];
    int len = 0;
    sizes[0] = 0;
    for (uint32_t i = 0; i < copy.poolSizeCount && len < (int)sizeof sizes - 32; i++)
        len += snprintf(sizes + len, sizeof sizes - len, " type%d:%u", copy.pPoolSizes[i].type, copy.pPoolSizes[i].descriptorCount);
    MLOG("descriptor pool #%u: maxSets %u (declared /%u), flags 0x%x,%s -> result %d, GPU memory of the process %u -> %u MB (+%u MB)",
         g_pools, copy.maxSets, divisor, copy.flags, sizes, r, mb(before), mb(after), mb(after > before ? after - before : 0));
    pthread_mutex_unlock(&g_mu);
    return r;
}

static VkResult VKAPI_CALL allocsets_wrap(VkDevice d, const VkDescriptorSetAllocateInfo* info, VkDescriptorSet* out) {
    VkResult r = g_allocSets(d, info, out);
    pthread_mutex_lock(&g_mu);
    mem_summary();
    int i = pool_index(info->descriptorPool);
    if (i >= 0) {
        if (r == VK_SUCCESS) {
            g_poolTable[i].live += info->descriptorSetCount;
            if (g_poolTable[i].live > g_poolTable[i].peak) g_poolTable[i].peak = g_poolTable[i].live;
        } else if (g_poolTable[i].failed++ < 20) {
            MLOG("descriptor pool #%u OUT OF SETS: result %d asking %u, live %u of %u (peak %u)", g_poolTable[i].id, r,
                 info->descriptorSetCount, g_poolTable[i].live, g_poolTable[i].maxSets, g_poolTable[i].peak);
        }
    }
    pthread_mutex_unlock(&g_mu);
    return r;
}

static VkResult VKAPI_CALL freesets_wrap(VkDevice d, VkDescriptorPool p, uint32_t n, const VkDescriptorSet* sets) {
    pthread_mutex_lock(&g_mu);
    int i = pool_index(p);
    if (i >= 0) g_poolTable[i].live = g_poolTable[i].live >= n ? g_poolTable[i].live - n : 0;
    pthread_mutex_unlock(&g_mu);
    return g_freeSets(d, p, n, sets);
}

static VkResult VKAPI_CALL resetdescpool_wrap(VkDevice d, VkDescriptorPool p, VkDescriptorPoolResetFlags f) {
    pthread_mutex_lock(&g_mu);
    int i = pool_index(p);
    if (i >= 0) g_poolTable[i].live = 0;
    pthread_mutex_unlock(&g_mu);
    return g_resetDescPool(d, p, f);
}

static void VKAPI_CALL destroydescpool_wrap(VkDevice d, VkDescriptorPool p, const VkAllocationCallbacks* a) {
    pthread_mutex_lock(&g_mu);
    int i = pool_index(p);
    if (i >= 0) {
        MLOG("descriptor pool #%u destroyed: peak %u sets of %u, %u failed allocations", g_poolTable[i].id, g_poolTable[i].peak,
             g_poolTable[i].maxSets, g_poolTable[i].failed);
        g_poolTable[i].pool = VK_NULL_HANDLE;
    }
    pthread_mutex_unlock(&g_mu);
    g_destroyDescPool(d, p, a);
}

static void pools_summary(void) {   // g_mu held
    char line[768];
    int len = 0;
    line[0] = 0;
    for (int i = 0; i < POOLS && len < (int)sizeof line - 48; i++) {
        if (!g_poolTable[i].pool || g_poolTable[i].maxSets < 1024) continue;
        len += snprintf(line + len, sizeof line - len, " #%u:%u/%u(peak %u)", g_poolTable[i].id, g_poolTable[i].live,
                        g_poolTable[i].maxSets, g_poolTable[i].peak);
    }
    MLOG("descriptor sets in use, big pools:%s", line);
}

// Second round (same day): the kernel charged the process 4.7 GB of GPU memory while vkAllocateMemory
// accounted for 2.1 GB; the process map showed the rest as five 360 MB and eight 46 MB kgsl mappings
// the engine never asked for through vkAllocateMemory: driver-internal memory. Suspects: command
// buffers that are allocated and never freed or reset (Adreno keeps their recording memory per pool),
// query pools, images the driver backs itself. Counted here.
static PFN_vkCreateCommandPool g_createPool;
static PFN_vkDestroyCommandPool g_destroyPool;
static PFN_vkResetCommandPool g_resetPool;
static PFN_vkAllocateCommandBuffers g_allocCb;
static PFN_vkFreeCommandBuffers g_freeCb;
static PFN_vkCreateQueryPool g_queryPool;
static PFN_vkCreateImage g_createImage;
static PFN_vkMapMemory g_map;
static unsigned g_cmdPools, g_cmdPoolsDestroyed, g_cmdResets, g_cbLive, g_cbAllocated, g_cbFreed, g_cbNext, g_images, g_maps;
static VkDeviceSize g_mapped;

static void cmd_summary(void) {   // g_mu held
    MLOG("command buffers: %u live (%u allocated, %u freed), %u pools (%u destroyed, %u resets); %u images created; "
         "%u maps (%u MB mapped)", g_cbLive, g_cbAllocated, g_cbFreed, g_cmdPools, g_cmdPoolsDestroyed, g_cmdResets,
         g_images, g_maps, mb(g_mapped));
}

static VkResult VKAPI_CALL createpool_wrap(VkDevice d, const VkCommandPoolCreateInfo* info, const VkAllocationCallbacks* a, VkCommandPool* out) {
    pthread_mutex_lock(&g_mu);
    g_cmdPools++;
    if (g_cmdPools <= 40) MLOG("command pool #%u: queue family %u, flags 0x%x%s%s", g_cmdPools, info->queueFamilyIndex, info->flags,
                               (info->flags & VK_COMMAND_POOL_CREATE_TRANSIENT_BIT) ? " transient" : "",
                               (info->flags & VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT) ? " reset-per-buffer" : "");
    pthread_mutex_unlock(&g_mu);
    return g_createPool(d, info, a, out);
}

static void VKAPI_CALL destroypool_wrap(VkDevice d, VkCommandPool p, const VkAllocationCallbacks* a) {
    pthread_mutex_lock(&g_mu);
    g_cmdPoolsDestroyed++;
    pthread_mutex_unlock(&g_mu);
    g_destroyPool(d, p, a);
}

static VkResult VKAPI_CALL resetpool_wrap(VkDevice d, VkCommandPool p, VkCommandPoolResetFlags f) {
    __atomic_add_fetch(&g_cmdResets, 1, __ATOMIC_RELAXED);
    return g_resetPool(d, p, f);
}

static VkResult VKAPI_CALL alloccb_wrap(VkDevice d, const VkCommandBufferAllocateInfo* info, VkCommandBuffer* out) {
    VkResult r = g_allocCb(d, info, out);
    if (r != VK_SUCCESS) return r;
    pthread_mutex_lock(&g_mu);
    g_cbAllocated += info->commandBufferCount;
    g_cbLive += info->commandBufferCount;
    if (g_cbLive >= g_cbNext) { g_cbNext = g_cbLive + 256; cmd_summary(); }
    pthread_mutex_unlock(&g_mu);
    return r;
}

static void VKAPI_CALL freecb_wrap(VkDevice d, VkCommandPool p, uint32_t n, const VkCommandBuffer* cbs) {
    pthread_mutex_lock(&g_mu);
    g_cbFreed += n;
    g_cbLive -= n;
    pthread_mutex_unlock(&g_mu);
    g_freeCb(d, p, n, cbs);
}

static VkResult VKAPI_CALL querypool_wrap(VkDevice d, const VkQueryPoolCreateInfo* info, const VkAllocationCallbacks* a, VkQueryPool* out) {
    MLOG("query pool: type %d, %u queries", info->queryType, info->queryCount);
    return g_queryPool(d, info, a, out);
}

// ---- texture shrink --------------------------------------------------------------------------------
// The same lever the 1.6 engines have (box64's mip shim, Unity's mipmap limit): a big sampled
// block-compressed texture is created at half its size with one mip fewer, i.e. without its top
// level, 4x less memory for it. The engine keeps talking about the texture it asked for; every
// reference to a mip level is shifted by one here, and references to the dropped level 0 are left out:
//  - vkCmdCopyBufferToImage / vkCmdCopyImage / vkCmdBlitImage (and their *2 forms): a region for old
//    level n is a region for new level n-1 with the SAME offsets and extent (old level n's extent is
//    new level n-1's), regions for level 0 are dropped;
//  - image views and barriers: baseMipLevel-1, levelCount trimmed, a range of level 0 alone dropped.
// Never render targets, storage images, arrays or uncompressed images (the engine's compute encoders
// address storage images by texel; uncompressed big images are its targets). The engine uploads BC
// mip chains ready-made (BC cannot be blit-filtered), so nothing generates mips from level 0.
// Readback of a shrunk texture (vkCmdCopyImageToBuffer of level 0) would hand the engine half-size
// data under the full-size name: logged loudly, not expected with a built texture cache.
// RIMDROID_TEX_SHRINK=0 turns it off; RIMDROID_TEX_SHRINK_MIN (default 2048) is the smallest side.
static PFN_vkDestroyImage g_destroyImage;
static PFN_vkCreateImageView g_createView;
static PFN_vkCmdCopyBufferToImage g_copyB2I;
static PFN_vkCmdCopyImage g_copyImage;
static PFN_vkCmdBlitImage g_blitImage;
static PFN_vkCmdCopyImageToBuffer g_copyI2B;
static PFN_vkCmdPipelineBarrier g_barrier;
static PFN_vkCmdPipelineBarrier2 g_barrier2;
static PFN_vkCmdCopyBufferToImage2 g_copyB2I2;
static PFN_vkCmdCopyImage2 g_copyImage2;
static PFN_vkCmdBlitImage2 g_blitImage2;
static PFN_vkCmdCopyImageToBuffer2 g_copyI2B2;

static void shrink_config(void) {
    if (g_shrinkOn >= 0) return;
    const char* e = getenv("RIMDROID_TEX_SHRINK");
    g_shrinkOn = !(e && e[0] == '0');
    e = getenv("RIMDROID_TEX_SHRINK_MIN");
    if (e && atoi(e) >= 256) g_shrinkMin = (unsigned)atoi(e);
    MLOG("texture shrink %s, from %u texels on a side", g_shrinkOn ? "on" : "off", g_shrinkMin);
}

static int is_block_compressed(VkFormat f) {
    return (f >= VK_FORMAT_BC1_RGB_UNORM_BLOCK && f <= VK_FORMAT_BC7_SRGB_BLOCK)
        || (f >= VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK && f <= VK_FORMAT_ASTC_12x12_SRGB_BLOCK);
}

static unsigned shrunk_slot(VkImage img) {
    return (unsigned)(((uint64_t)(uintptr_t)img * 0x9E3779B97F4A7C15ULL) >> 48) & (SHRUNK - 1);
}

// Index of a shrunk image, or -1. g_smu held.
static int shrunk_index(VkImage img) {
    if (img == VK_NULL_HANDLE || img == SHRUNK_TOMB) return -1;
    for (unsigned i = shrunk_slot(img), n = 0; n < SHRUNK; i = (i + 1) & (SHRUNK - 1), n++) {
        if (g_shrunk[i].image == VK_NULL_HANDLE) return -1;
        if (g_shrunk[i].image == img) return (int)i;
    }
    return -1;
}

// g_smu held and a slot reserved (g_shrunkLive + g_shrunkReserved < SHRUNK_LIMIT), so one is free.
static void shrunk_insert(VkImage img, uint32_t origMips, uint32_t newMips) {
    for (unsigned i = shrunk_slot(img); ; i = (i + 1) & (SHRUNK - 1)) {
        if (g_shrunk[i].image != VK_NULL_HANDLE && g_shrunk[i].image != SHRUNK_TOMB) continue;
        if (g_shrunk[i].image == SHRUNK_TOMB) g_shrunkTombs--;
        g_shrunk[i].image = img;
        g_shrunk[i].origMips = origMips;
        g_shrunk[i].newMips = newMips;
        g_shrunkLive++;
        return;
    }
}

// g_smu held. Removed entries leave tombstones; once they are a quarter of the table, it is rebuilt
// so that lookups keep finding empty slots early.
static void shrunk_remove(int index) {
    g_shrunk[index].image = SHRUNK_TOMB;
    g_shrunkLive--;
    if (++g_shrunkTombs < SHRUNK / 4) return;
    memcpy(g_shrunkSpare, g_shrunk, sizeof g_shrunk);
    memset(g_shrunk, 0, sizeof g_shrunk);
    g_shrunkLive = 0;
    g_shrunkTombs = 0;
    for (unsigned i = 0; i < SHRUNK; i++)
        if (g_shrunkSpare[i].image != VK_NULL_HANDLE && g_shrunkSpare[i].image != SHRUNK_TOMB)
            shrunk_insert(g_shrunkSpare[i].image, g_shrunkSpare[i].origMips, g_shrunkSpare[i].newMips);
}

// Old level -> new level; 0 means "dropped". Returns 0 if the whole range is the dropped level.
static int shift_range(const ShrunkImage* e, VkImageSubresourceRange* r) {
    if (r->baseMipLevel == 0 && r->levelCount == 1) return 0;
    if (r->baseMipLevel > 0) r->baseMipLevel--;
    else if (r->levelCount != VK_REMAINING_MIP_LEVELS) r->levelCount--;
    if (r->levelCount != VK_REMAINING_MIP_LEVELS && r->baseMipLevel + r->levelCount > e->newMips)
        r->levelCount = e->newMips > r->baseMipLevel ? e->newMips - r->baseMipLevel : 1;
    return 1;
}

static int shrinkable(const VkImageCreateInfo* info) {
    return g_shrinkOn && info->imageType == VK_IMAGE_TYPE_2D && info->tiling == VK_IMAGE_TILING_OPTIMAL
        && info->mipLevels >= 2 && info->arrayLayers == 1 && info->samples == VK_SAMPLE_COUNT_1_BIT
        && is_block_compressed(info->format)
        && !(info->usage & (VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT
                            | VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT))
        && !(info->flags & (VK_IMAGE_CREATE_SPARSE_BINDING_BIT | VK_IMAGE_CREATE_SPARSE_RESIDENCY_BIT))
        && (info->extent.width >= g_shrinkMin || info->extent.height >= g_shrinkMin);
}

// Images the driver might back itself (lazily allocated, sparse) or that are simply huge: logged
// from the create call, before any memory is bound. Big block-compressed textures: see texture shrink.
static const char* imageRoleName(uint32_t usage) {
    if (usage & VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT) return "depth target";
    if (usage & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) return "color target";
    return "storage image";
}

static VkResult VKAPI_CALL createimage_wrap(VkDevice d, const VkImageCreateInfo* info, const VkAllocationCallbacks* a, VkImage* out) {
    shrink_config();
    if (no_bc() && is_bc(info->format)) {
        unsigned n = __atomic_add_fetch(&g_bcImagesRefused, 1, __ATOMIC_RELAXED);
        if (n <= 10) MLOG("NO-BC MODE: refusing a BC image %ux%u format %d (the game asked despite the queries) #%u",
                          info->extent.width, info->extent.height, info->format, n);
        return VK_ERROR_FORMAT_NOT_SUPPORTED;
    }
    VkImageCreateInfo copy;
    int shrink = shrinkable(info);
    if (shrink) {
        pthread_mutex_lock(&g_smu);
        if (g_shrunkLive + g_shrunkReserved < SHRUNK_LIMIT) g_shrunkReserved++;
        else shrink = 0;                                  // no room to track it: created as asked
        pthread_mutex_unlock(&g_smu);
    }
    if (shrink) {
        copy = *info;
        copy.extent.width = info->extent.width > 1 ? info->extent.width / 2 : 1;
        copy.extent.height = info->extent.height > 1 ? info->extent.height / 2 : 1;
        copy.mipLevels = info->mipLevels - 1;
    }
    VkResult r = g_createImage(d, shrink ? &copy : info, a, out);
    if (shrink) {
        pthread_mutex_lock(&g_smu);
        g_shrunkReserved--;
        if (r == VK_SUCCESS) {
            shrunk_insert(*out, info->mipLevels, copy.mipLevels);
            g_shrunkUsed = 1;
            g_shrinkCount++;
            g_shrinkSavedApprox += (VkDeviceSize)info->extent.width * info->extent.height * 3 / 4;   // BC: ~1 B/texel, top level = 3/4 of the chain
            if (g_shrinkCount <= 40)
                MLOG("texture shrunk: %ux%u %u mips -> %ux%u %u mips, format %d (#%u, ~%u MB saved so far)",
                     info->extent.width, info->extent.height, info->mipLevels, copy.extent.width, copy.extent.height,
                     copy.mipLevels, info->format, g_shrinkCount, mb(g_shrinkSavedApprox));
        }
        pthread_mutex_unlock(&g_smu);
    }
    if (r != VK_SUCCESS) return r;
    pthread_mutex_lock(&g_mu);
    g_images++;
    uint64_t texels = (uint64_t)info->extent.width * info->extent.height * info->extent.depth * info->arrayLayers;
    int compressed = info->format >= VK_FORMAT_BC1_RGB_UNORM_BLOCK && info->format <= VK_FORMAT_BC7_SRGB_BLOCK;
    uint64_t approx = compressed ? texels : texels * 4;   // bytes for level 0: 1 B/texel BC, 4 B/texel otherwise
    int big = approx >= (48u << 20) || (info->arrayLayers > 1 && approx >= (8u << 20));
    if (info->usage & (VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT | VK_IMAGE_USAGE_STORAGE_BIT)) {
        ring_add("%s %ux%u x%u, %u mips, format %d, usage 0x%x (%llx)", imageRoleName(info->usage), info->extent.width, info->extent.height,
                 info->arrayLayers, info->mipLevels, info->format, info->usage, (unsigned long long)*out);
        target_remember(*out, info->extent.width, info->extent.height);
    }
    else if (approx >= (16u << 20))
        ring_add("texture %ux%u, %u mips, format %d", info->extent.width, info->extent.height, info->mipLevels, info->format);
    if (big || (info->flags & VK_IMAGE_CREATE_SPARSE_BINDING_BIT))
        MLOG("image #%u: %ux%ux%u x%u layers, %u mips, format %d, usage 0x%x, flags 0x%x, tiling %d, ~%u MB level 0",
             g_images, info->extent.width, info->extent.height, info->extent.depth, info->arrayLayers, info->mipLevels,
             info->format, info->usage, info->flags, info->tiling, mb(approx));
    pthread_mutex_unlock(&g_mu);
    return r;
}

#define TARGETS 256
static struct { VkImage image; uint32_t w, h; } g_targets[TARGETS];
static pthread_mutex_t g_tmu = PTHREAD_MUTEX_INITIALIZER;

static void target_remember(VkImage img, uint32_t w, uint32_t h) {
    pthread_mutex_lock(&g_tmu);
    for (int i = 0; i < TARGETS; i++) if (!g_targets[i].image) { g_targets[i].image = img; g_targets[i].w = w; g_targets[i].h = h; break; }
    pthread_mutex_unlock(&g_tmu);
}

static void VKAPI_CALL destroyimage_wrap(VkDevice d, VkImage img, const VkAllocationCallbacks* a) {
    if (img) {
        pthread_mutex_lock(&g_tmu);
        for (int i = 0; i < TARGETS; i++) if (g_targets[i].image == img) {
            ring_add("destroy target %ux%u (%llx)", g_targets[i].w, g_targets[i].h, (unsigned long long)img);
            g_targets[i].image = VK_NULL_HANDLE;
            break;
        }
        pthread_mutex_unlock(&g_tmu);
    }
    if (g_shrunkUsed) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(img);
        if (i >= 0) shrunk_remove(i);
        pthread_mutex_unlock(&g_smu);
    }
    g_destroyImage(d, img, a);
}

static VkResult VKAPI_CALL createview_wrap(VkDevice d, const VkImageViewCreateInfo* info, const VkAllocationCallbacks* a, VkImageView* out) {
    if (g_shrunkUsed) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(info->image);
        if (i >= 0) {
            VkImageViewCreateInfo copy = *info;
            VkImageSubresourceRange* r = &copy.subresourceRange;
            if (r->baseMipLevel > 0) r->baseMipLevel--;
            else if (r->levelCount != VK_REMAINING_MIP_LEVELS && r->levelCount > 1) r->levelCount--;   // a view of level 0 alone stays one level: the new level 0
            if (r->levelCount != VK_REMAINING_MIP_LEVELS && r->baseMipLevel + r->levelCount > g_shrunk[i].newMips)
                r->levelCount = g_shrunk[i].newMips - r->baseMipLevel;
            pthread_mutex_unlock(&g_smu);
            return g_createView(d, &copy, a, out);
        }
        pthread_mutex_unlock(&g_smu);
    }
    return g_createView(d, info, a, out);
}

// Copies: regions of old level n -> new level n-1, same offsets and extent; level 0 regions dropped.
static void VKAPI_CALL copyb2i_wrap(VkCommandBuffer cb, VkBuffer src, VkImage dst, VkImageLayout layout, uint32_t n, const VkBufferImageCopy* regions) {
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(dst);
        pthread_mutex_unlock(&g_smu);
        if (i >= 0) {
            VkBufferImageCopy stackKept[32];
            VkBufferImageCopy* kept = n <= 32 ? stackKept : (VkBufferImageCopy*)malloc(n * sizeof(VkBufferImageCopy));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                if (regions[j].imageSubresource.mipLevel == 0) continue;
                kept[k] = regions[j];
                kept[k].imageSubresource.mipLevel--;
                k++;
            }
            if (k) g_copyB2I(cb, src, dst, layout, k, kept);
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyB2I(cb, src, dst, layout, n, regions);
}

static void VKAPI_CALL copyimage_wrap(VkCommandBuffer cb, VkImage src, VkImageLayout sl, VkImage dst, VkImageLayout dl, uint32_t n, const VkImageCopy* regions) {
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int si = shrunk_index(src), di = shrunk_index(dst);
        pthread_mutex_unlock(&g_smu);
        if (si >= 0 || di >= 0) {
            VkImageCopy stackKept[32];
            VkImageCopy* kept = n <= 32 ? stackKept : (VkImageCopy*)malloc(n * sizeof(VkImageCopy));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                if ((si >= 0 && regions[j].srcSubresource.mipLevel == 0) || (di >= 0 && regions[j].dstSubresource.mipLevel == 0)) continue;
                kept[k] = regions[j];
                if (si >= 0) kept[k].srcSubresource.mipLevel--;
                if (di >= 0) kept[k].dstSubresource.mipLevel--;
                k++;
            }
            if (k) g_copyImage(cb, src, sl, dst, dl, k, kept);
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyImage(cb, src, sl, dst, dl, n, regions);
}

static void VKAPI_CALL blitimage_wrap(VkCommandBuffer cb, VkImage src, VkImageLayout sl, VkImage dst, VkImageLayout dl, uint32_t n, const VkImageBlit* regions, VkFilter f) {
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int si = shrunk_index(src), di = shrunk_index(dst);
        pthread_mutex_unlock(&g_smu);
        if (si >= 0 || di >= 0) {
            VkImageBlit stackKept[32];
            VkImageBlit* kept = n <= 32 ? stackKept : (VkImageBlit*)malloc(n * sizeof(VkImageBlit));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                if ((si >= 0 && regions[j].srcSubresource.mipLevel == 0) || (di >= 0 && regions[j].dstSubresource.mipLevel == 0)) continue;
                kept[k] = regions[j];
                if (si >= 0) kept[k].srcSubresource.mipLevel--;
                if (di >= 0) kept[k].dstSubresource.mipLevel--;
                k++;
            }
            if (k) g_blitImage(cb, src, sl, dst, dl, k, kept, f);
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_blitImage(cb, src, sl, dst, dl, n, regions, f);
}

static void VKAPI_CALL copyi2b_wrap(VkCommandBuffer cb, VkImage src, VkImageLayout sl, VkBuffer dst, uint32_t n, const VkBufferImageCopy* regions) {
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(src);
        pthread_mutex_unlock(&g_smu);
        if (i >= 0) {
            VkBufferImageCopy stackKept[32];
            VkBufferImageCopy* kept = n <= 32 ? stackKept : (VkBufferImageCopy*)malloc(n * sizeof(VkBufferImageCopy));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0, dropped = 0;
            for (uint32_t j = 0; j < n; j++) {
                if (regions[j].imageSubresource.mipLevel == 0) { dropped++; continue; }
                kept[k] = regions[j];
                kept[k].imageSubresource.mipLevel--;
                k++;
            }
            if (dropped && g_shrinkReadbacks++ < 20)
                MLOG("READBACK of a shrunk texture: %u region(s) of its dropped level 0 not copied (the engine gets stale data there)", dropped);
            if (k) g_copyI2B(cb, src, sl, dst, k, kept);
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyI2B(cb, src, sl, dst, n, regions);
}

static void VKAPI_CALL barrier_wrap(VkCommandBuffer cb, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkDependencyFlags dep,
                                    uint32_t nm, const VkMemoryBarrier* mem, uint32_t nb, const VkBufferMemoryBarrier* buf,
                                    uint32_t ni, const VkImageMemoryBarrier* img) {
    if (g_shrunkUsed && ni > 0) {
        VkImageMemoryBarrier stackKept[16];
        VkImageMemoryBarrier* kept = ni <= 16 ? stackKept : (VkImageMemoryBarrier*)malloc(ni * sizeof(VkImageMemoryBarrier));
        if (!kept) return;
        uint32_t k = 0;
        int touched = 0;
        pthread_mutex_lock(&g_smu);
        for (uint32_t j = 0; j < ni; j++) {
            int i = shrunk_index(img[j].image);
            kept[k] = img[j];
            if (i >= 0) {
                touched = 1;
                if (!shift_range(&g_shrunk[i], &kept[k].subresourceRange)) continue;
            }
            k++;
        }
        pthread_mutex_unlock(&g_smu);
        if (touched) {
            if (k || nm || nb) g_barrier(cb, srcStage, dstStage, dep, nm, mem, nb, buf, k, kept);
            if (kept != stackKept) free(kept);
            return;
        }
        if (kept != stackKept) free(kept);
    }
    g_barrier(cb, srcStage, dstStage, dep, nm, mem, nb, buf, ni, img);
}

static void VKAPI_CALL barrier2_wrap(VkCommandBuffer cb, const VkDependencyInfo* info) {
    uint32_t ni = info->imageMemoryBarrierCount;
    if (g_shrunkUsed && ni > 0) {
        VkImageMemoryBarrier2 stackKept[16];
        VkImageMemoryBarrier2* kept = ni <= 16 ? stackKept : (VkImageMemoryBarrier2*)malloc(ni * sizeof(VkImageMemoryBarrier2));
        if (!kept) return;
        uint32_t k = 0;
        int touched = 0;
        pthread_mutex_lock(&g_smu);
        for (uint32_t j = 0; j < ni; j++) {
            int i = shrunk_index(info->pImageMemoryBarriers[j].image);
            kept[k] = info->pImageMemoryBarriers[j];
            if (i >= 0) {
                touched = 1;
                if (!shift_range(&g_shrunk[i], &kept[k].subresourceRange)) continue;
            }
            k++;
        }
        pthread_mutex_unlock(&g_smu);
        if (touched) {
            VkDependencyInfo copy = *info;
            copy.imageMemoryBarrierCount = k;
            copy.pImageMemoryBarriers = kept;
            if (k || copy.memoryBarrierCount || copy.bufferMemoryBarrierCount) g_barrier2(cb, &copy);
            if (kept != stackKept) free(kept);
            return;
        }
        if (kept != stackKept) free(kept);
    }
    g_barrier2(cb, info);
}

static void VKAPI_CALL copyb2i2_wrap(VkCommandBuffer cb, const VkCopyBufferToImageInfo2* info) {
    uint32_t n = info->regionCount;
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(info->dstImage);
        pthread_mutex_unlock(&g_smu);
        if (i >= 0) {
            VkBufferImageCopy2 stackKept[32];
            VkBufferImageCopy2* kept = n <= 32 ? stackKept : (VkBufferImageCopy2*)malloc(n * sizeof(VkBufferImageCopy2));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                if (info->pRegions[j].imageSubresource.mipLevel == 0) continue;
                kept[k] = info->pRegions[j];
                kept[k].imageSubresource.mipLevel--;
                k++;
            }
            if (k) { VkCopyBufferToImageInfo2 copy = *info; copy.regionCount = k; copy.pRegions = kept; g_copyB2I2(cb, &copy); }
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyB2I2(cb, info);
}

static void VKAPI_CALL copyimage2_wrap(VkCommandBuffer cb, const VkCopyImageInfo2* info) {
    uint32_t n = info->regionCount;
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int si = shrunk_index(info->srcImage), di = shrunk_index(info->dstImage);
        pthread_mutex_unlock(&g_smu);
        if (si >= 0 || di >= 0) {
            VkImageCopy2 stackKept[32];
            VkImageCopy2* kept = n <= 32 ? stackKept : (VkImageCopy2*)malloc(n * sizeof(VkImageCopy2));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                const VkImageCopy2* r = &info->pRegions[j];
                if ((si >= 0 && r->srcSubresource.mipLevel == 0) || (di >= 0 && r->dstSubresource.mipLevel == 0)) continue;
                kept[k] = *r;
                if (si >= 0) kept[k].srcSubresource.mipLevel--;
                if (di >= 0) kept[k].dstSubresource.mipLevel--;
                k++;
            }
            if (k) { VkCopyImageInfo2 copy = *info; copy.regionCount = k; copy.pRegions = kept; g_copyImage2(cb, &copy); }
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyImage2(cb, info);
}

static void VKAPI_CALL blitimage2_wrap(VkCommandBuffer cb, const VkBlitImageInfo2* info) {
    uint32_t n = info->regionCount;
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int si = shrunk_index(info->srcImage), di = shrunk_index(info->dstImage);
        pthread_mutex_unlock(&g_smu);
        if (si >= 0 || di >= 0) {
            VkImageBlit2 stackKept[32];
            VkImageBlit2* kept = n <= 32 ? stackKept : (VkImageBlit2*)malloc(n * sizeof(VkImageBlit2));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0;
            for (uint32_t j = 0; j < n; j++) {
                const VkImageBlit2* r = &info->pRegions[j];
                if ((si >= 0 && r->srcSubresource.mipLevel == 0) || (di >= 0 && r->dstSubresource.mipLevel == 0)) continue;
                kept[k] = *r;
                if (si >= 0) kept[k].srcSubresource.mipLevel--;
                if (di >= 0) kept[k].dstSubresource.mipLevel--;
                k++;
            }
            if (k) { VkBlitImageInfo2 copy = *info; copy.regionCount = k; copy.pRegions = kept; g_blitImage2(cb, &copy); }
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_blitImage2(cb, info);
}

static void VKAPI_CALL copyi2b2_wrap(VkCommandBuffer cb, const VkCopyImageToBufferInfo2* info) {
    uint32_t n = info->regionCount;
    if (g_shrunkUsed && n > 0) {
        pthread_mutex_lock(&g_smu);
        int i = shrunk_index(info->srcImage);
        pthread_mutex_unlock(&g_smu);
        if (i >= 0) {
            VkBufferImageCopy2 stackKept[32];
            VkBufferImageCopy2* kept = n <= 32 ? stackKept : (VkBufferImageCopy2*)malloc(n * sizeof(VkBufferImageCopy2));
            if (!kept) return;   /* out of memory: the copy is lost, never passed unshifted */
            uint32_t k = 0, dropped = 0;
            for (uint32_t j = 0; j < n; j++) {
                if (info->pRegions[j].imageSubresource.mipLevel == 0) { dropped++; continue; }
                kept[k] = info->pRegions[j];
                kept[k].imageSubresource.mipLevel--;
                k++;
            }
            if (dropped && g_shrinkReadbacks++ < 20)
                MLOG("READBACK of a shrunk texture: %u region(s) of its dropped level 0 not copied (the engine gets stale data there)", dropped);
            if (k) { VkCopyImageToBufferInfo2 copy = *info; copy.regionCount = k; copy.pRegions = kept; g_copyI2B2(cb, &copy); }
            if (kept != stackKept) free(kept);
            return;
        }
    }
    g_copyI2B2(cb, info);
}

// ---- device-lost diagnosis ------------------------------------------------------------------------
// CameraPlus on 1.7 loses the device (VK_ERROR_DEVICE_LOST at vkQueueSubmit) in the first frame after a
// save loads. The fault may be in work submitted earlier than the submit that reports it, so the last
// notable things the engine did are kept in a ring: render targets and storage images created (any
// size), big buffers, framebuffers, oversized dispatches and draws, and every submit's command buffer
// count. On a failed submit, wait or device-wait-idle the ring is printed (RimDroid/vkmem).
#define RING 48
static PFN_vkQueueSubmit g_submit;
static PFN_vkQueueSubmit2 g_submit2;
static PFN_vkWaitForFences g_waitFences;
static PFN_vkDeviceWaitIdle g_waitIdle;
static PFN_vkCreateBuffer g_createBuffer;
static PFN_vkCreateFramebuffer g_createFb;
static PFN_vkCmdDispatch g_dispatch;
static PFN_vkCmdDraw g_draw;
static PFN_vkCmdDrawIndexed g_drawIndexed;
static PFN_vkCmdDrawIndexedIndirect g_drawIndexedIndirect;
static PFN_vkCmdDrawIndirect g_drawIndirect;
static pthread_mutex_t g_rmu = PTHREAD_MUTEX_INITIALIZER;
static char g_ring[RING][200];
static unsigned g_ringNext, g_submits, g_draws, g_dispatches, g_lostDumped;

static void ring_add(const char* fmt, ...) __attribute__((format(printf, 1, 2)));
static void ring_add(const char* fmt, ...) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    pthread_mutex_lock(&g_rmu);
    char* line = g_ring[g_ringNext % RING];
    int n = snprintf(line, 200, "[%lu.%03lu s%u d%u] ", (unsigned long)ts.tv_sec, (unsigned long)(ts.tv_nsec / 1000000), g_submits, g_draws);
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(line + n, 200 - n, fmt, ap);
    va_end(ap);
    g_ringNext++;
    pthread_mutex_unlock(&g_rmu);
}

static void ring_dump(const char* why) {
    pthread_mutex_lock(&g_rmu);
    if (g_lostDumped++ < 3) {
        MLOG("=== %s: last %u events (submits %u, draws %u, dispatches %u) ===", why, g_ringNext < RING ? g_ringNext : RING, g_submits, g_draws, g_dispatches);
        unsigned from = g_ringNext > RING ? g_ringNext - RING : 0;
        for (unsigned i = from; i < g_ringNext; i++) MLOG("  %s", g_ring[i % RING]);
    }
    pthread_mutex_unlock(&g_rmu);
}

static VkResult VKAPI_CALL submit_wrap(VkQueue q, uint32_t n, const VkSubmitInfo* infos, VkFence fence) {
    __atomic_add_fetch(&g_submits, 1, __ATOMIC_RELAXED);
    VkResult r = g_submit(q, n, infos, fence);
    if (r != VK_SUCCESS) {
        uint32_t cbs = 0;
        for (uint32_t i = 0; i < n; i++) cbs += infos[i].commandBufferCount;
        char why[96];
        snprintf(why, sizeof why, "vkQueueSubmit -> %d (%u submits, %u command buffers)", r, n, cbs);
        ring_dump(why);
    }
    return r;
}

static VkResult VKAPI_CALL submit2_wrap(VkQueue q, uint32_t n, const VkSubmitInfo2* infos, VkFence fence) {
    __atomic_add_fetch(&g_submits, 1, __ATOMIC_RELAXED);
    VkResult r = g_submit2(q, n, infos, fence);
    if (r != VK_SUCCESS) {
        uint32_t cbs = 0;
        for (uint32_t i = 0; i < n; i++) cbs += infos[i].commandBufferInfoCount;
        char why[96];
        snprintf(why, sizeof why, "vkQueueSubmit2 -> %d (%u submits, %u command buffers)", r, n, cbs);
        ring_dump(why);
    }
    return r;
}

static VkResult VKAPI_CALL waitfences_wrap(VkDevice d, uint32_t n, const VkFence* f, VkBool32 all, uint64_t timeout) {
    VkResult r = g_waitFences(d, n, f, all, timeout);
    if (timeout) ring_add("waited for %u fence(s) -> %d", n, r);
    if (r != VK_SUCCESS && r != VK_TIMEOUT) { char why[64]; snprintf(why, sizeof why, "vkWaitForFences -> %d", r); ring_dump(why); }
    return r;
}

static VkResult VKAPI_CALL waitidle_wrap(VkDevice d) {
    VkResult r = g_waitIdle(d);
    if (r != VK_SUCCESS) { char why[64]; snprintf(why, sizeof why, "vkDeviceWaitIdle -> %d", r); ring_dump(why); }
    return r;
}

static VkResult VKAPI_CALL createbuffer_wrap(VkDevice d, const VkBufferCreateInfo* info, const VkAllocationCallbacks* a, VkBuffer* out) {
    VkResult r = g_createBuffer(d, info, a, out);
    if (info->size >= (32u << 20)) ring_add("buffer %u MB usage 0x%x -> %d", mb(info->size), (unsigned)info->usage, r);
    return r;
}

static VkResult VKAPI_CALL createfb_wrap(VkDevice d, const VkFramebufferCreateInfo* info, const VkAllocationCallbacks* a, VkFramebuffer* out) {
    VkResult r = g_createFb(d, info, a, out);
    ring_add("framebuffer %ux%u x%u, %u attachments -> %d", info->width, info->height, info->layers, info->attachmentCount, r);
    return r;
}

static void VKAPI_CALL dispatch_wrap(VkCommandBuffer cb, uint32_t x, uint32_t y, uint32_t z) {
    __atomic_add_fetch(&g_dispatches, 1, __ATOMIC_RELAXED);
    if ((uint64_t)x * y * z > (1u << 20)) ring_add("dispatch %ux%ux%u groups", x, y, z);
    g_dispatch(cb, x, y, z);
}

static void VKAPI_CALL draw_wrap(VkCommandBuffer cb, uint32_t vc, uint32_t ic, uint32_t fv, uint32_t fi) {
    __atomic_add_fetch(&g_draws, 1, __ATOMIC_RELAXED);
    if (vc > (16u << 20) || ic > 200000) ring_add("draw %u vertices x %u instances", vc, ic);
    g_draw(cb, vc, ic, fv, fi);
}

static void VKAPI_CALL drawindexed_wrap(VkCommandBuffer cb, uint32_t ic, uint32_t inst, uint32_t fi, int32_t vo, uint32_t fin) {
    __atomic_add_fetch(&g_draws, 1, __ATOMIC_RELAXED);
    if (ic > (16u << 20) || inst > 200000) ring_add("drawIndexed %u indices x %u instances", ic, inst);
    g_drawIndexed(cb, ic, inst, fi, vo, fin);
}

static void VKAPI_CALL drawindexedindirect_wrap(VkCommandBuffer cb, VkBuffer b, VkDeviceSize off, uint32_t count, uint32_t stride) {
    __atomic_add_fetch(&g_draws, 1, __ATOMIC_RELAXED);
    if (count > 100000) ring_add("drawIndexedIndirect %u draws", count);
    g_drawIndexedIndirect(cb, b, off, count, stride);
}

static void VKAPI_CALL drawindirect_wrap(VkCommandBuffer cb, VkBuffer b, VkDeviceSize off, uint32_t count, uint32_t stride) {
    __atomic_add_fetch(&g_draws, 1, __ATOMIC_RELAXED);
    if (count > 100000) ring_add("drawIndirect %u draws", count);
    g_drawIndirect(cb, b, off, count, stride);
}

// Descriptor writes with a null image view / sampler / buffer: a material whose texture never
// resolved (a mod's asset-bundle shader and textures the engine could not take) ends up as a
// descriptor the GPU reads garbage through. Counted and ringed, never changed.
static PFN_vkUpdateDescriptorSets g_updateSets;
static PFN_vkCreateShaderModule g_shaderModule;
static unsigned g_shaderModules;

static void VKAPI_CALL updatesets_wrap(VkDevice d, uint32_t nw, const VkWriteDescriptorSet* w, uint32_t nc, const VkCopyDescriptorSet* c) {
    for (uint32_t i = 0; i < nw; i++) {
        const VkWriteDescriptorSet* x = &w[i];
        unsigned nulls = 0;
        switch (x->descriptorType) {
            case VK_DESCRIPTOR_TYPE_SAMPLER:
                for (uint32_t j = 0; x->pImageInfo && j < x->descriptorCount; j++) if (!x->pImageInfo[j].sampler) nulls++;
                break;
            case VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER:
                for (uint32_t j = 0; x->pImageInfo && j < x->descriptorCount; j++) if (!x->pImageInfo[j].imageView || !x->pImageInfo[j].sampler) nulls++;
                break;
            case VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE: case VK_DESCRIPTOR_TYPE_STORAGE_IMAGE: case VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT:
                for (uint32_t j = 0; x->pImageInfo && j < x->descriptorCount; j++) if (!x->pImageInfo[j].imageView) nulls++;
                break;
            case VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER: case VK_DESCRIPTOR_TYPE_STORAGE_BUFFER:
            case VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC: case VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC:
                for (uint32_t j = 0; x->pBufferInfo && j < x->descriptorCount; j++) if (!x->pBufferInfo[j].buffer) nulls++;
                break;
            default: break;
        }
        if (nulls) {
            unsigned n = __atomic_add_fetch(&g_nullWrites, nulls, __ATOMIC_RELAXED);
            if (n <= 40) ring_add("NULL descriptor write: type %d, binding %u, element %u, %u of %u null (total %u)",
                                  x->descriptorType, x->dstBinding, x->dstArrayElement, nulls, x->descriptorCount, n);
        }
    }
    g_updateSets(d, nw, w, nc, c);
}

// Every shader module: its size, whether it starts with the SPIR-V magic word, and how many
// instructions it holds (garbage or empty code from a failed compile is the one thing that makes a
// GPU die without any API error). Logged once the game is up (the first ~260 are the pack's).
static VkResult VKAPI_CALL shadermodule_wrap(VkDevice d, const VkShaderModuleCreateInfo* info, const VkAllocationCallbacks* a, VkShaderModule* out) {
    VkResult r = g_shaderModule(d, info, a, out);
    unsigned n = __atomic_add_fetch(&g_shaderModules, 1, __ATOMIC_RELAXED);
    uint32_t magic = info->codeSize >= 4 ? info->pCode[0] : 0, bound = info->codeSize >= 16 ? info->pCode[3] : 0;
    int bad = magic != 0x07230203u || info->codeSize < 20 || (info->codeSize & 3);
    if (bad || n > 200) {
        ring_add("shader module #%u: %u bytes, magic 0x%08x%s, bound %u -> %d", n, (unsigned)info->codeSize, magic,
                 bad ? " (NOT SPIR-V)" : "", bound, r);
        if (bad) MLOG("shader module #%u is NOT valid SPIR-V: %u bytes, first word 0x%08x", n, (unsigned)info->codeSize, magic);
    }
    return r;
}

static VkResult VKAPI_CALL map_wrap(VkDevice d, VkDeviceMemory m, VkDeviceSize off, VkDeviceSize size, VkMemoryMapFlags f, void** data) {
    VkResult r = g_map(d, m, off, size, f, data);
    if (r == VK_SUCCESS) {
        pthread_mutex_lock(&g_mu);
        g_maps++;
        if (size != VK_WHOLE_SIZE) g_mapped += size;
        pthread_mutex_unlock(&g_mu);
    }
    return r;
}

static VkResult VKAPI_CALL gfxpipes_wrap(VkDevice d, VkPipelineCache c, uint32_t n, const VkGraphicsPipelineCreateInfo* i, const VkAllocationCallbacks* a, VkPipeline* p) {
    unsigned total = __atomic_add_fetch(&g_pipelines, n, __ATOMIC_RELAXED);
    VkResult r = g_gfxPipes(d, c, n, i, a, p);
    for (uint32_t k = 0; k < n && k < 4; k++) {
        const VkGraphicsPipelineCreateInfo* g = &i[k];
        uint64_t vs = 0, fs = 0;
        for (uint32_t st = 0; st < g->stageCount; st++) {
            if (g->pStages[st].stage == VK_SHADER_STAGE_VERTEX_BIT) vs = (uint64_t)g->pStages[st].module;
            if (g->pStages[st].stage == VK_SHADER_STAGE_FRAGMENT_BIT) fs = (uint64_t)g->pStages[st].module;
        }
        const VkPipelineColorBlendStateCreateInfo* cb = g->pColorBlendState;
        ring_add("graphics pipeline = %u -> %d: vs %llx fs %llx, %u attachments, blend %s, topology %d", total, r,
                 (unsigned long long)vs, (unsigned long long)fs, cb ? cb->attachmentCount : 0,
                 cb && cb->attachmentCount && cb->pAttachments && cb->pAttachments[0].blendEnable ? "on" : "off",
                 g->pInputAssemblyState ? (int)g->pInputAssemblyState->topology : -1);
    }
    return r;
}

static VkResult VKAPI_CALL comppipes_wrap(VkDevice d, VkPipelineCache c, uint32_t n, const VkComputePipelineCreateInfo* i, const VkAllocationCallbacks* a, VkPipeline* p) {
    __atomic_add_fetch(&g_pipelines, n, __ATOMIC_RELAXED);
    return g_compPipes(d, c, n, i, a, p);
}

static VkResult VKAPI_CALL swapchain_wrap(VkDevice device, const VkSwapchainCreateInfoKHR* info,
                                          const VkAllocationCallbacks* alloc, VkSwapchainKHR* out) {
    VkSwapchainCreateInfoKHR copy = *info;
    copy.preTransform = VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR;
    LOGI("swapchain %ux%u (preTransform forced to identity)", copy.imageExtent.width, copy.imageExtent.height);
    return g_swapchain(device, &copy, alloc, out);
}

static PFN_vkVoidFunction VKAPI_CALL gdpa_wrap(VkDevice device, const char* name);

static PFN_vkVoidFunction VKAPI_CALL gipa_wrap(VkInstance instance, const char* name) {
    if (!g_gipa) g_gipa = (PFN_vkGetInstanceProcAddr)real("vkGetInstanceProcAddr");
    if (!name || !g_gipa) return NULL;
    if (!strcmp(name, "vkGetInstanceProcAddr")) return (PFN_vkVoidFunction)gipa_wrap;
    if (!strcmp(name, "vkGetDeviceProcAddr")) {
        if (!g_gdpa) g_gdpa = (PFN_vkGetDeviceProcAddr)g_gipa(instance, "vkGetDeviceProcAddr");
        return (PFN_vkVoidFunction)gdpa_wrap;
    }
    PFN_vkVoidFunction f = g_gipa(instance, name);
    if (!f) return NULL;
    if (!strcmp(name, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR")) { g_caps = (PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR)f; return (PFN_vkVoidFunction)caps_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceFeatures")) { g_getFeatures = (PFN_vkGetPhysicalDeviceFeatures)f; return (PFN_vkVoidFunction)getfeatures_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceFeatures2") || !strcmp(name, "vkGetPhysicalDeviceFeatures2KHR")) { g_getFeatures2 = (PFN_vkGetPhysicalDeviceFeatures2)f; return (PFN_vkVoidFunction)getfeatures2_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceFormatProperties")) { g_getFormatProps = (PFN_vkGetPhysicalDeviceFormatProperties)f; return (PFN_vkVoidFunction)getformatprops_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceFormatProperties2") || !strcmp(name, "vkGetPhysicalDeviceFormatProperties2KHR")) { g_getFormatProps2 = (PFN_vkGetPhysicalDeviceFormatProperties2)f; return (PFN_vkVoidFunction)getformatprops2_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceImageFormatProperties")) { g_getImageFormatProps = (PFN_vkGetPhysicalDeviceImageFormatProperties)f; return (PFN_vkVoidFunction)getimageformatprops_wrap; }
    if (!strcmp(name, "vkGetPhysicalDeviceImageFormatProperties2") || !strcmp(name, "vkGetPhysicalDeviceImageFormatProperties2KHR")) { g_getImageFormatProps2 = (PFN_vkGetPhysicalDeviceImageFormatProperties2)f; return (PFN_vkVoidFunction)getimageformatprops2_wrap; }
    return hook(name, f);
}

static PFN_vkVoidFunction VKAPI_CALL gdpa_wrap(VkDevice device, const char* name) {
    if (!g_gdpa || !name) return NULL;
    if (!strcmp(name, "vkGetDeviceProcAddr")) return (PFN_vkVoidFunction)gdpa_wrap;
    PFN_vkVoidFunction f = g_gdpa(device, name);
    return f ? hook(name, f) : NULL;
}

static PFN_vkVoidFunction hook(const char* name, PFN_vkVoidFunction f) {
    if (!strcmp(name, "vkCreateSwapchainKHR")) { g_swapchain = (PFN_vkCreateSwapchainKHR)f; return (PFN_vkVoidFunction)swapchain_wrap; }
    if (!strcmp(name, "vkQueuePresentKHR")) { g_present = (PFN_vkQueuePresentKHR)f; return (PFN_vkVoidFunction)present_wrap; }
    if (!strcmp(name, "vkAcquireNextImageKHR")) { g_acquire = (PFN_vkAcquireNextImageKHR)f; return (PFN_vkVoidFunction)acquire_wrap; }
    if (!strcmp(name, "vkAllocateMemory")) { g_allocMem = (PFN_vkAllocateMemory)f; return (PFN_vkVoidFunction)allocmem_wrap; }
    if (!strcmp(name, "vkFreeMemory")) { g_freeMem = (PFN_vkFreeMemory)f; return (PFN_vkVoidFunction)freemem_wrap; }
    if (!strcmp(name, "vkCreateDescriptorPool")) { g_descPool = (PFN_vkCreateDescriptorPool)f; return (PFN_vkVoidFunction)descpool_wrap; }
    if (!strcmp(name, "vkAllocateDescriptorSets")) { g_allocSets = (PFN_vkAllocateDescriptorSets)f; return (PFN_vkVoidFunction)allocsets_wrap; }
    if (!strcmp(name, "vkFreeDescriptorSets")) { g_freeSets = (PFN_vkFreeDescriptorSets)f; return (PFN_vkVoidFunction)freesets_wrap; }
    if (!strcmp(name, "vkResetDescriptorPool")) { g_resetDescPool = (PFN_vkResetDescriptorPool)f; return (PFN_vkVoidFunction)resetdescpool_wrap; }
    if (!strcmp(name, "vkDestroyDescriptorPool")) { g_destroyDescPool = (PFN_vkDestroyDescriptorPool)f; return (PFN_vkVoidFunction)destroydescpool_wrap; }
    if (!strcmp(name, "vkCreateGraphicsPipelines")) { g_gfxPipes = (PFN_vkCreateGraphicsPipelines)f; return (PFN_vkVoidFunction)gfxpipes_wrap; }
    if (!strcmp(name, "vkCreateComputePipelines")) { g_compPipes = (PFN_vkCreateComputePipelines)f; return (PFN_vkVoidFunction)comppipes_wrap; }
    if (!strcmp(name, "vkCreateCommandPool")) { g_createPool = (PFN_vkCreateCommandPool)f; return (PFN_vkVoidFunction)createpool_wrap; }
    if (!strcmp(name, "vkDestroyCommandPool")) { g_destroyPool = (PFN_vkDestroyCommandPool)f; return (PFN_vkVoidFunction)destroypool_wrap; }
    if (!strcmp(name, "vkResetCommandPool")) { g_resetPool = (PFN_vkResetCommandPool)f; return (PFN_vkVoidFunction)resetpool_wrap; }
    if (!strcmp(name, "vkAllocateCommandBuffers")) { g_allocCb = (PFN_vkAllocateCommandBuffers)f; return (PFN_vkVoidFunction)alloccb_wrap; }
    if (!strcmp(name, "vkFreeCommandBuffers")) { g_freeCb = (PFN_vkFreeCommandBuffers)f; return (PFN_vkVoidFunction)freecb_wrap; }
    if (!strcmp(name, "vkCreateQueryPool")) { g_queryPool = (PFN_vkCreateQueryPool)f; return (PFN_vkVoidFunction)querypool_wrap; }
    if (!strcmp(name, "vkCreateImage")) { g_createImage = (PFN_vkCreateImage)f; return (PFN_vkVoidFunction)createimage_wrap; }
    if (!strcmp(name, "vkDestroyImage")) { g_destroyImage = (PFN_vkDestroyImage)f; return (PFN_vkVoidFunction)destroyimage_wrap; }
    if (!strcmp(name, "vkCreateImageView")) { g_createView = (PFN_vkCreateImageView)f; return (PFN_vkVoidFunction)createview_wrap; }
    if (!strcmp(name, "vkCmdCopyBufferToImage")) { g_copyB2I = (PFN_vkCmdCopyBufferToImage)f; return (PFN_vkVoidFunction)copyb2i_wrap; }
    if (!strcmp(name, "vkCmdCopyImage")) { g_copyImage = (PFN_vkCmdCopyImage)f; return (PFN_vkVoidFunction)copyimage_wrap; }
    if (!strcmp(name, "vkCmdBlitImage")) { g_blitImage = (PFN_vkCmdBlitImage)f; return (PFN_vkVoidFunction)blitimage_wrap; }
    if (!strcmp(name, "vkCmdCopyImageToBuffer")) { g_copyI2B = (PFN_vkCmdCopyImageToBuffer)f; return (PFN_vkVoidFunction)copyi2b_wrap; }
    if (!strcmp(name, "vkCmdPipelineBarrier")) { g_barrier = (PFN_vkCmdPipelineBarrier)f; return (PFN_vkVoidFunction)barrier_wrap; }
    if (!strcmp(name, "vkCmdPipelineBarrier2") || !strcmp(name, "vkCmdPipelineBarrier2KHR")) { g_barrier2 = (PFN_vkCmdPipelineBarrier2)f; return (PFN_vkVoidFunction)barrier2_wrap; }
    if (!strcmp(name, "vkCmdCopyBufferToImage2") || !strcmp(name, "vkCmdCopyBufferToImage2KHR")) { g_copyB2I2 = (PFN_vkCmdCopyBufferToImage2)f; return (PFN_vkVoidFunction)copyb2i2_wrap; }
    if (!strcmp(name, "vkCmdCopyImage2") || !strcmp(name, "vkCmdCopyImage2KHR")) { g_copyImage2 = (PFN_vkCmdCopyImage2)f; return (PFN_vkVoidFunction)copyimage2_wrap; }
    if (!strcmp(name, "vkCmdBlitImage2") || !strcmp(name, "vkCmdBlitImage2KHR")) { g_blitImage2 = (PFN_vkCmdBlitImage2)f; return (PFN_vkVoidFunction)blitimage2_wrap; }
    if (!strcmp(name, "vkCmdCopyImageToBuffer2") || !strcmp(name, "vkCmdCopyImageToBuffer2KHR")) { g_copyI2B2 = (PFN_vkCmdCopyImageToBuffer2)f; return (PFN_vkVoidFunction)copyi2b2_wrap; }
    if (!strcmp(name, "vkMapMemory")) { g_map = (PFN_vkMapMemory)f; return (PFN_vkVoidFunction)map_wrap; }
    if (!strcmp(name, "vkQueueSubmit")) { g_submit = (PFN_vkQueueSubmit)f; return (PFN_vkVoidFunction)submit_wrap; }
    if (!strcmp(name, "vkUpdateDescriptorSets")) { g_updateSets = (PFN_vkUpdateDescriptorSets)f; return (PFN_vkVoidFunction)updatesets_wrap; }
    if (!strcmp(name, "vkCreateShaderModule")) { g_shaderModule = (PFN_vkCreateShaderModule)f; return (PFN_vkVoidFunction)shadermodule_wrap; }
    if (!strcmp(name, "vkQueueSubmit2") || !strcmp(name, "vkQueueSubmit2KHR")) { g_submit2 = (PFN_vkQueueSubmit2)f; return (PFN_vkVoidFunction)submit2_wrap; }
    if (!strcmp(name, "vkWaitForFences")) { g_waitFences = (PFN_vkWaitForFences)f; return (PFN_vkVoidFunction)waitfences_wrap; }
    if (!strcmp(name, "vkDeviceWaitIdle")) { g_waitIdle = (PFN_vkDeviceWaitIdle)f; return (PFN_vkVoidFunction)waitidle_wrap; }
    if (!strcmp(name, "vkCreateBuffer")) { g_createBuffer = (PFN_vkCreateBuffer)f; return (PFN_vkVoidFunction)createbuffer_wrap; }
    if (!strcmp(name, "vkCreateFramebuffer")) { g_createFb = (PFN_vkCreateFramebuffer)f; return (PFN_vkVoidFunction)createfb_wrap; }
    if (!strcmp(name, "vkCmdDispatch")) { g_dispatch = (PFN_vkCmdDispatch)f; return (PFN_vkVoidFunction)dispatch_wrap; }
    if (!strcmp(name, "vkCmdDraw")) { g_draw = (PFN_vkCmdDraw)f; return (PFN_vkVoidFunction)draw_wrap; }
    if (!strcmp(name, "vkCmdDrawIndexed")) { g_drawIndexed = (PFN_vkCmdDrawIndexed)f; return (PFN_vkVoidFunction)drawindexed_wrap; }
    if (!strcmp(name, "vkCmdDrawIndexedIndirect")) { g_drawIndexedIndirect = (PFN_vkCmdDrawIndexedIndirect)f; return (PFN_vkVoidFunction)drawindexedindirect_wrap; }
    if (!strcmp(name, "vkCmdDrawIndirect")) { g_drawIndirect = (PFN_vkCmdDrawIndirect)f; return (PFN_vkVoidFunction)drawindirect_wrap; }
    return f;
}

API PFN_vkVoidFunction vkGetInstanceProcAddr(VkInstance instance, const char* name) { return gipa_wrap(instance, name); }
API PFN_vkVoidFunction vkGetDeviceProcAddr(VkDevice device, const char* name) {
    if (!g_gdpa) g_gdpa = (PFN_vkGetDeviceProcAddr)real("vkGetDeviceProcAddr");
    return gdpa_wrap(device, name);
}
API VkResult vkCreateInstance(const VkInstanceCreateInfo* info, const VkAllocationCallbacks* alloc, VkInstance* out) {
    return ((PFN_vkCreateInstance)real("vkCreateInstance"))(info, alloc, out);
}
API VkResult vkEnumerateInstanceExtensionProperties(const char* layer, uint32_t* count, VkExtensionProperties* props) {
    return ((PFN_vkEnumerateInstanceExtensionProperties)real("vkEnumerateInstanceExtensionProperties"))(layer, count, props);
}
API VkResult vkEnumerateInstanceLayerProperties(uint32_t* count, VkLayerProperties* props) {
    return ((PFN_vkEnumerateInstanceLayerProperties)real("vkEnumerateInstanceLayerProperties"))(count, props);
}
API VkResult vkEnumerateInstanceVersion(uint32_t* version) {
    PFN_vkEnumerateInstanceVersion f = (PFN_vkEnumerateInstanceVersion)real("vkEnumerateInstanceVersion");
    if (!f) { *version = VK_API_VERSION_1_0; return VK_SUCCESS; }
    return f(version);
}
// The instance-level surface query, in case the engine binds it directly instead of through
// vkGetInstanceProcAddr.
API VkResult vkGetPhysicalDeviceSurfaceCapabilitiesKHR(VkPhysicalDevice pd, VkSurfaceKHR s, VkSurfaceCapabilitiesKHR* c) {
    if (!g_caps) g_caps = (PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR)real("vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
    return caps_wrap(pd, s, c);
}
