// libclrhost.so — hosts .NET (CoreCLR for Android) for RimWorld 1.7, which no longer runs on Unity
// but on Ludeon's Waylith engine over .NET 10. Stage 1 of the port: start the runtime from the
// official android-arm64 runtime pack, run the game's RimWorld.dll, and log everything (stdout,
// stderr and the runtime's own error writer) to logcat and a file, so the first native library the
// game cannot find is visible. Runs in its own process (ClrActivity, ":clr"): a CoreCLR is
// initialised once per process and box64's linker hooks must never be around it.
#include <jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define TAG "RimDroid/clr"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

typedef int (*coreclr_initialize_fn)(const char* exePath, const char* appDomainFriendlyName, int propertyCount,
                                     const char** propertyKeys, const char** propertyValues,
                                     void** hostHandle, unsigned int* domainId);
typedef int (*coreclr_execute_assembly_fn)(void* hostHandle, unsigned int domainId, int argc, const char** argv,
                                           const char* managedAssemblyPath, unsigned int* exitCode);
typedef void (*coreclr_error_writer_fn)(const char* message);
typedef int (*coreclr_set_error_writer_fn)(coreclr_error_writer_fn writer);

static FILE* g_log;
static pthread_mutex_t g_log_lock = PTHREAD_MUTEX_INITIALIZER;

static void log_line(int prio, const char* line) {
    __android_log_write(prio, TAG, line);
    pthread_mutex_lock(&g_log_lock);
    if (g_log) { fputs(line, g_log); fputc('\n', g_log); fflush(g_log); }
    pthread_mutex_unlock(&g_log_lock);
}

static void error_writer(const char* message) { log_line(ANDROID_LOG_ERROR, message); }

// stdout/stderr of the managed side (Console.WriteLine, unhandled exceptions) go to a pipe that this
// thread splits into lines.
static void* pipe_reader(void* arg) {
    int fd = (int)(intptr_t)arg;
    char buf[4096], line[4096];
    size_t len = 0;
    ssize_t n;
    while ((n = read(fd, buf, sizeof(buf))) > 0) {
        for (ssize_t i = 0; i < n; i++) {
            if (buf[i] == '\n' || len == sizeof(line) - 1) {
                line[len] = 0;
                log_line(ANDROID_LOG_INFO, line);
                len = 0;
            } else if (buf[i] != '\r') {
                line[len++] = buf[i];
            }
        }
    }
    return NULL;
}

