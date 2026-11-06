/*
 * Aether's allocation profiler: a CoreCLR profiler (the ICorProfilerCallback interface, .NET 10)
 * that records every managed allocation's class, size and managed call stack while collecting, and
 * writes a report of the allocation sites by bytes. Built by `aether` (tools/aether/profiler.py)
 * and loaded with the runtime's profiler variables (CORECLR_ENABLE_PROFILING, CORECLR_PROFILER,
 * CORECLR_PROFILER_PATH) by `aether run --profile`.
 *
 * It collects only between aether_profiler_start() and aether_profiler_stop(), which the game's
 * control server calls (its `profile` command, through Aether.Testing.NativeProfiler), so the game
 * runs at full speed otherwise; the runtime's allocation fast paths are off for the whole run,
 * though, since allocation events can only be enabled at startup.
 *
 * Written in C without the runtime's headers: COM interfaces are tables of function pointers, so
 * the profiler implements ICorProfilerCallback3 as a table whose every slot returns S_OK (it only
 * enables the events it handles), and calls ICorProfilerInfo4 and IMetaDataImport methods by their
 * slot numbers, taken from dotnet/runtime's corprof.idl and cor.h (v10.0.0).
 */
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef int32_t HRESULT;
typedef uint32_t ULONG;
typedef uintptr_t UINT_PTR;
typedef UINT_PTR ObjectID, ClassID, FunctionID, ModuleID, ThreadID;
typedef uint32_t mdToken;
typedef uint16_t WCHAR;

typedef struct {
    uint32_t a;
    uint16_t b, c;
    uint8_t d[8];
} GUID;

#define S_OK 0
#define S_FALSE 1
#define E_NOINTERFACE ((HRESULT)0x80004002)
#define CLASS_E_CLASSNOTAVAILABLE ((HRESULT)0x80040111)

static const GUID IID_IUnknown = { 0x00000000, 0x0000, 0x0000, { 0xC0, 0, 0, 0, 0, 0, 0, 0x46 } };
static const GUID IID_IClassFactory = { 0x00000001, 0x0000, 0x0000, { 0xC0, 0, 0, 0, 0, 0, 0, 0x46 } };
static const GUID IID_ICorProfilerCallback = { 0x176FBED1, 0xA55C, 0x4796, { 0x98, 0xCA, 0xA9, 0xDA, 0x0E, 0xF8, 0x83, 0xE7 } };
static const GUID IID_ICorProfilerCallback2 = { 0x8A8CC829, 0xCCF2, 0x49fe, { 0xBB, 0xAE, 0x0F, 0x02, 0x22, 0x28, 0x07, 0x1A } };
static const GUID IID_ICorProfilerCallback3 = { 0x4FD2ED52, 0x7731, 0x4b8d, { 0x94, 0x69, 0x03, 0xD2, 0xCC, 0x30, 0x86, 0xC5 } };
static const GUID IID_ICorProfilerInfo4 = { 0x0d8fdcaa, 0x6257, 0x47bf, { 0xb1, 0xbf, 0x94, 0xda, 0xc8, 0x84, 0x66, 0xee } };
static const GUID IID_IMetaDataImport = { 0x7dac8207, 0xd3ae, 0x4c75, { 0x9b, 0x67, 0x92, 0x80, 0x1a, 0x49, 0x7d, 0x44 } };
/* This profiler's class id: CORECLR_PROFILER={9A1E2F5C-3B7D-4E8A-A6C2-5D0F1B3E7A41}. */
static const GUID CLSID_AetherProfiler = { 0x9A1E2F5C, 0x3B7D, 0x4E8A, { 0xA6, 0xC2, 0x5D, 0x0F, 0x1B, 0x3E, 0x7A, 0x41 } };

/* Event mask: allocations (COR_PRF_MONITOR_OBJECT_ALLOCATED | COR_PRF_ENABLE_OBJECT_ALLOCATED) and
 * stack snapshots (COR_PRF_ENABLE_STACK_SNAPSHOT). */
#define EVENT_MASK (0x00000100u | 0x00800000u | 0x10000000u)

