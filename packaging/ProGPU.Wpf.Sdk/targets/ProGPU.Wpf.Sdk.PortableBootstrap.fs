// The F# form of ProGPU.Wpf.Sdk.PortableBootstrap.cs (keep the two in step). F# has no module initializer attribute, and an executable with an explicit
// [<EntryPoint>] initializes the files of its assembly only when something in them is first used: so the start is a function, `ensure ()`, that is
// idempotent. The classes the XAML generator emits for F# call it before they build their tree, and a program without XAML calls it first in main.
module ProGpu.Wpf.Sdk.PortableBootstrap

[<Sealed; AllowNullLiteral>]
type internal Marker() = class end

#if PROGPU_WPF_USE_LIBREWINFORMS || PROGPU_WPF_BOOTSTRAP_WPF
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private initializeWindowsForms (enableNativeModalSessions: bool) : unit =
#if PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS
    if enableNativeModalSessions then
        LibreWinForms.ProGPU.ProGpuPlatform.Register(enableNativeModalSessions = true)
    else
        LibreWinForms.ProGPU.ProGpuPlatform.Register()
#else
    ignore enableNativeModalSessions
#endif
#if PROGPU_WPF_BOOTSTRAP_WPF && PROGPU_WPF_USE_LIBREWINFORMS
    System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop()
#endif
#endif

#if PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS && !PROGPU_WPF_BOOTSTRAP_WPF
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private initializeFormsOnly () : unit =
    let args = System.Environment.GetCommandLineArgs()
    let startup = LibreWinForms.ProGPU.ProGpuStartupOptions.ParseArguments(System.ReadOnlySpan<string>(args, 1, args.Length - 1))
    initializeWindowsForms startup.EnableNativeModalSessions
#endif

#if PROGPU_WPF_BOOTSTRAP_WPF
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private readWpfStartupOptions () : bool =
    let args = System.Environment.GetCommandLineArgs()
    System.Windows.Media.ProGPU.ProGpuWpfStartupOptions.ParseArguments(System.ReadOnlySpan<string>(args, 1, args.Length - 1)).EnableNativeModalSessions

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private initializeWpf (enableNativeModalSessions: bool) : unit =
#if PROGPU_WPF_NATIVE_MIL
    let architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
    if architecture <> System.Runtime.InteropServices.Architecture.X64 && architecture <> System.Runtime.InteropServices.Architecture.Arm64 then
        raise (System.PlatformNotSupportedException("LibreWPF NativeMilWgpu requires an x64 or ARM64 desktop process and its matching ProGPU native payload."))
    System.Windows.Media.ProGPU.ProGpuWpfNativeMediaServices.Initialize()
#endif
#if PROGPU_WPF_USE_LIBREWINFORMS
    initializeWindowsForms enableNativeModalSessions
#endif
    System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof<System.Windows.Application>.Module.ModuleHandle)
    System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof<System.Windows.Clipboard>.Module.ModuleHandle)
#if PROGPU_WPF_NATIVE_MIL
    let registered =
        System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(fun window ->
            let options = System.Windows.Media.ProGPU.ProGpuWpfWindowOptions()
            options.RendererMode <- System.Windows.Media.ProGPU.ProGpuWpfRendererMode.NativeMilWgpu
            options.EnableNativeModalSessions <- enableNativeModalSessions
#if PROGPU_WPF_NATIVE_MIL_HIT_TESTING
            options.EnableNativeMilHitTesting <- true
#endif
            new System.Windows.Media.ProGPU.ProGpuWpfWindowHost(System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(window, options)))
    if not registered then
        raise (System.InvalidOperationException("LibreWPF NativeMilWgpu requires the source-built typed WPF activation service."))
#else
    if enableNativeModalSessions then
        let registered =
            System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(fun window ->
                let options = System.Windows.Media.ProGPU.ProGpuWpfWindowOptions()
                options.EnableNativeModalSessions <- enableNativeModalSessions
                new System.Windows.Media.ProGPU.ProGpuWpfWindowHost(System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(window, options)))
        if not registered then
            raise (System.InvalidOperationException("Explicit native modal startup requires the source-built typed WPF activation service."))
    else
        System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation() |> ignore
#endif
    System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService() |> ignore
#endif

let mutable private started = false
let mutable private starting = false
let private startLock = obj ()

let private initialize () : unit =
    // An executable can also be a project reference of a server or test host: only the entry assembly owns automatic desktop startup.
    if obj.ReferenceEquals(System.Reflection.Assembly.GetEntryAssembly(), typeof<Marker>.Assembly) then
#if PROGPU_WPF_BOOTSTRAP_WPF
        let enableNativeModalSessions = readWpfStartupOptions ()
#if !PROGPU_WPF_NATIVE_MIL
        if System.OperatingSystem.IsWindows() then
            initializeWindowsForms enableNativeModalSessions
        else
            initializeWpf enableNativeModalSessions
#else
        initializeWpf enableNativeModalSessions
#endif
#elif PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS
        initializeFormsOnly ()
#else
        ()
#endif

/// Starts the LibreWPF desktop platform once, when this assembly is the entry assembly. Safe to call from anywhere, any number of times.
let ensure () : unit =
    if not started then
        // Under a lock, and the flag is set only once initialize () has succeeded: a failure is raised again on the next call, as the C# module initializer
        // raises it, instead of being swallowed; a second thread waits for the first instead of initializing twice.
        // (starting: initialize () may create a generated class, whose constructor calls ensure again on this thread.)
        lock startLock (fun () ->
            if not started && not starting then
                starting <- true
                try
                    initialize ()
                    started <- true
                finally
                    starting <- false)
