#include <stdint.h>
#include <stdlib.h>
#include <time.h>

#if defined(__APPLE__)
#include <pthread.h>
#include <unistd.h>
#elif defined(__linux__)
#include <sys/syscall.h>
#include <unistd.h>
#endif

#if defined(__GNUC__) || defined(__clang__)
#define PROGPU_EXPORT __attribute__((visibility("default")))
#else
#define PROGPU_EXPORT
#endif

typedef intptr_t progpu_intptr;

typedef struct progpu_rect
{
    int32_t left;
    int32_t top;
    int32_t right;
    int32_t bottom;
} progpu_rect;

typedef struct progpu_point
{
    int32_t x;
    int32_t y;
} progpu_point;

PROGPU_EXPORT uint32_t GetCurrentThreadId(void)
{
#if defined(__APPLE__)
    uint64_t thread_id = 0;
    if (pthread_threadid_np(0, &thread_id) == 0)
    {
        return (uint32_t)thread_id;
    }

    return (uint32_t)(uintptr_t)pthread_self();
#elif defined(__linux__)
    return (uint32_t)syscall(SYS_gettid);
#else
    return 1;
#endif
}

PROGPU_EXPORT uint32_t GetCurrentProcessId(void)
{
#if defined(__APPLE__) || defined(__linux__)
    return (uint32_t)getpid();
#else
    return 1;
#endif
}

PROGPU_EXPORT void Sleep(uint32_t milliseconds)
{
    struct timespec requested;
    requested.tv_sec = (time_t)(milliseconds / 1000u);
    requested.tv_nsec = (long)((milliseconds % 1000u) * 1000000u);
    nanosleep(&requested, 0);
}

PROGPU_EXPORT progpu_intptr LocalFree(progpu_intptr memory)
{
    if (memory != 0)
    {
        free((void*)memory);
    }

    return 0;
}

PROGPU_EXPORT uint32_t SetErrorMode(uint32_t mode)
{
    (void)mode;
    return 0;
}

PROGPU_EXPORT int32_t SetProcessWorkingSetSize(progpu_intptr process, progpu_intptr minimum_size, progpu_intptr maximum_size)
{
    (void)process;
    (void)minimum_size;
    (void)maximum_size;
    return 1;
}

PROGPU_EXPORT progpu_intptr SetWindowsHookEx(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    (void)code;
    (void)callback;
    (void)instance;
    (void)thread_id;
    return 1;
}

PROGPU_EXPORT progpu_intptr SetWindowsHookExA(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    return SetWindowsHookEx(code, callback, instance, thread_id);
}

PROGPU_EXPORT progpu_intptr SetWindowsHookExW(int32_t code, progpu_intptr callback, progpu_intptr instance, int32_t thread_id)
{
    return SetWindowsHookEx(code, callback, instance, thread_id);
}

PROGPU_EXPORT int32_t UnhookWindowsHookEx(progpu_intptr hook)
{
    (void)hook;
    return 1;
}

PROGPU_EXPORT int32_t CallNextHookEx(progpu_intptr hook, int32_t code, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)hook;
    (void)code;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT progpu_intptr SetActiveWindow(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetFocus(void)
{
    return 0;
}

PROGPU_EXPORT progpu_intptr SetFocus(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT int32_t IsChild(progpu_intptr parent, progpu_intptr child)
{
    (void)parent;
    (void)child;
    return 0;
}

PROGPU_EXPORT int32_t IsWindow(progpu_intptr window)
{
    return window != 0;
}

PROGPU_EXPORT int32_t IsWindowVisible(progpu_intptr window)
{
    return window != 0;
}

PROGPU_EXPORT int32_t IsWindowEnabled(progpu_intptr window)
{
    return window != 0;
}

PROGPU_EXPORT int32_t BringWindowToTop(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t SetWindowPos(
    progpu_intptr window,
    progpu_intptr insert_after,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    uint32_t flags)
{
    (void)window;
    (void)insert_after;
    (void)x;
    (void)y;
    (void)width;
    (void)height;
    (void)flags;
    return 1;
}

PROGPU_EXPORT progpu_intptr SetParent(progpu_intptr child, progpu_intptr new_parent)
{
    (void)child;
    (void)new_parent;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetParent(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetTopWindow(progpu_intptr window)
{
    (void)window;
    return 0;
}

PROGPU_EXPORT progpu_intptr GetWindow(progpu_intptr window, uint32_t command)
{
    (void)window;
    (void)command;
    return 0;
}

PROGPU_EXPORT int32_t GetCursorPos(progpu_point* point)
{
    if (point != 0)
    {
        point->x = 0;
        point->y = 0;
    }

    return 1;
}

PROGPU_EXPORT int32_t GetClientRect(progpu_intptr window, progpu_rect* rect)
{
    (void)window;
    if (rect != 0)
    {
        rect->left = 0;
        rect->top = 0;
        rect->right = 0;
        rect->bottom = 0;
    }

    return 1;
}

PROGPU_EXPORT int32_t GetWindowRect(progpu_intptr window, progpu_rect* rect)
{
    return GetClientRect(window, rect);
}

PROGPU_EXPORT progpu_intptr MonitorFromRect(progpu_rect* rect, uint32_t flags)
{
    (void)rect;
    (void)flags;
    return 1;
}

PROGPU_EXPORT progpu_intptr MonitorFromWindow(progpu_intptr window, uint32_t flags)
{
    (void)window;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t GetMonitorInfo(progpu_intptr monitor, void* info)
{
    (void)monitor;
    (void)info;
    return 0;
}

PROGPU_EXPORT int32_t SendMessage(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT int32_t PostMessage(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 1;
}

PROGPU_EXPORT progpu_intptr CreateWindowEx(
    int32_t extended_style,
    const void* class_name,
    const void* window_name,
    int32_t style,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    progpu_intptr parent,
    progpu_intptr menu,
    progpu_intptr instance,
    void* parameter)
{
    (void)extended_style;
    (void)class_name;
    (void)window_name;
    (void)style;
    (void)x;
    (void)y;
    (void)width;
    (void)height;
    (void)parent;
    (void)menu;
    (void)instance;
    (void)parameter;
    return 0;
}

PROGPU_EXPORT progpu_intptr CreateWindowExW(
    int32_t extended_style,
    const void* class_name,
    const void* window_name,
    int32_t style,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    progpu_intptr parent,
    progpu_intptr menu,
    progpu_intptr instance,
    void* parameter)
{
    return CreateWindowEx(extended_style, class_name, window_name, style, x, y, width, height, parent, menu, instance, parameter);
}

PROGPU_EXPORT int32_t DestroyWindow(progpu_intptr window)
{
    (void)window;
    return 1;
}
