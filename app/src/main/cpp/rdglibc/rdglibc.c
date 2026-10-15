// librdglibc.so — glibc's ABI for the libc calls where Android's bionic differs, for mods on
// RimWorld 1.7 (.NET) that P/Invoke "libc" as on desktop Linux. MonoMod inside Harmony asks for
// __errno_location (bionic calls it __errno), sysconf with glibc's numbering of the names, and mmap
// with a 32-bit offset; without them Harmony patches nothing. RimDroidClrHooks maps "libc" and
// "libdl.so.2" of those assemblies here; any other symbol is found in this library's dependencies
// (bionic's libc.so and libdl.so). Same translation box64 gives native Mono on 1.6
// (wrappedlibmonobdwgc.c).
//
// mkstemp also hands MonoMod the Android build of its exception helper (librdexhelper.so, see
// exhelper.c): MonoMod writes its glibc build to a mkstemp file and loads it from there.
#include <android/log.h>
#include <dlfcn.h>
#include <errno.h>
#include <limits.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <sys/types.h>
#include <unistd.h>

#define API __attribute__((visibility("default")))
#define TAG "RimDroid/glibc"

// The exported names are glibc's; the C names differ so that bionic's own declarations of
// sysconf and mmap (whose signatures these do not share) stay out of the way.
API int* glibc_errno_location(void) __asm__("__errno_location");
API long glibc_sysconf(int name) __asm__("sysconf");
API void* glibc_mmap(void* addr, size_t length, int prot, int flags, int fd, int offset) __asm__("mmap");
API int glibc_mkstemp(char* path_template) __asm__("mkstemp");

static long (*real_sysconf)(int);
static void* (*real_mmap)(void*, size_t, int, int, int, off_t);
static int (*real_mkstemp)(char*);

__attribute__((constructor)) static void init(void) {
    void* libc = dlopen("libc.so", RTLD_NOW | RTLD_NOLOAD);
    if (libc) {
        real_sysconf = (long (*)(int))dlsym(libc, "sysconf");
        real_mmap = (void* (*)(void*, size_t, int, int, int, off_t))dlsym(libc, "mmap");
        real_mkstemp = (int (*)(char*))dlsym(libc, "mkstemp");
    }
    int ok = real_sysconf && real_mmap && real_mkstemp;
    __android_log_print(ok ? ANDROID_LOG_INFO : ANDROID_LOG_ERROR, TAG,
                        "glibc ABI for libc P/Invokes: %s", ok ? "ready" : "bionic libc not found");
}

// MonoMod.Core drops its native exception helper (built for glibc, unloadable here) into a
// "mm-exhelper.so.XXXXXX" file and loads it by that name. The name becomes a link to our Android
// build next to this library; the descriptor MonoMod writes its own copy through now belongs to a
// file without a name, which disappears when MonoMod closes it.
int glibc_mkstemp(char* path_template) {
    if (!real_mkstemp) {
        errno = ENOSYS;
        return -1;
    }
    int fd = real_mkstemp(path_template);
    const char* base = strrchr(path_template, '/');
    base = base ? base + 1 : path_template;
    if (fd < 0 || strncmp(base, "mm-exhelper", 11) != 0) return fd;
    Dl_info self;
    char helper[PATH_MAX];
    if (!dladdr((void*)glibc_mkstemp, &self) || !self.dli_fname) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "MonoMod's exception helper: cannot locate librdglibc.so");
        return fd;
    }
    const char* slash = strrchr(self.dli_fname, '/');
    int dir = slash ? (int)(slash - self.dli_fname) : 0;
    snprintf(helper, sizeof helper, "%.*s/librdexhelper.so", dir, self.dli_fname);
    if (access(helper, R_OK) != 0 || unlink(path_template) != 0 || symlink(helper, path_template) != 0) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "MonoMod's exception helper: cannot use %s: %s",
                            helper, strerror(errno));
        return fd;
    }
    __android_log_print(ANDROID_LOG_INFO, TAG, "MonoMod's exception helper: %s -> %s", path_template, helper);
    return fd;
}

int* glibc_errno_location(void) {
    return &errno;
}

// glibc numbers sysconf's names differently from bionic; translate the ones desktop code asks for.
long glibc_sysconf(int name) {
    int bionic;
    switch (name) {
        case 0: bionic = _SC_ARG_MAX; break;
        case 2: bionic = _SC_CLK_TCK; break;
        case 4: bionic = _SC_OPEN_MAX; break;
        case 30: bionic = _SC_PAGESIZE; break;
        case 31: bionic = _SC_RTSIG_MAX; break;
        case 83: bionic = _SC_NPROCESSORS_CONF; break;
        case 84: bionic = _SC_NPROCESSORS_ONLN; break;
        case 85: bionic = _SC_PHYS_PAGES; break;
        case 86: bionic = _SC_AVPHYS_PAGES; break;
        default:
            __android_log_print(ANDROID_LOG_WARN, TAG, "sysconf(%d): glibc name not translated, returning -1", name);
            errno = EINVAL;
            return -1;
    }
    return real_sysconf ? real_sysconf(bionic) : -1;
}

// MonoMod declares the offset as a 32-bit int: the upper half of the register is not ours to trust.
void* glibc_mmap(void* addr, size_t length, int prot, int flags, int fd, int offset) {
    if (!real_mmap) {
        errno = ENOSYS;
        return (void*)-1;
    }
    return real_mmap(addr, length, prot, flags, fd, (off_t)offset);
}