/* Slots: ICorProfilerCallback3 (QueryInterface 0, AddRef 1, Release 2, Initialize 3,
 * ObjectAllocated 50; 97 in all), ICorProfilerInfo4 and IMetaDataImport. */
#define CALLBACK_SLOTS 83
#define SLOT_INITIALIZE 3
#define SLOT_OBJECT_ALLOCATED 50
#define INFO_GET_CLASS_FROM_OBJECT 3
#define INFO_IS_ARRAY_CLASS 11
#define INFO_GET_CLASS_ID_INFO 14
#define INFO_GET_FUNCTION_INFO 15
#define INFO_SET_EVENT_MASK 16
#define INFO_GET_MODULE_METADATA 21
#define INFO_DO_STACK_SNAPSHOT 36
#define INFO_GET_OBJECT_SIZE2 80
#define MD_GET_TYPEDEF_PROPS 12
#define MD_GET_METHOD_PROPS 30
#define MD_GET_NESTED_CLASS_PROPS 62

/* A COM object: a pointer to its table of functions. */
typedef struct {
    void **vtable;
} Object;

#define METHOD(object, slot, type) ((type)((Object *)(object))->vtable[slot])

static Object *info;

#define MAX_DEPTH 32
#define TABLE_BITS 17
#define TABLE_SIZE (1u << TABLE_BITS)

/* One allocation site: a class allocated by a call stack, on the main thread or another. */
typedef struct {
    ClassID klass;
    FunctionID frames[MAX_DEPTH];
    int depth;
    int main_thread;
    uint64_t count;
    uint64_t bytes;
    int used;
} Site;

static Site *sites;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
static volatile int collecting;
static pthread_t main_thread;
static int depth = 16;
static uint64_t dropped;
static uint64_t total_count;
static uint64_t total_bytes;
static uint64_t main_bytes;

static int same_guid(const GUID *x, const GUID *y)
{
    return memcmp(x, y, sizeof(GUID)) == 0;
}

/* --- Collecting -------------------------------------------------------------------------------- */

typedef struct {
    FunctionID *frames;
    int depth;
} Walk;

static HRESULT collect_frame(FunctionID function, UINT_PTR ip, UINT_PTR frame_info, uint32_t context_size, uint8_t *context, void *data)
{
    Walk *walk = data;
    (void)ip;
    (void)frame_info;
    (void)context_size;
    (void)context;
    if (function == 0) {
        return S_OK; /* a native frame */
    }
    walk->frames[walk->depth++] = function;
    return walk->depth >= depth ? S_FALSE : S_OK;
}

static uint32_t hash_site(ClassID klass, const FunctionID *frames, int count, int main)
{
    uint64_t hash = 1469598103934665603ull ^ (uint64_t)klass ^ (uint64_t)main;
    for (int i = 0; i < count; i++) {
        hash = (hash ^ (uint64_t)frames[i]) * 1099511628211ull;
    }
    return (uint32_t)(hash ^ (hash >> 32));
}

typedef HRESULT (*DoStackSnapshotFn)(Object *, ThreadID, void *, uint32_t, void *, uint8_t *, uint32_t);
typedef HRESULT (*GetObjectSize2Fn)(Object *, ObjectID, size_t *);

static HRESULT object_allocated(Object *self, ObjectID object, ClassID klass)
{
    (void)self;
    if (!collecting) {
        return S_OK;
    }
    FunctionID frames[MAX_DEPTH];
    Walk walk = { frames, 0 };
    METHOD(info, INFO_DO_STACK_SNAPSHOT, DoStackSnapshotFn)(info, 0, (void *)collect_frame, 0, &walk, NULL, 0);
    size_t size = 0;
    METHOD(info, INFO_GET_OBJECT_SIZE2, GetObjectSize2Fn)(info, object, &size);
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
            memcpy(site->frames, frames, sizeof(FunctionID) * walk.depth);
        } else if (site->klass != klass || site->depth != walk.depth || site->main_thread != main || memcmp(site->frames, frames, sizeof(FunctionID) * walk.depth) != 0) {
            continue;
        }
        site->count++;
        site->bytes += size;
        pthread_mutex_unlock(&lock);
        return S_OK;
    }
    dropped++;
    pthread_mutex_unlock(&lock);
    return S_OK;
}

