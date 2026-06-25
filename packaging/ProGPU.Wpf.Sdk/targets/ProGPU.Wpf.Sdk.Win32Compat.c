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

static progpu_intptr progpu_next_fake_handle = 2;

#define PROGPU_GWL_WNDPROC (-4)
#define PROGPU_GWL_HINSTANCE (-6)
#define PROGPU_GWL_HWNDPARENT (-8)
#define PROGPU_GWL_ID (-12)
#define PROGPU_GWL_STYLE (-16)
#define PROGPU_GWL_EXSTYLE (-20)
#define PROGPU_GWL_USERDATA (-21)
#define PROGPU_DEFAULT_STYLE ((progpu_intptr)0x10CF0000)
#define PROGPU_DEFAULT_EX_STYLE ((progpu_intptr)0x00000100)
#define PROGPU_FAKE_WINDOW_STATE_COUNT 256

typedef struct progpu_window_state
{
    progpu_intptr window;
    progpu_intptr style;
    progpu_intptr ex_style;
    progpu_intptr wnd_proc;
    progpu_intptr instance;
    progpu_intptr parent;
    progpu_intptr id;
    progpu_intptr user_data;
} progpu_window_state;

static progpu_window_state progpu_fake_window_states[PROGPU_FAKE_WINDOW_STATE_COUNT];

static progpu_intptr progpu_allocate_fake_handle(void)
{
    return progpu_next_fake_handle++;
}

static void progpu_initialize_window_state(progpu_window_state* state, progpu_intptr window)
{
    state->window = window;
    state->style = PROGPU_DEFAULT_STYLE;
    state->ex_style = PROGPU_DEFAULT_EX_STYLE;
    state->wnd_proc = 1;
    state->instance = 1;
    state->parent = 0;
    state->id = 1;
    state->user_data = 1;
}

static progpu_window_state* progpu_get_window_state(progpu_intptr window, int create)
{
    if (window == 0)
    {
        return 0;
    }

    progpu_window_state* empty = 0;
    for (int i = 0; i < PROGPU_FAKE_WINDOW_STATE_COUNT; ++i)
    {
        progpu_window_state* state = &progpu_fake_window_states[i];
        if (state->window == window)
        {
            return state;
        }

        if (empty == 0 && state->window == 0)
        {
            empty = state;
        }
    }

    if (!create || empty == 0)
    {
        return 0;
    }

    progpu_initialize_window_state(empty, window);
    return empty;
}

static progpu_intptr progpu_get_default_window_long(int32_t index)
{
    switch (index)
    {
        case PROGPU_GWL_STYLE: return PROGPU_DEFAULT_STYLE;
        case PROGPU_GWL_EXSTYLE: return PROGPU_DEFAULT_EX_STYLE;
        case PROGPU_GWL_WNDPROC: return 1;
        case PROGPU_GWL_HINSTANCE: return 1;
        case PROGPU_GWL_ID: return 1;
        case PROGPU_GWL_USERDATA: return 1;
        default: return 1;
    }
}

static progpu_intptr progpu_get_window_long_value(progpu_intptr window, int32_t index)
{
    progpu_window_state* state = progpu_get_window_state(window, 0);
    if (state == 0)
    {
        return progpu_get_default_window_long(index);
    }

    switch (index)
    {
        case PROGPU_GWL_STYLE: return state->style;
        case PROGPU_GWL_EXSTYLE: return state->ex_style;
        case PROGPU_GWL_WNDPROC: return state->wnd_proc;
        case PROGPU_GWL_HINSTANCE: return state->instance;
        case PROGPU_GWL_HWNDPARENT: return state->parent;
        case PROGPU_GWL_ID: return state->id;
        case PROGPU_GWL_USERDATA: return state->user_data;
        default: return progpu_get_default_window_long(index);
    }
}

static progpu_intptr progpu_set_window_long_value(progpu_intptr window, int32_t index, progpu_intptr value)
{
    progpu_window_state* state = progpu_get_window_state(window, 1);
    progpu_intptr previous = progpu_get_window_long_value(window, index);
    if (state == 0)
    {
        return previous;
    }

    switch (index)
    {
        case PROGPU_GWL_STYLE:
            state->style = value == 0 ? PROGPU_DEFAULT_STYLE : value;
            break;
        case PROGPU_GWL_EXSTYLE:
            state->ex_style = value == 0 ? PROGPU_DEFAULT_EX_STYLE : value;
            break;
        case PROGPU_GWL_WNDPROC:
            state->wnd_proc = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_HINSTANCE:
            state->instance = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_HWNDPARENT:
            state->parent = value;
            break;
        case PROGPU_GWL_ID:
            state->id = value == 0 ? 1 : value;
            break;
        case PROGPU_GWL_USERDATA:
            state->user_data = value == 0 ? 1 : value;
            break;
    }

    return previous;
}

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

