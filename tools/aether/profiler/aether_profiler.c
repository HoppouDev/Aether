/*
 * Aether's allocation profiler: a Mono profiler (profiler API v2, Mono 6.x) that records every
 * managed allocation's class, size and managed call stack while collecting, and writes a report of
 * the allocation sites by bytes. Built by `aether` (tools/aether/profiler.py) and loaded into the
 * game with `aether run --profile`.
 *
 * The game's launcher (MonoKickstart) starts the runtime through the embedding API, which ignores
 * MONO_ENV_OPTIONS' --profile, and allocation tracking can only be turned on before the runtime has
 * started. So the library is LD_PRELOADed and registers itself from a constructor, before main, when
 * AETHER_PROFILER is set (to "" or "depth=N"). Processes without Mono (the launcher script, dialogs
 * the game starts) ignore it. It can also be loaded as a normal Mono profiler module
 * (--profile=aether[:depth=N]) where a runtime honours that.
 *
 * It collects only between aether_profiler_start() and aether_profiler_stop(), which the game's
 * control server calls (its `profile` command), so the game runs at full speed otherwise; the
 * runtime's allocation fast paths are off for the whole run, though.
 *
 * The runtime is linked into the game's launcher, which exports its API: the few functions used are
 * looked up with dlsym rather than linked, so the library loads into any process.
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef int32_t mono_bool;
typedef struct _MonoProfiler MonoProfiler;
typedef struct _MonoProfilerDesc *MonoProfilerHandle;
typedef struct _MonoObject MonoObject;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef mono_bool (*MonoStackWalk)(MonoMethod *method, int32_t native_offset, int32_t il_offset, mono_bool managed, void *data);

static MonoProfilerHandle (*mono_profiler_create)(MonoProfiler *prof);
static mono_bool (*mono_profiler_enable_allocations)(void);
static void (*mono_profiler_set_gc_allocation_callback)(MonoProfilerHandle handle, void (*cb)(MonoProfiler *prof, MonoObject *object));
static MonoClass *(*mono_object_get_class)(MonoObject *obj);
static unsigned int (*mono_object_get_size)(MonoObject *obj);
static const char *(*mono_class_get_name)(MonoClass *klass);
static const char *(*mono_class_get_namespace)(MonoClass *klass);
static void (*mono_stack_walk_no_il)(MonoStackWalk func, void *user_data);
static char *(*mono_method_full_name)(MonoMethod *method, mono_bool signature);
static void (*mono_free)(void *ptr);

/* Finds the runtime's functions in the process; false where there's no Mono. */
static int resolve_mono(void)
{
#define RESOLVE(name) \
    if (!(*(void **)&name = dlsym(RTLD_DEFAULT, #name))) { \
        return 0; \
    }
    RESOLVE(mono_profiler_create)
    RESOLVE(mono_profiler_enable_allocations)
    RESOLVE(mono_profiler_set_gc_allocation_callback)
    RESOLVE(mono_object_get_class)
    RESOLVE(mono_object_get_size)
    RESOLVE(mono_class_get_name)
    RESOLVE(mono_class_get_namespace)
    RESOLVE(mono_stack_walk_no_il)
    RESOLVE(mono_method_full_name)
    RESOLVE(mono_free)
#undef RESOLVE
    return 1;
}

#define MAX_DEPTH 32
#define TABLE_BITS 17
#define TABLE_SIZE (1u << TABLE_BITS)

struct _MonoProfiler {
    int unused;
};

/* One allocation site: a class allocated by a call stack, on the main thread or another. */
typedef struct {
    MonoClass *klass;
    MonoMethod *frames[MAX_DEPTH];
    int depth;
    int main_thread;
    uint64_t count;
    uint64_t bytes;
    int used;
} Site;

static MonoProfiler profiler;
static Site *sites;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
static volatile int collecting;
static pthread_t main_thread;
static int depth = 16;
static uint64_t dropped;
static uint64_t total_count;
static uint64_t total_bytes;
static uint64_t main_bytes;

typedef struct {
    MonoMethod **frames;
    int depth;
} Walk;

static mono_bool collect_frame(MonoMethod *method, int32_t native_offset, int32_t il_offset, mono_bool managed, void *data)
{
    Walk *walk = data;
    (void)native_offset;
    (void)il_offset;
    if (!managed || !method) {
        return 0;
    }
    walk->frames[walk->depth++] = method;
    return walk->depth >= depth;
}

static uint32_t hash_site(MonoClass *klass, MonoMethod **frames, int count, int main)
{
    uint64_t hash = 1469598103934665603ull ^ (uintptr_t)klass ^ (uint64_t)main;
    for (int i = 0; i < count; i++) {
        hash = (hash ^ (uintptr_t)frames[i]) * 1099511628211ull;
    }
    return (uint32_t)(hash ^ (hash >> 32));
}

static void on_allocation(MonoProfiler *prof, MonoObject *object)
{
    (void)prof;
    if (!collecting) {
        return;
    }
    MonoMethod *frames[MAX_DEPTH];
    Walk walk = { frames, 0 };
    mono_stack_walk_no_il(collect_frame, &walk);
    MonoClass *klass = mono_object_get_class(object);
    uint64_t size = mono_object_get_size(object);
    int main = pthread_equal(pthread_self(), main_thread);
    uint32_t slot = hash_site(klass, frames, walk.depth, main) & (TABLE_SIZE - 1);
    pthread_mutex_lock(&lock);
    total_count++;
    total_bytes += size;
    if (main) {
        main_bytes += size;
    }
    for (uint32_t probe = 0; probe < TABLE_SIZE; probe++) {
        Site *site = &sites[(slot + probe) & (TABLE_SIZE - 1)];
        if (!site->used) {
            site->used = 1;
            site->klass = klass;
            site->depth = walk.depth;
            site->main_thread = main;
            memcpy(site->frames, frames, sizeof(MonoMethod *) * walk.depth);
        } else if (site->klass != klass || site->depth != walk.depth || site->main_thread != main || memcmp(site->frames, frames, sizeof(MonoMethod *) * walk.depth) != 0) {
            continue;
        }
        site->count++;
        site->bytes += size;
        pthread_mutex_unlock(&lock);
        return;
    }
    dropped++;
    pthread_mutex_unlock(&lock);
}

/* Entry point the runtime calls for --profile=aether[:options]; options: depth=N (frames kept). */
void mono_profiler_init_aether(const char *desc)
{
    if (sites || !resolve_mono()) {
        return;
    }
    const char *options = desc ? strchr(desc, ':') : NULL;
    if (options && strncmp(options + 1, "depth=", 6) == 0) {
        depth = atoi(options + 7);
        if (depth < 1) {
            depth = 1;
        }
        if (depth > MAX_DEPTH) {
            depth = MAX_DEPTH;
        }
    }
    sites = calloc(TABLE_SIZE, sizeof(Site));
    MonoProfilerHandle handle = mono_profiler_create(&profiler);
    mono_profiler_enable_allocations();
    mono_profiler_set_gc_allocation_callback(handle, on_allocation);
    fprintf(stderr, "Aether profiler loaded (allocations, depth %d)\n", depth);
}

/* LD_PRELOAD entry: registers before the runtime starts, in the process that has Mono. */
__attribute__((constructor)) static void preload(void)
{
    const char *options = getenv("AETHER_PROFILER");
    if (!options || !dlsym(RTLD_DEFAULT, "mono_profiler_create")) {
        return;
    }
    char desc[64];
    snprintf(desc, sizeof desc, "aether:%s", options);
    mono_profiler_init_aether(desc);
    /* Not for the processes the game starts. */
    unsetenv("LD_PRELOAD");
}

/* Whether the module is loaded and ready (for the game to check before using it). */
int aether_profiler_available(void)
{
    return sites != NULL;
}

/* Forgets what was collected and starts collecting; the calling thread counts as the main thread. */
void aether_profiler_start(void)
{
    pthread_mutex_lock(&lock);
    memset(sites, 0, sizeof(Site) * TABLE_SIZE);
    dropped = total_count = total_bytes = main_bytes = 0;
    main_thread = pthread_self();
    collecting = 1;
    pthread_mutex_unlock(&lock);
}

void aether_profiler_stop(void)
{
    collecting = 0;
}

static int by_bytes(const void *a, const void *b)
{
    const Site *x = *(const Site *const *)a;
    const Site *y = *(const Site *const *)b;
    return (x->bytes < y->bytes) - (x->bytes > y->bytes);
}

/* A method's inclusive bytes: allocations with it anywhere on the stack. */
typedef struct {
    MonoMethod *method;
    uint64_t bytes;
    uint64_t count;
} Inclusive;

static int inclusive_by_bytes(const void *a, const void *b)
{
    const Inclusive *x = a;
    const Inclusive *y = b;
    return (x->bytes < y->bytes) - (x->bytes > y->bytes);
}

static void print_method(FILE *out, MonoMethod *method)
{
    char *name = mono_method_full_name(method, 1);
    fprintf(out, "%s", name ? name : "?");
    mono_free(name);
}

/*
 * Writes what was collected to path, divided by per (the number of frames collected over, for
 * per-frame figures; 1 for totals): the top sites by bytes with their stacks, then the methods
 * with the most bytes allocated under them. Returns 0, or -1 if path can't be written.
 */
int aether_profiler_report(const char *path, int top, int per)
{
    FILE *out = fopen(path, "w");
    if (!out) {
        return -1;
    }
    if (per < 1) {
        per = 1;
    }
    pthread_mutex_lock(&lock);
    int used = 0;
    for (uint32_t i = 0; i < TABLE_SIZE; i++) {
        used += sites[i].used;
    }
    Site **sorted = malloc(sizeof(Site *) * (used ? used : 1));
    int n = 0;
    for (uint32_t i = 0; i < TABLE_SIZE; i++) {
        if (sites[i].used) {
            sorted[n++] = &sites[i];
        }
    }
    qsort(sorted, n, sizeof(Site *), by_bytes);
    fprintf(out, "Allocations per %s: %llu bytes in %llu objects (main thread %llu bytes); %d sites%s\n", per > 1 ? "frame" : "run",
            (unsigned long long)(total_bytes / per), (unsigned long long)(total_count / per), (unsigned long long)(main_bytes / per), n,
            dropped ? " (table full: some dropped)" : "");
    /* Inclusive bytes per method (a hash table by method), each site counted once per method
     * however often it recurses; then compacted for sorting. */
    Inclusive *methods = calloc(TABLE_SIZE, sizeof(Inclusive));
    for (int i = 0; i < n; i++) {
        Site *site = sorted[i];
        for (int f = 0; f < site->depth; f++) {
            int seen = 0;
            for (int g = 0; g < f; g++) {
                seen |= site->frames[g] == site->frames[f];
            }
            if (seen) {
                continue;
            }
            uint32_t slot = hash_site(NULL, &site->frames[f], 1, 0) & (TABLE_SIZE - 1);
            for (uint32_t probe = 0; probe < TABLE_SIZE; probe++) {
                Inclusive *entry = &methods[(slot + probe) & (TABLE_SIZE - 1)];
                if (entry->method == NULL) {
                    entry->method = site->frames[f];
                } else if (entry->method != site->frames[f]) {
                    continue;
                }
                entry->bytes += site->bytes;
                entry->count += site->count;
                break;
            }
        }
    }
    int method_count = 0;
    for (uint32_t i = 0; i < TABLE_SIZE; i++) {
        if (methods[i].method) {
            methods[method_count++] = methods[i];
        }
    }
    qsort(methods, method_count, sizeof(Inclusive), inclusive_by_bytes);
    fprintf(out, "\nMethods by bytes allocated under them (inclusive):\n");
    for (int i = 0; i < method_count && i < top; i++) {
        fprintf(out, "%10llu B %8llu  ", (unsigned long long)(methods[i].bytes / per), (unsigned long long)(methods[i].count / per));
        print_method(out, methods[i].method);
        fprintf(out, "\n");
    }
    fprintf(out, "\nSites by bytes (class, thread, stack from the allocation outwards):\n");
    for (int i = 0; i < n && i < top; i++) {
        Site *site = sorted[i];
        const char *ns = mono_class_get_namespace(site->klass);
        fprintf(out, "\n#%d %llu B %llu objects  %s%s%s  [%s]\n", i + 1, (unsigned long long)(site->bytes / per), (unsigned long long)(site->count / per),
                ns && *ns ? ns : "", ns && *ns ? "." : "", mono_class_get_name(site->klass), site->main_thread ? "main" : "other thread");
        for (int f = 0; f < site->depth; f++) {
            fprintf(out, "    at ");
            print_method(out, site->frames[f]);
            fprintf(out, "\n");
        }
    }
    free(methods);
    free(sorted);
    pthread_mutex_unlock(&lock);
    fclose(out);
    return 0;
}