/* --- The ICorProfilerCallback3 object ---------------------------------------------------------- */

static HRESULT unused_callback(void)
{
    return S_OK;
}

static HRESULT callback_query_interface(Object *self, const GUID *iid, void **out)
{
    if (same_guid(iid, &IID_IUnknown) || same_guid(iid, &IID_ICorProfilerCallback) || same_guid(iid, &IID_ICorProfilerCallback2) || same_guid(iid, &IID_ICorProfilerCallback3)) {
        *out = self;
        return S_OK;
    }
    *out = NULL;
    return E_NOINTERFACE;
}

static ULONG add_ref(Object *self)
{
    (void)self;
    return 1;
}

static ULONG release(Object *self)
{
    (void)self;
    return 1;
}

typedef HRESULT (*QueryInterfaceFn)(Object *, const GUID *, void **);
typedef HRESULT (*SetEventMaskFn)(Object *, uint32_t);

static HRESULT initialize(Object *self, Object *runtime)
{
    (void)self;
    if (METHOD(runtime, 0, QueryInterfaceFn)(runtime, &IID_ICorProfilerInfo4, (void **)&info) != S_OK) {
        fprintf(stderr, "Aether profiler: no ICorProfilerInfo4\n");
        return E_NOINTERFACE;
    }
    HRESULT result = METHOD(info, INFO_SET_EVENT_MASK, SetEventMaskFn)(info, EVENT_MASK);
    if (result != S_OK) {
        fprintf(stderr, "Aether profiler: SetEventMask failed (0x%08x)\n", (unsigned)result);
        return result;
    }
    const char *options = getenv("AETHER_PROFILER");
    if (options && strncmp(options, "depth=", 6) == 0) {
        depth = atoi(options + 6);
        depth = depth < 1 ? 1 : depth > MAX_DEPTH ? MAX_DEPTH : depth;
    }
    sites = calloc(TABLE_SIZE, sizeof(Site));
    fprintf(stderr, "Aether profiler loaded (allocations, depth %d)\n", depth);
    return S_OK;
}

static void *callback_vtable[CALLBACK_SLOTS];
static Object callback = { callback_vtable };

/* --- The class factory and the runtime's entry point ------------------------------------------- */

static HRESULT factory_query_interface(Object *self, const GUID *iid, void **out)
{
    if (same_guid(iid, &IID_IUnknown) || same_guid(iid, &IID_IClassFactory)) {
        *out = self;
        return S_OK;
    }
    *out = NULL;
    return E_NOINTERFACE;
}

static HRESULT factory_create_instance(Object *self, Object *outer, const GUID *iid, void **out)
{
    (void)self;
    (void)outer;
    for (int i = 0; i < CALLBACK_SLOTS; i++) {
        callback_vtable[i] = (void *)unused_callback;
    }
    callback_vtable[0] = (void *)callback_query_interface;
    callback_vtable[1] = (void *)add_ref;
    callback_vtable[2] = (void *)release;
    callback_vtable[SLOT_INITIALIZE] = (void *)initialize;
    callback_vtable[SLOT_OBJECT_ALLOCATED] = (void *)object_allocated;
    return callback_query_interface(&callback, iid, out);
}

static HRESULT factory_lock_server(Object *self, int lock_server)
{
    (void)self;
    (void)lock_server;
    return S_OK;
}

static void *factory_vtable[5] = { (void *)factory_query_interface, (void *)add_ref, (void *)release, (void *)factory_create_instance, (void *)factory_lock_server };
static Object factory = { factory_vtable };