PROGPU_EXPORT progpu_intptr GetModuleHandleW(const void* module_name)
{
    (void)module_name;
    return 1;
}

PROGPU_EXPORT progpu_intptr GetModuleHandleA(const void* module_name)
{
    return GetModuleHandleW(module_name);
}

PROGPU_EXPORT progpu_intptr GetModuleHandle(const void* module_name)
{
    return GetModuleHandleW(module_name);
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

PROGPU_EXPORT progpu_intptr DefWindowProcW(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    return 0;
}

PROGPU_EXPORT progpu_intptr DefWindowProcA(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    return DefWindowProcW(window, message, w_param, l_param);
}

PROGPU_EXPORT progpu_intptr DefWindowProc(progpu_intptr window, int32_t message, progpu_intptr w_param, progpu_intptr l_param)
{
    return DefWindowProcW(window, message, w_param, l_param);
}

PROGPU_EXPORT int16_t RegisterClassExW(void* window_class)
{
    (void)window_class;
    return 1;
}

PROGPU_EXPORT int16_t RegisterClassExA(void* window_class)
{
    return RegisterClassExW(window_class);
}

PROGPU_EXPORT int16_t RegisterClassEx(void* window_class)
{
    return RegisterClassExW(window_class);
}

PROGPU_EXPORT int32_t UnregisterClassW(const void* class_name, progpu_intptr instance)
{
    (void)class_name;
    (void)instance;
    return 1;
}

PROGPU_EXPORT int32_t UnregisterClassA(const void* class_name, progpu_intptr instance)
{
    return UnregisterClassW(class_name, instance);
}

PROGPU_EXPORT int32_t UnregisterClass(const void* class_name, progpu_intptr instance)
{
    return UnregisterClassW(class_name, instance);
}

PROGPU_EXPORT uint32_t RegisterWindowMessageW(const void* message)
{
    (void)message;
    return 0xC000u;
}

PROGPU_EXPORT uint32_t RegisterWindowMessageA(const void* message)
{
    return RegisterWindowMessageW(message);
}

PROGPU_EXPORT uint32_t RegisterWindowMessage(const void* message)
{
    return RegisterWindowMessageW(message);
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
    progpu_intptr handle = progpu_allocate_fake_handle();
    progpu_window_state* state = progpu_get_window_state(handle, 1);
    if (state != 0)
    {
        state->style = style == 0 ? PROGPU_DEFAULT_STYLE : (progpu_intptr)style;
        state->ex_style = extended_style == 0 ? PROGPU_DEFAULT_EX_STYLE : (progpu_intptr)extended_style;
        state->parent = parent;
        state->id = menu == 0 ? 1 : menu;
        state->instance = instance == 0 ? 1 : instance;
    }

    return handle;
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

PROGPU_EXPORT progpu_intptr CreateWindowExA(
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

PROGPU_EXPORT int32_t DestroyIcon(progpu_intptr icon)
{
    (void)icon;
    return 1;
}

PROGPU_EXPORT int32_t ShowWindow(progpu_intptr window, int32_t command)
{
    (void)window;
    (void)command;
    return 1;
}

PROGPU_EXPORT int32_t GetSystemMetrics(int32_t metric)
{
    switch (metric)
    {
        case 4:  return 23; /* SM_CYCAPTION */
        case 30: return 30; /* SM_CXSIZE */
        case 31: return 18; /* SM_CYSIZE */
        case 32: return 4;  /* SM_CXSIZEFRAME */
        case 33: return 4;  /* SM_CYSIZEFRAME */
        case 45: return 2;  /* SM_CXEDGE */
        case 46: return 2;  /* SM_CYEDGE */
        case 49: return 16; /* SM_CXSMICON */
        case 50: return 16; /* SM_CYSMICON */
        default: return 0;
    }
}

PROGPU_EXPORT int32_t SystemParametersInfoW(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    (void)action;
    (void)parameter;
    (void)value;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t SystemParametersInfoA(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    return SystemParametersInfoW(action, parameter, value, flags);
}

PROGPU_EXPORT int32_t SystemParametersInfo(uint32_t action, uint32_t parameter, void* value, uint32_t flags)
{
    return SystemParametersInfoW(action, parameter, value, flags);
}

PROGPU_EXPORT int32_t AdjustWindowRectEx(progpu_rect* rect, uint32_t style, int32_t has_menu, uint32_t extended_style)
{
    (void)rect;
    (void)style;
    (void)has_menu;
    (void)extended_style;
    return 1;
}

PROGPU_EXPORT int32_t ChangeWindowMessageFilter(uint32_t message, uint32_t flag)
{
    (void)message;
    (void)flag;
    return 1;
}

PROGPU_EXPORT int32_t ChangeWindowMessageFilterEx(progpu_intptr window, uint32_t message, uint32_t action, void* filter)
{
    (void)window;
    (void)message;
    (void)action;
    (void)filter;
    return 1;
}

PROGPU_EXPORT progpu_intptr GetSystemMenu(progpu_intptr window, int32_t revert)
{
    (void)window;
    (void)revert;
    return 0;
}

PROGPU_EXPORT int32_t EnableMenuItem(progpu_intptr menu, uint32_t item, uint32_t enable)
{
    (void)menu;
    (void)item;
    (void)enable;
    return 0;
}

PROGPU_EXPORT int32_t RemoveMenu(progpu_intptr menu, uint32_t position, uint32_t flags)
{
    (void)menu;
    (void)position;
    (void)flags;
    return 1;
}

PROGPU_EXPORT int32_t DrawMenuBar(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t GetWindowLong(progpu_intptr window, int32_t index)
{
    return (int32_t)progpu_get_window_long_value(window, index);
}

PROGPU_EXPORT progpu_intptr GetWindowLongPtr(progpu_intptr window, int32_t index)
{
    return progpu_get_window_long_value(window, index);
}

PROGPU_EXPORT int32_t SetWindowLong(progpu_intptr window, int32_t index, int32_t value)
{
    return (int32_t)progpu_set_window_long_value(window, index, (progpu_intptr)value);
}

PROGPU_EXPORT progpu_intptr SetWindowLongPtr(progpu_intptr window, int32_t index, progpu_intptr value)
{
    return progpu_set_window_long_value(window, index, value);
}

PROGPU_EXPORT int32_t SetClassLong(progpu_intptr window, int32_t index, int32_t value)
{
    (void)window;
    (void)index;
    return value;
}

PROGPU_EXPORT progpu_intptr SetClassLongPtr(progpu_intptr window, int32_t index, progpu_intptr value)
{
    (void)window;
    (void)index;
    return value;
}

PROGPU_EXPORT int32_t SetWindowRgn(progpu_intptr window, progpu_intptr region, int32_t redraw)
{
    (void)window;
    (void)region;
    (void)redraw;
    return 1;
}

PROGPU_EXPORT int32_t GetWindowPlacement(progpu_intptr window, void* placement)
{
    (void)window;
    (void)placement;
    return 1;
}

PROGPU_EXPORT uint32_t TrackPopupMenuEx(progpu_intptr menu, uint32_t flags, int32_t x, int32_t y, progpu_intptr window, void* parameters)
{
    (void)menu;
    (void)flags;
    (void)x;
    (void)y;
    (void)window;
    (void)parameters;
    return 0;
}

PROGPU_EXPORT int32_t SendInput(int32_t input_count, void* inputs, int32_t input_size)
{
    (void)inputs;
    (void)input_size;
    return input_count;
}

PROGPU_EXPORT progpu_intptr GetStockObject(int32_t object)
{
    (void)object;
    return 1;
}

PROGPU_EXPORT int32_t DeleteObject(progpu_intptr object)
{
    (void)object;
    return 1;
}

PROGPU_EXPORT progpu_intptr CreateSolidBrush(int32_t color)
{
    (void)color;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRectRgn(int32_t left, int32_t top, int32_t right, int32_t bottom)
{
    (void)left;
    (void)top;
    (void)right;
    (void)bottom;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRoundRectRgn(
    int32_t left,
    int32_t top,
    int32_t right,
    int32_t bottom,
    int32_t ellipse_width,
    int32_t ellipse_height)
{
    (void)left;
    (void)top;
    (void)right;
    (void)bottom;
    (void)ellipse_width;
    (void)ellipse_height;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT progpu_intptr CreateRectRgnIndirect(const progpu_rect* rect)
{
    (void)rect;
    return progpu_allocate_fake_handle();
}

PROGPU_EXPORT int32_t CombineRgn(progpu_intptr destination, progpu_intptr source1, progpu_intptr source2, int32_t mode)
{
    (void)destination;
    (void)source1;
    (void)source2;
    (void)mode;
    return 1;
}

PROGPU_EXPORT progpu_intptr SelectObject(progpu_intptr dc, progpu_intptr object)
{
    (void)dc;
    return object;
}

PROGPU_EXPORT progpu_intptr GetDC(progpu_intptr window)
{
    (void)window;
    return 1;
}

PROGPU_EXPORT int32_t ReleaseDC(progpu_intptr window, progpu_intptr dc)
{
    (void)window;
    (void)dc;
    return 1;
}

PROGPU_EXPORT int32_t GetDeviceCaps(progpu_intptr dc, int32_t index)
{
    (void)dc;
    switch (index)
    {
        case 88: /* LOGPIXELSX */
        case 90: /* LOGPIXELSY */
            return 96;
        default:
            return 0;
    }
}

PROGPU_EXPORT int32_t DwmIsCompositionEnabled(void)
{
    return 0;
}

PROGPU_EXPORT int32_t DwmGetColorizationColor(uint32_t* colorization, int32_t* opaque_blend)
{
    if (colorization != 0)
    {
        *colorization = 0xFF000000u;
    }

    if (opaque_blend != 0)
    {
        *opaque_blend = 1;
    }

    return 0;
}

PROGPU_EXPORT int32_t DwmExtendFrameIntoClientArea(progpu_intptr window, void* margins)
{
    (void)window;
    (void)margins;
    return 0;
}

PROGPU_EXPORT int32_t DwmDefWindowProc(
    progpu_intptr window,
    int32_t message,
    progpu_intptr w_param,
    progpu_intptr l_param,
    progpu_intptr* result)
{
    (void)window;
    (void)message;
    (void)w_param;
    (void)l_param;
    if (result != 0)
    {
        *result = 0;
    }

    return 0;
}

PROGPU_EXPORT void DwmSetWindowAttribute(progpu_intptr window, int32_t attribute, void* value, int32_t value_size)
{
    (void)window;
    (void)attribute;
    (void)value;
    (void)value_size;
}

PROGPU_EXPORT int32_t IsThemeActive(void)
{
    return 0;
}

PROGPU_EXPORT int32_t GetCurrentThemeName(void* theme_name, int32_t theme_name_count, void* color, int32_t color_count, void* size, int32_t size_count)
{
    (void)theme_name;
    (void)theme_name_count;
    (void)color;
    (void)color_count;
    (void)size;
    (void)size_count;
    return 0;
}

PROGPU_EXPORT void SetWindowThemeAttribute(progpu_intptr window, int32_t attribute, void* options, uint32_t options_size)
{
    (void)window;
    (void)attribute;
    (void)options;
    (void)options_size;
}

PROGPU_EXPORT int32_t GdiplusStartup(progpu_intptr* token, void* input, void* output)
{
    (void)input;
    (void)output;
    if (token != 0)
    {
        *token = 1;
    }

    return 0;
}

PROGPU_EXPORT void GdiplusShutdown(progpu_intptr token)
{
    (void)token;
}

PROGPU_EXPORT int32_t GdipCreateBitmapFromStream(void* stream, progpu_intptr* bitmap)
{
    (void)stream;
    if (bitmap != 0)
    {
        *bitmap = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipCreateHBITMAPFromBitmap(progpu_intptr bitmap, progpu_intptr* hbitmap, int32_t background)
{
    (void)bitmap;
    (void)background;
    if (hbitmap != 0)
    {
        *hbitmap = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipCreateHICONFromBitmap(progpu_intptr bitmap, progpu_intptr* hicon)
{
    (void)bitmap;
    if (hicon != 0)
    {
        *hicon = progpu_allocate_fake_handle();
    }

    return 0;
}

PROGPU_EXPORT int32_t GdipDisposeImage(progpu_intptr image)
{
    (void)image;
    return 0;
}

PROGPU_EXPORT int32_t GdipImageForceValidation(progpu_intptr image)
{
    (void)image;
    return 0;
}