static void redirect_std(void) {
    int fds[2];
    if (pipe(fds) != 0) return;
    dup2(fds[1], 1);
    dup2(fds[1], 2);
    setvbuf(stdout, NULL, _IOLBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
    pthread_t t;
    pthread_create(&t, NULL, pipe_reader, (void*)(intptr_t)fds[0]);
    pthread_detach(t);
}

static char** to_cstrings(JNIEnv* env, jobjectArray arr, int* count) {
    int n = arr ? (*env)->GetArrayLength(env, arr) : 0;
    char** out = calloc((size_t)n + 1, sizeof(char*));
    for (int i = 0; i < n; i++) {
        jstring s = (jstring)(*env)->GetObjectArrayElement(env, arr, i);
        const char* c = (*env)->GetStringUTFChars(env, s, NULL);
        out[i] = strdup(c);
        (*env)->ReleaseStringUTFChars(env, s, c);
        (*env)->DeleteLocalRef(env, s);
    }
    *count = n;
    return out;
}

// Returns the managed exit code, or a negative number when the runtime could not start.
static void exit_now(void) { _exit(0); }

JNIEXPORT jint JNICALL
Java_com_rimdroid_ClrActivity_runClr(JNIEnv* env, jclass clazz, jstring jCoreclr, jstring jAssembly,
                                     jstring jLogFile, jobjectArray jKeys, jobjectArray jValues, jobjectArray jArgs) {
    (void)clazz;
    const char* coreclrPath = (*env)->GetStringUTFChars(env, jCoreclr, NULL);
    const char* assembly = (*env)->GetStringUTFChars(env, jAssembly, NULL);
    const char* logFile = (*env)->GetStringUTFChars(env, jLogFile, NULL);
    g_log = fopen(logFile, "w");
    redirect_std();
    {   // The game's folder is the working directory, as on a PC (Data/, Mods/, steam_appid.txt).
        char dir[1024];
        snprintf(dir, sizeof(dir), "%s", assembly);
        char* slash = strrchr(dir, '/');
        if (slash) { *slash = 0; if (chdir(dir) != 0) log_line(ANDROID_LOG_WARN, "chdir to the game folder failed"); }
    }

    int nKeys, nValues, nArgs;
    char** keys = to_cstrings(env, jKeys, &nKeys);
    char** values = to_cstrings(env, jValues, &nValues);
    char** args = to_cstrings(env, jArgs, &nArgs);
    jint result = -1;

    void* lib = dlopen(coreclrPath, RTLD_NOW | RTLD_LOCAL);
    if (!lib) {
        char msg[1024];
        snprintf(msg, sizeof(msg), "dlopen %s failed: %s", coreclrPath, dlerror());
        log_line(ANDROID_LOG_ERROR, msg);
        goto out;
    }
    coreclr_initialize_fn init = (coreclr_initialize_fn)dlsym(lib, "coreclr_initialize");
    coreclr_execute_assembly_fn exec = (coreclr_execute_assembly_fn)dlsym(lib, "coreclr_execute_assembly");
    coreclr_set_error_writer_fn set_writer = (coreclr_set_error_writer_fn)dlsym(lib, "coreclr_set_error_writer");
    if (!init || !exec) {
        log_line(ANDROID_LOG_ERROR, "coreclr_initialize / coreclr_execute_assembly not exported");
        goto out;
    }
    if (set_writer) set_writer(error_writer);

    void* host = NULL;
    unsigned int domain = 0;
    int hr = init(assembly, "RimWorld", nKeys < nValues ? nKeys : nValues,
                  (const char**)keys, (const char**)values, &host, &domain);
    {
        char msg[256];
        snprintf(msg, sizeof(msg), "coreclr_initialize -> 0x%08x", (unsigned)hr);
        log_line(hr < 0 ? ANDROID_LOG_ERROR : ANDROID_LOG_INFO, msg);
    }
    if (hr < 0) { result = -2; goto out; }

    // From here on the game owns the process, and when it ends, the process ends at once (_exit).
    // A normal exit() runs every library's exit-time cleanup on the game's thread while Android's
    // UI thread keeps drawing; it then locked a mutex that cleanup had destroyed and the process
    // died with SIGABRT ("pthread_mutex_lock called on a destroyed mutex") after every quit.
    // Environment.Exit calls exit(): this handler, registered after everything loaded so far, runs
    // before their cleanup and stops there. Nothing is lost: the game closes its own files first.
    atexit(exit_now);

    unsigned int exitCode = 0;
    hr = exec(host, domain, nArgs, (const char**)args, assembly, &exitCode);
    {
        char msg[256];
        snprintf(msg, sizeof(msg), "coreclr_execute_assembly -> 0x%08x, exit code %u", (unsigned)hr, exitCode);
        log_line(hr < 0 ? ANDROID_LOG_ERROR : ANDROID_LOG_INFO, msg);
    }
    result = hr < 0 ? -3 : (jint)exitCode;
    if (hr >= 0) _exit((int)exitCode);   // the game's Main returned: done, the same quick way

out:
    (*env)->ReleaseStringUTFChars(env, jCoreclr, coreclrPath);
    (*env)->ReleaseStringUTFChars(env, jAssembly, assembly);
    (*env)->ReleaseStringUTFChars(env, jLogFile, logFile);
    return result;
}