__attribute__((visibility("default"))) HRESULT DllGetClassObject(const GUID *clsid, const GUID *iid, void **out)
{
    if (!same_guid(clsid, &CLSID_AetherProfiler)) {
        *out = NULL;
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    return factory_query_interface(&factory, iid, out);
}

/* --- Control, from the game ---------------------------------------------------------------------- */

/* Whether the profiler was loaded and is ready (for the game to check before using it). */
__attribute__((visibility("default"))) int aether_profiler_available(void)
{
    return sites != NULL;
}

/* Forgets what was collected and starts collecting; the calling thread counts as the main thread. */
__attribute__((visibility("default"))) void aether_profiler_start(void)
{
    pthread_mutex_lock(&lock);
    memset(sites, 0, sizeof(Site) * TABLE_SIZE);
    dropped = total_count = total_bytes = main_bytes = 0;
    main_thread = pthread_self();
    collecting = 1;
    pthread_mutex_unlock(&lock);
}

__attribute__((visibility("default"))) void aether_profiler_stop(void)
{
    collecting = 0;
}

/* --- Names --------------------------------------------------------------------------------------- */

typedef HRESULT (*GetModuleMetaDataFn)(Object *, ModuleID, uint32_t, const GUID *, Object **);
typedef HRESULT (*GetTypeDefPropsFn)(Object *, mdToken, WCHAR *, ULONG, ULONG *, uint32_t *, mdToken *);
typedef HRESULT (*GetMethodPropsFn)(Object *, mdToken, mdToken *, WCHAR *, ULONG, ULONG *, uint32_t *, const void **, ULONG *, ULONG *, uint32_t *);
typedef HRESULT (*GetNestedClassPropsFn)(Object *, mdToken, mdToken *);
typedef HRESULT (*GetClassIDInfoFn)(Object *, ClassID, ModuleID *, mdToken *);
typedef HRESULT (*GetFunctionInfoFn)(Object *, FunctionID, ClassID *, ModuleID *, mdToken *);
typedef HRESULT (*IsArrayClassFn)(Object *, ClassID, int32_t *, ClassID *, ULONG *);

/* Metadata readers by module, kept for the report. */
#define MODULES 4096
static ModuleID module_ids[MODULES];
static Object *module_imports[MODULES];
static int module_count;

static Object *metadata(ModuleID module)
{
    for (int i = 0; i < module_count; i++) {
        if (module_ids[i] == module) {
            return module_imports[i];
        }
    }
    Object *import = NULL;
    if (METHOD(info, INFO_GET_MODULE_METADATA, GetModuleMetaDataFn)(info, module, 0, &IID_IMetaDataImport, &import) != S_OK) {
        import = NULL;
    }
    if (module_count < MODULES) {
        module_ids[module_count] = module;
        module_imports[module_count++] = import;
    }
    return import;
}

/* Appends a UTF-16 name to out as UTF-8 (the names are identifiers: no surrogate pairs needed). */
static void append_utf8(char *out, size_t size, const WCHAR *text)
{
    size_t length = strlen(out);
    for (; *text && length + 4 < size; text++) {
        WCHAR c = *text;
        if (c < 0x80) {
            out[length++] = (char)c;
        } else if (c < 0x800) {
            out[length++] = (char)(0xC0 | (c >> 6));
            out[length++] = (char)(0x80 | (c & 0x3F));
        } else {
            out[length++] = (char)(0xE0 | (c >> 12));
            out[length++] = (char)(0x80 | ((c >> 6) & 0x3F));
            out[length++] = (char)(0x80 | (c & 0x3F));
        }
    }
    out[length] = 0;
}

/* A type definition's full name (Namespace.Outer+Inner) into out. */
static void type_name(Object *import, mdToken type, char *out, size_t size)
{
    mdToken outer = 0;
    if (METHOD(import, MD_GET_NESTED_CLASS_PROPS, GetNestedClassPropsFn)(import, type, &outer) == S_OK && outer != 0) {
        type_name(import, outer, out, size);
        append_utf8(out, size, (const WCHAR *)u"+");
    }
    WCHAR name[512];
    ULONG length = 0;
    uint32_t flags = 0;
    mdToken extends = 0;
    if (METHOD(import, MD_GET_TYPEDEF_PROPS, GetTypeDefPropsFn)(import, type, name, 512, &length, &flags, &extends) == S_OK) {
        append_utf8(out, size, name);
    } else {
        append_utf8(out, size, (const WCHAR *)u"?");
    }
}

static void class_name(ClassID klass, char *out, size_t size)
{
    int32_t element_type = 0;
    ClassID element = 0;
    ULONG rank = 0;
    if (METHOD(info, INFO_IS_ARRAY_CLASS, IsArrayClassFn)(info, klass, &element_type, &element, &rank) == S_OK) {
        if (element != 0) {
            class_name(element, out, size);
        } else {
            append_utf8(out, size, (const WCHAR *)u"?");
        }
        append_utf8(out, size, rank > 1 ? (const WCHAR *)u"[,]" : (const WCHAR *)u"[]");
        return;
    }
    ModuleID module = 0;
    mdToken type = 0;
    Object *import = NULL;
    if (METHOD(info, INFO_GET_CLASS_ID_INFO, GetClassIDInfoFn)(info, klass, &module, &type) == S_OK && (import = metadata(module)) != NULL) {
        type_name(import, type, out, size);
    } else {
        append_utf8(out, size, (const WCHAR *)u"?");
    }
}

static void print_method(FILE *out, FunctionID function)
{
    ClassID klass = 0;
    ModuleID module = 0;
    mdToken token = 0;
    Object *import = NULL;
    char name[1024] = "";
    if (METHOD(info, INFO_GET_FUNCTION_INFO, GetFunctionInfoFn)(info, function, &klass, &module, &token) == S_OK && (import = metadata(module)) != NULL) {
        mdToken type = 0;
        WCHAR method[512];
        ULONG length = 0;
        uint32_t attributes = 0, impl = 0;
        const void *signature = NULL;
        ULONG signature_length = 0, rva = 0;
        if (METHOD(import, MD_GET_METHOD_PROPS, GetMethodPropsFn)(import, token, &type, method, 512, &length, &attributes, &signature, &signature_length, &rva, &impl) == S_OK) {
            type_name(import, type, name, sizeof name);
            append_utf8(name, sizeof name, (const WCHAR *)u":");
            append_utf8(name, sizeof name, method);
        }
    }
    fprintf(out, "%s", name[0] ? name : "?");
}

/* --- The report ------------------------------------------------------------------------------------ */

static int by_bytes(const void *a, const void *b)
{
    const Site *x = *(const Site *const *)a;
    const Site *y = *(const Site *const *)b;
    return (x->bytes < y->bytes) - (x->bytes > y->bytes);
}

/* A method's inclusive bytes: allocations with it anywhere on the stack. */
typedef struct {
    FunctionID method;
    uint64_t bytes;
    uint64_t count;
} Inclusive;

static int inclusive_by_bytes(const void *a, const void *b)
{
    const Inclusive *x = a;
    const Inclusive *y = b;
    return (x->bytes < y->bytes) - (x->bytes > y->bytes);
}

/*
 * Writes what was collected to path, divided by per (the number of frames collected over, for
 * per-frame figures; 1 for totals): the methods with the most bytes allocated under them, then the
 * top sites by bytes with their stacks. Returns 0, or -1 if path can't be written.
 */
static int write_report(const char *path, int top, int per)
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
            uint32_t slot = hash_site(0, &site->frames[f], 1, 0) & (TABLE_SIZE - 1);
            for (uint32_t probe = 0; probe < TABLE_SIZE; probe++) {
                Inclusive *entry = &methods[(slot + probe) & (TABLE_SIZE - 1)];
                if (entry->method == 0) {
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
        char name[1024] = "";
        class_name(site->klass, name, sizeof name);
        fprintf(out, "\n#%d %llu B %llu objects  %s  [%s]\n", i + 1, (unsigned long long)(site->bytes / per), (unsigned long long)(site->count / per), name,
                site->main_thread ? "main" : "other thread");
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

typedef struct {
    const char *path;
    int top;
    int per;
    int result;
} Report;

static void *report_thread(void *data)
{
    Report *report = data;
    report->result = write_report(report->path, report->top, report->per);
    return NULL;
}

/*
 * Writes the report (see write_report) from a thread of its own: .NET refuses metadata lookups
 * from a managed thread outside a profiler callback, but not from a thread it doesn't know.
 */
__attribute__((visibility("default"))) int aether_profiler_report(const char *path, int top, int per)
{
    Report report = { path, top, per, -1 };
    pthread_t thread;
    if (pthread_create(&thread, NULL, report_thread, &report) != 0) {
        return -1;
    }
    pthread_join(thread, NULL);
    return report.result;
}
