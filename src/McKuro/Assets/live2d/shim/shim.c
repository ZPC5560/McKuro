/*
 * shim.c — Cubism 4 → Cubism 5 的 API 名称桥接层
 *
 * 背景:
 *   McKuro 使用的 C# 绑定 (Live2DCSharpSDK.Framework) 是针对 Cubism 4 写的,
 *   其 P/Invoke EntryPoint 写死为 csmGetDrawableRenderOrders。
 *   但随包的「心」模型是 moc3 version 5,只有 Cubism 5 的 Core 能读取
 *   (Cubism 4 R7 的 csmHasMocConsistency 对该模型返回 0)。
 *
 *   Cubism 5 把这个 API 改名成了 csmGetRenderOrders,于是 C# 绑定在 C5 上
 *   抛 EntryPointNotFoundException。
 *
 * 做法:
 *   把 Cubism 5 的静态库 libLive2DCubismCore.a 整个链入本 dylib,
 *   再额外导出一个 csmGetDrawableRenderOrders —— 转发到 csmGetRenderOrders。
 *
 * 为什么可以简单转发(语义等价性已实测确认):
 *   两者签名完全一致:const int* f(const csmModel*)
 *   都返回「按 drawable 下标排列的绘制优先级数组」,长度 = csmGetDrawableCount()。
 *
 *   但有一处内存所有权差异必须处理:
 *     · Cubism 4:返回数组位于模型结构内部,调用方可直接改写并跨 csmUpdateModel 保留
 *       (McKuro 的 RepairDrawableRenderOrder 正依赖这一点修复第三方导出器的全 0 值);
 *     · Cubism 5:返回模型尾部的独立缓冲,实测每次 csmUpdateModel 后指针会变,
 *       直接透传会让调用方的改写丢失。
 *
 *   因此本层维护一份「按 model 指针缓存」的稳定缓冲:首次调用复制 C5 的初值,
 *   之后始终返回同一块内存,调用方写入得以跨帧保留。
 *
 * 适用范围:
 *   仅当 Core 缺少 C4 老符号时使用。Windows 那份定制 Core 自带
 *   csmGetDrawableRenderOrders,不走本层。
 *
 * 导出清单(C# 侧对应 Sparkle.Live2DView/Live2DCoreShim.cs):
 *   csmGetDrawableRenderOrders      —— C4 兼容转发 + 稳定缓冲
 *   mckuroForgetRenderOrderCache    —— 模型释放时丢弃该模型的稳定缓冲(防地址复用串味)
 *
 * ⚠ 修改本文件后必须用 build-live2d-shim.sh 在 macOS/Linux 上重建随包的
 *   libLive2DCubismCore.dylib / .so,否则新增导出不会生效(C# 侧会自动降级为 no-op)。
 */

#include <stddef.h>
#include <stdlib.h>
#include <string.h>

/* 与官方头文件保持一致的声明(只声明本层用到的部分,避免依赖 SDK include 路径) */
typedef struct csmModel csmModel;

extern const int* csmGetRenderOrders(const csmModel* model);
extern int csmGetDrawableCount(const csmModel* model);

#if defined(_WIN32)
#define SHIM_EXPORT __declspec(dllexport)
#else
#define SHIM_EXPORT __attribute__((visibility("default")))
#endif

/* 每个 model 的稳定缓冲。模型数量极少(通常 1),用小型链表即可,避免引入额外依赖。 */
typedef struct ShimEntry
{
    const csmModel* model;
    int* buffer;
    int capacity;
    struct ShimEntry* next;
} ShimEntry;

static ShimEntry* g_entries = NULL;

/* 单线程假设:Core 的模型访问本来就不是线程安全的,渲染全部在 UI 线程。 */
static ShimEntry* FindEntry(const csmModel* model)
{
    for (ShimEntry* e = g_entries; e != NULL; e = e->next)
    {
        if (e->model == model)
        {
            return e;
        }
    }
    return NULL;
}

/*
 * 丢弃某个 model 的缓存条目(C# 在模型释放后调用)。
 *
 * 为什么必须显式丢弃:缓存以 model 指针为键且永不失效,而 malloc 会在模型销毁后
 * 复用同一块地址 —— 新模型若恰好落在旧地址上,FindEntry 会命中旧条目并直接返回
 * 旧缓冲(连"首次复制 Core 初值"都跳过),于是新模型继承上一个模型的 renderOrder。
 * 表现是换模型后图层顺序错乱(极端情况下出现越界下标),且只在地址复用时出现,极难定位。
 */
SHIM_EXPORT void mckuroForgetRenderOrderCache(const csmModel* model)
{
    ShimEntry** link = &g_entries;
    while (*link != NULL)
    {
        ShimEntry* entry = *link;
        if (entry->model == model)
        {
            *link = entry->next;
            free(entry->buffer);
            free(entry);
            return;
        }
        link = &entry->next;
    }
}

SHIM_EXPORT const int* csmGetDrawableRenderOrders(const csmModel* model)
{
    if (model == NULL)
    {
        return NULL;
    }

    const int* source = csmGetRenderOrders(model);
    int count = csmGetDrawableCount(model);
    if (count <= 0)
    {
        return source;
    }

    ShimEntry* entry = FindEntry(model);
    if (entry != NULL && entry->capacity >= count)
    {
        return entry->buffer;
    }

    /* 首次见到该 model,或 drawable 数量增长:重新分配并复制初值 */
    if (entry == NULL)
    {
        entry = (ShimEntry*)calloc(1, sizeof(ShimEntry));
        if (entry == NULL)
        {
            return source;   /* 分配失败:退化为直接透传,不阻断渲染 */
        }
        entry->model = model;
        entry->next = g_entries;
        g_entries = entry;
    }

    int* grown = (int*)realloc(entry->buffer, (size_t)count * sizeof(int));
    if (grown == NULL)
    {
        return source;
    }
    entry->buffer = grown;
    entry->capacity = count;

    if (source != NULL)
    {
        memcpy(entry->buffer, source, (size_t)count * sizeof(int));
    }
    else
    {
        /* Core 未给出初值:填恒等排列(下标顺序即 moc3 的 drawable 顺序) */
        for (int i = 0; i < count; i++)
        {
            entry->buffer[i] = i;
        }
    }

    return entry->buffer;
}
