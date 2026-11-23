# LibreWPF NativeAOT 支持计划

状态：草案（2026-11-23）。依据：openavalon `samples/XsgNativeAot` 原型实验。M0 的三块改动（生成器、运行时注册表、Silk 平台注册）已逐个审查并提交（见附录 B），尚未发布。
相关文档：[XamlSourceGeneratorPlan.md](XamlSourceGeneratorPlan.md)、[XamlSourceGeneratorUsage.md](XamlSourceGeneratorUsage.md)。

## 0. 目标与"支持"的定义

**目标**：一个 LibreWPF 应用以 `PublishAot=true` 发布成单个原生可执行文件，在 macOS / Linux / Windows 上运行，行为与 JIT 版一致。

"支持"的验收标准（全部满足才算）：

1. 不需要 `_SuppressWpfTrimError` 之类的私有开关：SDK 提供正式的 AOT 模式。
2. 在支持的功能子集内，ILC/ILLink **零 IL 警告**；子集之外的 API 被标注（`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`），在应用代码里调用就报警告，而不是运行时才失败。
3. 主题、XAML、绑定全部走编译期路径（XSG）：运行时不加载 BAML，不经 System.Xaml。
4. CI 每个平台都有 AOT 冒烟应用（逻辑自检 + 像素对比），并有警告数量的门槛（只降不升）。

**不在范围内**：运行时 `XamlReader.Load/Parse` 和松散 BAML 在 AOT 下继续可用（这本质上是反射加载，保留给 JIT）；WinForms 互操作（`WindowsFormsHost`）；打印 / XPS。

## 1. 实验结论（原型证明了什么）

原型：`samples/XsgNativeAot`，macOS arm64，原生 Mach-O 44.6 MB，自检全部通过：

| 证明点 | 结果 |
|---|---|
| ProGPU 渲染栈 + LibreWPF 在 NativeAOT 下能启动、建窗、跑消息循环、发出 `ContentRendered` | ✅ |
| XSG 编译的主题（注册为 `PresentationFramework.Aero2`）提供 `Window`/`Button` 模板，布局正确 | ✅ 420×208 |
| 编译绑定（`x:DataType`）源→目标、INPC 更新、TwoWay 目标→源 | ✅ 不经反射 |
| BAML 主题在 AOT 下可用 | ❌ 即使 root 主题程序集，BAML 读取器也会拖入整个 System.Xaml 已知类型图，在 `XpsDocumentWriter` 上类型加载失败 |
| 屏幕像素 | ⚠️ 未验证（构建 shell 无录屏权限；`RenderTargetBitmap` 在 macOS 上 JIT 和 AOT 都依赖 Windows 的 `wpfgfx`） |

实验中逐个遇到并解决的阻塞，按出现顺序：

1. `NETSDK1168`（SDK 拒绝对 WPF 做裁剪）→ 原型用 `_SuppressWpfTrimError=true` 绕过。
2. Silk.NET 按名字加载平台程序集并用反射创建平台对象 → 已改成在 `ProGpuWpfWindowHost` 里显式注册 GLFW 平台。
3. 主题程序集无法加载导致 `Window` 没有模板、布局 0×0 → XSG 主题 + 运行时按程序集名查找生成的字典。
4. 主题程序集只作为引用（`-r:`）传给 ILC，模块初始化器不运行 → 原型用 `TrimmerRootAssembly` 手工 root。
5. 绑定的反射路径 → 编译绑定。

**警告清单**（开启 `SuppressTrimAnalysisWarnings=false`，去重后共 **308** 条；WPF SDK 默认会把它们折叠或隐藏）：

| 代码 | 数量 | 含义 |
|---|---|---|
| IL2026 | 88 | 调用了标注 `RequiresUnreferencedCode` 的成员 |
| IL2075 / IL2072 / IL2070 / IL2067 / IL2080 | 40 / 36 / 27 / 7 / 5 | 对 `Type` 做反射，但缺少 `DynamicallyAccessedMembers` |
| IL3050 / IL3051 | 30 / 17 | 需要动态代码生成（`MakeGenericType`、`Expression.Compile` 等） |
| IL2046 / IL2094 | 18 / 11 | 重写成员与基类成员的标注不一致 |
| IL2057 / IL2050 / IL2059 / IL2111 | 10 / 9 / 6 / 6 | 按字符串取 `Type.GetType`、COM 互操作、`RunClassConstructor`、反射访问 DAM 成员 |
| IL3000 / IL3002 | 4 / 7 | `Assembly.Location` / `CodeBase` 在单文件下为空或抛异常 |

IL2026 调用目标排行：`TypeDescriptor.GetConverter` **38**、`Assembly.GetType` 13、`TypeDescriptor.GetProperties` 10、`Delegate.CreateDelegate` 6，其余零星（BinaryFormatter、`JsonSerializer`、`EventSource.Write<T>` 等）。

按命名空间：`System.Windows.Markup` 36、`MS.Internal.ComponentModel` 33、`System.Xaml.Schema` 28、`System.Windows.Input` 28、`MS.Internal.Data` 21、`MS.Internal.Xaml` 18、`System.Windows.Documents` 14、`MS.Internal.Interop` 12、`System.Windows.Data` 11，以及 Automation、AppModel、`PropertyPath`（及其 Converter）、`DependencyPropertyDescriptor`、Configuration、Silk.NET.Core 等。

运行时首次异常（`XSGAOT_TRACE=1`，应用虽然跑通但被吞掉的失败）：

- `System.Configuration.ClientConfigurationHost` 缺少无参构造函数（配置系统初始化失败）；
- Silk.NET 找不到 `.deps.json`，`CodeBase` 不受支持（原生库定位）；
- `NISC18030.ttf` 格式异常（字体枚举，与 AOT 无关，但在启动路径上）。

## 1.1 进展：真实 Aero2 过生成器（2026-11-23）

把真实的 `Aero2.NormalColor.xaml`（9.6k 行）交给生成器，对真实 `PresentationFramework` 引用编译生成结果：

| | 开始 | 现在 |
|---|---|---|
| 生成代码的 C# 编译错误 | 459 | **0** |
| 未生成构造（LWXSG0008） | 171 | **0**（最后剩下的 `Style.Resources` ×2、`ControlTemplate.Resources` ×1 随后也生成了，见 1.2） |
| 未编译、保留反射的绑定（LWXSG0007） | 49 | **8**（全是 `TemplatedParent.View` 这类成员只在运行时类型上存在的路径） |

做了什么：Trigger 家族（Trigger / MultiTrigger / DataTrigger / MultiDataTrigger / EventTrigger、`EnterActions`/`ExitActions`、属性元素形式的 `Binding`）；
按属性类型转换值（含 `Rect`、`KeyTime`、`RepeatBehavior`、`PropertyPath`、`Geometry`、枚举忽略大小写）；集合/子对象属性元素按属性形状处理；
非字符串资源键（`{x:Type}`、`{x:Static}`、`{ComponentResourceKey}`、`DataTemplateKey`）；绑定在 `RelativeSource` / `ElementName` 的源类型已知时编译，
依赖属性步骤直接用 `DependencyProperty`（变更通知由 DP 系统负责），`(Owner.Property)` 路径。
运行时只在 `FrameworkElementFactory` 上加了三个只有生成代码会调用的增量方法：`SetInitializer`（让模板节点能填集合属性和对象属性）、`SetAttacher`
（`Track.Thumb`、带名字的 `RowDefinition` 仍是真正的模板节点）、`SetCreator`（`new T()`，不用 `Activator`），见 1.2 第 5 点；已在 NativeAOT 二进制里验证。

仍然是反射、且**不可能**在编译期解决的绑定：路径经过基类类型的值再取只有运行时类型才有的成员（如 `TemplatedParent.View`，`View` 属于 `ListView`）。
AOT 模式下它们应当是错误，由作者改成 `x:DataType` 或具体类型的路径；内置主题里剩下的 8 处需要在主题里改写。

### 1.2 真实 Aero2 在 NativeAOT 二进制里运行（2026-11-23）

示例（`openavalon/samples/XsgNativeAot`）现在用的是**真实的 Aero2.NormalColor.xaml**，由 XSG 编译、没有 BAML。窗口里有 10 个控件，
`compare-jit-aot.sh` 把它在 JIT（原版 BAML Aero2）和 NativeAOT（XSG Aero2）下的可视树做 diff：**两边都是 129 个元素，类型和尺寸逐个相同**，
唯一差别是窗口客户区总高度 612 对 608（原生窗口边框，窗口内部没有差别）。构造主题字典时 WPF 会对全部约 215 个模板调用 `Seal()`，也已通过。

这一轮暴露并解决的问题，按出现顺序：

1. `;` 在 .editorconfig 里是注释符，`build_property` 里的分号分隔列表被截断到第一项 → 排除列表改用逗号。
2. `System.Printing` 的可移植实现 56 个公开类型只有 1 个（上游是 C++/CLI）。ILC 把所有需要 `XpsDocumentWriter` 的方法（文档查看器）替换成抛异常的桩，
   包括整个主题的 `InitializeComponent`。**按决定 printing 保持 Windows-only**：主题用 `LibreWpfXsgExcludeTargetTypes` 排除 `DocumentViewer`、`FlowDocument*`、`FixedDocument*`。
3. 生成器把静默的字符串当成值（`Setter.Value` 是 `object`，C# 编译器不会报错）：现在"类型已知但不会转换"会报 LWXSG0008；补上
   `TextDecorationCollection`、`DoubleCollection`、`PointCollection`、`ICommand`（`NavigationCommands.X`）、`char`。
4. 模板里局部 `.Resources` 的键（`Grid.Resources`、`Style.Resources`、`ControlTemplate.Resources`）：延迟查找 + 生成时的作用域栈；一个资源看不到自己和排在它后面的同字典资源。
   （统计：523 个 `StaticResource` 引用全部指向文档里更早的顶层键，没有前向引用，所以顶层按文档顺序即时解析是对的。）
5. `<Border.Child>` 这类写成属性元素的内容属性，以及 `<Track.Thumb>`、带名字或带 `DynamicResource` 的 `RowDefinition`：必须是**真正的模板节点**（有名字、触发器可引用、`TemplateBinding` 生效），
   不能是初始化 lambda 里 `new` 出来的对象。运行时因此加了 `FrameworkElementFactory.SetAttacher`；再加 `SetInitializer`（填集合/设对象属性）和 `SetCreator`（`new T()`，
   不用 `Activator.CreateInstance`）。三个都是增量、只有生成的代码调用。
6. DataTrigger 的 `Value="True"`：`DefaultValueConverter.GetConverter` 经 BAML 已知类型表用 `Activator` 创建 `BooleanConverter`。仅在 NativeAOT 下
   （`RuntimeFeature.IsDynamicCodeSupported` 为 false）先用 `TypeDescriptor` 的内置转换器，JIT 路径不变。

仍然开放：

- ILC 还列出 `ProGPU.Wpf` 里 13 个"必定抛异常"的方法：调用了 PresentationCore 里不存在的成员（`GlyphRun` 的构造函数和 `FontSize`/`GlyphIndices`、
  `DrawingContext(ProGPU.Scene.DrawingContext)`、`DashStyle(double[], double)`、`FontFamily.NativeFont`、`BitmapSource.GpuTexture`）。哪些在真实渲染路径上，要靠像素验证才知道。
- 8 个绑定（`TemplatedParent.View` 之类，成员只在运行时类型上存在）按设计保留反射，AOT 模式应当报错。
- 其余内置主题（Classic、Fluent、AeroLite、Luna、Royale）还没有跑；每个主题的"排除哪些平台不支持的控件"需要各自一份清单。
- 屏幕像素：已验证。示例用进程自己的窗口截图（`MacWindowCapture`，进程可以不经录屏权限抓自己的窗口；`screencapture`、别的进程、DevFlow 的
  `ui/screenshot`（走 `RenderTargetBitmap`，需要 Windows 独有的 `wpfgfx`）都不行）。JIT（原版 BAML Aero2）与 AOT（XSG Aero2）的窗口内容区**任何通道的差异都不超过 1/255**；
  标题栏不同是因为 macOS 按可执行文件链接的 SDK 版本画窗口边框（JIT 的 apphost 是 SDK 15.5，AOT 可执行文件是 SDK 27.0），不是 LibreWPF 的问题。
  文字能正常画出来，说明 ILC 列出的 `GlyphRun` 相关"必抛异常"方法不在这条渲染路径上；其余几个（`BitmapSource.GpuTexture`、`DashStyle` 构造函数等）还没有被走到。
- DevFlow 代理（`LeXtudio.DevFlow.Agent.LibreWpf`）在 NativeAOT 下 HTTP 服务能起来，但所有请求失败：`System.Text.Json` 反射序列化被禁用（需要 source-generated 的 JSON 上下文）。
  它自己还不是 NativeAOT 兼容的；JIT 下可视树和动作都可用，只有 `ui/screenshot` 在 macOS 上不可用。

## 2. 架构原则

1. **AOT 应用只有一条 XAML 路径：XSG。** BAML、System.Xaml、`PresentationBuildTasks` 保留给 JIT 应用和设计器，不删除也不改语义。运行时用 feature switch 把 BAML/XAML 加载路径裁掉，而不是让它们在 AOT 下"尽量能用"。
2. **编译期做完反射原本在运行时做的事**：类型解析、TypeConverter、属性路径、资源键，都在生成器里用 Roslyn 语义模型完成，生成强类型 C#。生成器解析不了的东西必须**报诊断**，不能只生成一行 `// not generated yet` 的注释静默跳过。
3. **运行时改动保持最小、与上游兼容**：优先复用 WPF 已有的扩展点（编译绑定复用了 `PropertyDescriptor` 这个早绑定访问器，绑定引擎零改动）；必须改运行时的地方集中在少数可审查的入口（`SystemResources`、`XamlSourceGeneratedResources`、feature switch 守卫），以控制将来 rebase 上游 dotnet/wpf 的成本。
4. **在 LibreWPF 里修 LibreWPF 的问题**，不在消费者那边绕（Silk 平台注册已按此原则移入 `ProGpuWpfWindowHost`）。

## 3. 工作流

以下每个工作流都有范围、做法、退出标准。规模按 S（几天）/ M（1–2 周）/ L（数周）估计。

### WS1 SDK 与打包：正式的 AOT 模式（M）

- `LibreWPF.Sdk` 在 `PublishAot=true`（或 `IsAotCompatible`）时：
  - 要求 `UseLibreWpfXamlSourceGenerator=true`，否则报错并给出修复方法；
  - 用 SDK 自己的检查替代 `NETSDK1168`：不再借用 `_SuppressWpfTrimError`，改为 LibreWPF 自己的属性和诊断；
  - 默认打开 `LibreWpfCompiledBindings`，并把 `LWXSG0007`（绑定未能编译）**升级为错误**（见第 5 节决策 D2）；
  - 自动 root 含 XSG 主题/字典的程序集。做法：生成器给这类程序集打一个程序集特性（例如 `[assembly: XamlSourceGeneratedResourcesAttribute]`），SDK 目标在 ILC 之前扫描引用、为带该标记的程序集加 `TrimmerRootAssembly`；或者由应用侧生成器生成对每个主题注册方法的显式调用（不需要扫描，更可靠，推荐）。
  - 声明 feature switch（`RuntimeHostConfigurationOption` + `[FeatureSwitchDefinition]`/`[FeatureGuard]`）：`System.Windows.Markup.XamlRuntime.IsSupported`（`XamlReader`、`Application.LoadComponent`、`Baml2006`），`System.Windows.Documents.XamlSerialization.IsSupported`（`TextRange` 的 XAML 序列化），`System.Configuration` 路径等。AOT 模式下默认全部关闭，让 ILC 裁掉这些路径，警告随之消失。
- 打包：`CompilerVisibleProperty` 清单已在 `ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets` 中；生成器作为 analyzer 随 SDK 一起发布（不需要应用再显式引用 `LibreWPF.Xaml.SourceGenerator`）。
- 发布产物清理：原型的 publish 目录里混入了 `libprogpu_native_*.a` 静态库、FreeType 源码和许可证 `.txt` 等不应随应用发布的文件；ProGPU 原生包的 `buildTransitive` 需要区分"发布资产"和"链接资产"。
- macOS `.app` bundle、codesign、notarization 的 AOT 版本（与现有 JIT 打包共用 `Info.plist` 生成）。

**退出**：示例 csproj 只需 `PublishAot=true`，不再有 `XsgLocal.targets`、`_SuppressWpfTrimError`、`TrimmerRootAssembly`、`prepare.sh`。

### WS2 主题：内置主题改由 XSG 编译（L，计划中最大的一块）

原型里手写的主题只覆盖 `Window` 和 `Button`。真正的主题规模（从源码统计）：

| 构造 | Aero2 | Fluent¹ | 生成器现状 |
|---|---|---|---|
| `Style` / `ControlTemplate` | 136 / 215 | 882 / 860 | ✅（原型） |
| `Setter` / `Trigger` | 1299 / 256 | 6590 / 935 | ✅ |
| `{TemplateBinding}` | 529 | 3165 | ✅ |
| `{DynamicResource}` / `{StaticResource}` | 217 / 535 | 3341 / 4457 | ✅ 基本可用；StaticResource 前向引用、合并字典查找待做 |
| `{x:Static}` | 262 | 353 | ✅（类型解析需支持 `clr-namespace:…;assembly=`） |
| `RelativeSource` | 288 | 500 | ⚠️ 只支持 Self / TemplatedParent / FindAncestor |
| `MultiTrigger` / `Condition` | 76 / 165 | 460 / 485 | ❌ |
| `DataTrigger` / `MultiDataTrigger` | 15 / 76 | 60 / 30 | ❌ |
| `Storyboard` / `VisualStateManager` | 29 / 20 | 295 / 150 | ❌ |
| `MultiBinding` / `Converter=` | 7 / 12 | 40 / 60 | ❌ / ⚠️ |
| `ComponentResourceKey` | 23 | 25 | ❌ |
| `BasedOn` | 12 | 370 | ⚠️ 只支持 StaticResource 形式 |
| `Style.Resources` / `ControlTemplate.Resources` | 2 / 1 | 0 / 5 | ❌ |
| `ItemsPanelTemplate` / `DataTemplate` | 4 / 5 | 20 / 25 | ⚠️ 走通用模板路径，未单独验证 |
| 模板内 `x:Name` | 249 | 1500 | ✅ |

¹ Fluent 目录包含按控件拆开的源文件和合并后的变体，计数有重复，用来看相对比例。

主题 XAML 还引用 `theme:`（`Microsoft.Windows.Themes`，如 `ButtonChrome`）、`framework:`（`MS.Internal`，PresentationFramework 内部类型）、`ui:`（PresentationUI）。生成代码访问 internal 类型需要 `InternalsVisibleTo`，或者让生成的主题代码就编译在主题程序集内部（方案 A 天然满足）。

**方案选择**（决策 D1）：

- **方案 A（推荐，长期）**：内置主题程序集本身（`PresentationFramework.Aero2.dll` 等）改由 XSG 编译，BAML 不再进入程序集。程序集名不变，`SystemResources` 先查注册表，JIT 应用和 AOT 应用用同一份主题。需要 LibreWPF 构建（arcade）里能以 analyzer 方式运行生成器，并且要求生成器覆盖主题里的全部构造。
- **方案 B（过渡）**：保留 BAML 主题程序集；另外发布 `LibreWPF.Themes.<Name>` 包，由 XSG 编译，用 `LibreWpfXsgResourceAssemblyName` 冒充原程序集名注册（原型做法）。可以先只覆盖常用控件，AOT 应用引用它；缺失的控件样式会回落为"无模板"。

建议路线：先按 B 把 Aero2 做到完整（便于在不动 arcade 构建的情况下迭代生成器），完整后切换到 A，B 包同时退役。

**验证手段（关键）**：做一个 **BAML↔XSG 主题等价测试**。在 JIT 下同时加载 BAML 版和 XSG 版的同一主题字典，逐键比较：键集合、Style 的 TargetType/BasedOn/Setter（DP + 值）、模板树结构（FrameworkElementFactory / 模板实例化后的视觉树类型序列和属性值）、Trigger 条件和 Setter。这是确定性的自动判据，能逐条暴露生成器缺口，而不必靠肉眼看渲染结果。

**退出**：Aero2（Windows 上的默认主题）和 Classic（回退主题）由 XSG 编译，等价测试全部通过；其后是 Fluent、AeroLite、Aero、Luna、Royale。

### WS3 生成器：通用 XAML 覆盖（L，与 WS2 共享大部分工作）

应用 XAML 和主题 XAML 走的是同一个生成器。现有生成器最初按文本处理，需要系统地改成以语义模型为准：

1. **类型解析**全部走 Compilation：现有 `s_presentationTypes` 字符串表只作为快速路径；`clr-namespace:…;assembly=X`、`XmlnsDefinition` 特性映射（第三方控件库用的 `http://…` 命名空间）都要支持。
2. **内容属性**用 `[ContentProperty]`（原型已用于模板），替换 `InferContentProperty` 里按名字猜的启发式（`EndsWith("Panel")` 等）。
3. **值转换**：原型的 `LiteralCode` 已覆盖常用值类型（Thickness、Brush、Color、enum、GridLength、FontWeight……）。还需补齐：`Geometry`/`PathGeometry` 路径迷你语言（主题里大量使用）、`Transform`、`KeyTime`/`KeySpline`、`ImageSource`（pack URI）、`Cursor`、`InputGesture`/`KeyGesture`、`PropertyPath` 字面量；对 `[TypeConverter]` 标注但生成器不认识的类型，报诊断（AOT 下为错误）。
4. **资源**：`ResourceDictionary Source="…"` 在编译期解析成对生成类的直接 `new`（同项目或被引用项目），运行时不经 BAML；`x:Shared="False"`；StaticResource 前向引用和祖先查找语义（目前 `FindResource` 发生在 `Resources` 赋值之前，存在顺序问题）；考虑延迟创建资源（BAML 有 deferred content，全部即时构建会拖慢启动）。
5. **Trigger 家族**：MultiTrigger、DataTrigger、MultiDataTrigger、EventTrigger、`Condition`（含 Binding 条件，也走编译绑定）。
6. **动画与视觉状态**：Storyboard、各类 `*AnimationUsingKeyFrames`、`BeginStoryboard`、`VisualStateManager.VisualStateGroups`（附加属性 + 集合）。
7. **事件**：现有 `s_knownEvents` 是固定名单；改为通过语义模型识别 `event` 成员和路由事件（`AddHandler`），模板里的 `EventSetter`。
8. **诊断纪律**：所有"未生成"的分支变成带行号的诊断（JIT 下为警告，AOT 模式下为错误），并配单元测试。
9. **增量性**：生成器现在每次 Compilation 变化都会重新生成所有 XAML（因为组合了 `CompilationProvider`）。只取需要的符号信息（按 XAML 引用的类型名做精确查询并缓存），保证 IDE 里的输入延迟可接受。

**退出**：OpenDevelop 的 XSG 夹具、add-in 中可迁移的 XAML、全部内置主题都能生成并通过编译；"未生成"诊断计数为零。

### WS4 编译绑定：覆盖面（M）

已完成：`x:DataType` 作用域、点号属性路径、TwoWay、只读检测、`x:CompileBindings`、`LWXSG0005/0006/0007`。

待做：

- **索引器**：`Items[0]`、`Dict[key]` → 生成 `PropertyDescriptor` 风格的访问器，目前会回退到反射。
- **附加属性步骤** `(Grid.Row)` → 早绑定参数直接用 `DependencyProperty` 对象（WPF 原生支持，无需反射）。
- **类型已知的非 DataContext 源**：`RelativeSource FindAncestor, AncestorType=X` 和 `ElementName=n`（元素类型在编译期已知）可以按目标类型编译，模板里 288 处 RelativeSource 大多属于这一类。
- **MultiBinding**、`PriorityBinding`；`Converter` 走静态资源。
- **DP 源上的路径**（`Path=ActualWidth`）：绑定引擎对 DependencyObject 源走 `DependencyPropertyDescriptor`（`MS.Internal.ComponentModel` 的 33 条警告大多与此有关）。可以在生成器里直接产出 DP 对象作为路径参数。
- `INotifyDataErrorInfo` / `IDataErrorInfo` 验证路径与 `PropertyDescriptor` 访问器的组合测试。
- 运行时只读模式：AOT 下 `Binding(string path)` 的反射解析被裁掉后，给出清晰的运行时错误（而不是静默得不到值）。

**退出**：在 AOT 模式下，示例应用和 OpenDevelop 夹具里没有一处反射绑定（`LWXSG0007` = 0）。

### WS5 运行时：程序集标注与清理（L，可按程序集并行）

按依赖顺序逐个打开 `IsTrimmable` / `IsAotCompatible` 分析：WindowsBase → PresentationCore → PresentationFramework → System.Xaml（只做标注，不追求可用）→ ProGPU.*。每个程序集的目标是：AOT 子集零警告，子集之外的路径正确标注并受 feature switch 守卫。

按根因分类的处理办法（数字来自第 1 节清单）：

| 根因 | 规模 | 做法 |
|---|---|---|
| `TypeDescriptor.GetConverter`（XAML 值、`PropertyPathConverter`、`KeyTimeConverter`、默认值转换） | 38 处调用 | 为 WPF 自有类型加一层**已知转换器快速路径**（按 `Type` 分派到具体 Converter），反射路径只留给未知类型并标注；AOT 下 XAML 字面量已由 XSG 在编译期转换，运行时基本不再走这条路 |
| XAML/BAML 运行时（`System.Xaml.Schema`、`MS.Internal.Xaml`、`System.Windows.Markup`、`Baml2006`、`SystemResources` 的 BAML 分支） | ~90 | feature switch `XamlRuntime.IsSupported` 守卫入口（`XamlReader`、`LoadComponent`、`SystemResources.LoadDictionary` 的 BAML 分支），入口加 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` |
| 数据绑定反射（`MS.Internal.Data`、`System.Windows.Data`、`PropertyPath`、`DependencyPropertyDescriptor`、`MS.Internal.ComponentModel`） | ~80 | 由 WS4 的编译绑定绕开；剩余反射入口（字符串路径解析）标注 |
| 输入（`System.Windows.Input`：命令、手势转换器等） | 28 | 逐个分析；多为 `GetConverter` 和按名字找类型，改为显式表 |
| 文档（`TextRange` XAML 序列化、`TextRangeSerialization`） | 14 | feature switch 守卫（富文本复制粘贴的 XAML 格式） |
| 互操作 / UI 自动化（COM、`Marshal`） | 21 | macOS/Linux 不走；Windows 上需要源生成的 COM 互操作（`[GeneratedComInterface]`） |
| `System.Configuration`（运行时首次异常已经出现） | 5 | 找出启动路径上读配置的调用方（很可能是 `AppContext`/诊断开关），AOT 下跳过 |
| `Assembly.Location`/`CodeBase`（`IconHelper`、`ContentFilePart`、Silk.NET 原生库定位） | 11 | 改用 `AppContext.BaseDirectory`；Silk.NET 用 `NativeLibrary.SetDllImportResolver` 或预加载路径，避免依赖 `.deps.json` |
| BinaryFormatter、`JsonSerializer`、`EventSource.Write<T>` | 零星 | 剪贴板/拖放格式和遥测，feature switch 或源生成序列化 |
| 主题 `KnownTypeHelper`（`Assembly.GetType` + `RunClassConstructor`） | 2 | 方案 A 下改为显式注册 |

**退出**：以示例应用和 OpenDevelop 的一个 AOT 夹具为准，ILC 零警告（不靠全局抑制）；警告数量进入 CI 门槛。

### WS6 平台（M–L，Windows 存在风险）

- **macOS arm64**：原型已通过，是首发平台。
- **Linux x64/arm64**：同样走 ProGPU + Silk/GLFW，预计与 macOS 接近。需要在 Linux lane 上跑 AOT 冒烟。
- **Windows x64/arm64：有风险，需要先做调查。** NativeAOT 不支持 C++/CLI 混合程序集。LibreWPF 的 Windows payload 里有按架构区分的 `DirectWriteForwarder`（C++/CLI），以及 `System.Printing`、`WindowsFormsIntegration` 等。要先确认 Windows 上的 ProGPU 文本/渲染路径在启动时是否还会触达 `DirectWriteForwarder`。如果会，要么提供纯托管替代（ProGPU 已有自己的文本栈），要么 Windows 版 AOT 暂缓（决策 D3）。
- 各平台的 ProGPU 原生库（`libprogpu_native*`、`wgpu_native`、Dawn）随 AOT 产物一起部署；评估静态链接进可执行文件的可行性（发布目录里已经出现了 `.a` 文件）。

### WS7 测试与 CI（M）

- **AOT 冒烟**：把 `samples/XsgNativeAot` 的 `check` 模式变成 CI job（每个平台：publish + 运行 + 断言 `PASS`）。
- **像素验证**：用 ProGPU 现成的 headless 渲染（`ProGPU.Tests.Headless` 里的 `HeadlessWindow` 截帧），对同一窗口的 JIT 渲染和 AOT 渲染做逐像素或容差对比，补上原型没能验证的"屏幕上画对了"。
- **主题等价测试**（WS2）和**生成器单元测试**（每个新构造都有生成文本断言 + 用替身编译并运行，沿用 `CompiledBindingsTests` 的做法）。
- **警告门槛**：把 ILC 输出解析成"按代码 × 程序集"的计数，存为基线文件；PR 增加警告即失败。
- **体积门槛**：记录 AOT 可执行文件的体积（基线 44.6 MB），防止退化。

## 4. 里程碑

| 里程碑 | 内容 | 依赖 | 退出标准 |
|---|---|---|---|
| **M0 原型落地**（S） | 把本次实验的改动走 review/commit：编译绑定、生成器的 Style/模板、`XamlSourceGeneratedResources` + `SystemResources`、Silk 注册；发布一版 closure | — | 新 closure 发布后，示例不再需要 `prepare.sh` 也能通过自检 |
| **M1 SDK AOT 模式**（M） | WS1：正式 AOT 开关、主题自动 root、feature switch 骨架、发布产物清理 | M0 | 示例 csproj 只剩 `PublishAot=true`；macOS CI 冒烟变绿 |
| **M2 Aero2 完整 XSG 化（方案 B）**（L） | WS2 + WS3 中主题需要的部分：MultiTrigger、DataTrigger、Storyboard/VSM、ComponentResourceKey、`x:Static` 跨程序集、Geometry 迷你语言 | M0 | Aero2 等价测试全部通过；示例加入 TextBox、CheckBox、ComboBox、ScrollViewer、ListBox、Menu |
| **M3 运行时零警告（macOS 子集）**（L） | WS5 中 XAML 运行时 feature switch、`GetConverter` 快速路径、绑定剩余反射、Configuration、`Assembly.Location` | M1 | 示例 ILC 零警告，警告门槛进 CI |
| **M4 编译绑定完整**（M） | WS4：索引器、附加属性、ElementName/RelativeSource、MultiBinding、DP 源 | M0 | `LWXSG0007` 在示例和夹具中为 0 |
| **M5 主题切到方案 A + 其余主题**（L） | arcade 构建里用 XSG 编译内置主题；Classic、Fluent 等；`KnownTypeHelper` 显式化 | M2 | 内置主题程序集不含 BAML；JIT 应用回归（OpenDevelop 全量集成测试）通过 |
| **M6 Linux，然后 Windows**（M–L） | WS6 | M3；Windows 依赖 D3 的调查结论 | 各平台 CI 冒烟 + 像素对比通过 |

M2 与 M3、M4 可以并行（分别是生成器、运行时、绑定）。M2 工作量最大，也决定 AOT 应用能用哪些控件，应最早投入。

## 5. 需要你决定的事项

- **D1 主题交付方式**：方案 A（主题程序集本身改由 XSG 编译）作为终点，方案 B（独立的 XSG 主题包冒充原程序集名）作为过渡。建议如此，但方案 A 会改变 JIT 应用使用的主题程序集，需要你确认可以接受。
  - **切换方案 A 的把关条件**（来自 OpenDevelop v5.9.4 的教训：一个"只为新项目准备"的改动落在所有应用共享的运行时上，弄坏了与新功能无关的第三方库）：① BAML↔XSG 主题等价测试全部通过；② 对 JIT 应用的主题查找行为做回归测试（不只是注册表为空时的单元测试，要覆盖内置主题程序集真实加载的路径）；③ OpenDevelop 全量 CI 在新 closure 上通过后才切换。M0 已经按这个标准做了运行时改动：`SystemResources` 在注册表为空时与原来等价，注册表查询没有任何副作用（审查中曾发现并去掉了对每个主题程序集强制执行模块构造函数的实现）。
- **D2 AOT 模式下的失败策略**：生成器遇到无法编译的绑定或构造时，报**错误**（推荐：AOT 应用要么编译期失败，要么运行时正确）还是警告。
- **D3 Windows AOT 的优先级**：取决于 `DirectWriteForwarder` 等 C++/CLI 组件在 ProGPU 路径上是否可以避开。建议先做一次 S 级调查再排期。
- **D4 与上游的分叉程度**：WS5 需要在 WPF 源码中大量添加标注和 feature switch 守卫，会增加 rebase dotnet/wpf 的成本。可选做法：集中在少数入口守卫（改动少、警告靠 feature switch 消掉），或逐成员标注（改动多、精确）。建议以前者为主。
- **D5 运行时 XAML 在 AOT 下的去留**：`XamlReader.Load` 在 AOT 模式默认关闭（feature switch），想用的应用自行打开并承担警告。建议如此。

## 6. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 生成器覆盖的长尾（主题里的少见构造、第三方控件库的 XAML） | 推迟 M2/M5 | 等价测试逐条暴露缺口；诊断纪律保证不静默 |
| 主题全部即时构建拖慢启动 | AOT 应用启动变慢，抵消 AOT 的启动优势 | 生成延迟创建的资源（按键懒构建），对比 BAML 的 deferred content 做基准测试 |
| Windows 的 C++/CLI 依赖 | Windows AOT 不可行 | D3 先调查；必要时只发 macOS/Linux |
| 运行时标注与上游分叉 | rebase 成本 | D4：集中守卫；标注集中在少数文件 |
| 体积（44.6 MB 起步） | 分发体积 | 体积门槛；feature switch 裁掉 XAML 运行时后预计下降，需实测 |
| Silk.NET 等第三方依赖的 AOT 兼容性 | 平台差异 | 显式注册、原生库路径解析；必要时 fork 或上游 PR |

## 附录 A：复现原型

```bash
cd openavalon/samples/XsgNativeAot
./prepare.sh                     # 构建并把本地 PresentationFramework / ProGPU.Wpf 打补丁进示例私有的 packages/
dotnet publish -c Release        # PublishAot=true, osx-arm64
bin/Release/net10.0-windows/osx-arm64/publish/XsgAot check   # 输出 PASS/FAIL
```

得到警告全集：`dotnet publish -c Release -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false`。

## 附录 B：M0 的 LibreWPF 改动（已提交，未发布）

三个提交，每个都经过独立审查；审查要求的修改都已落实（生成器提交审查了五轮）。

- **生成器**（`c28c7b095`）：`XamlSourceGenerator.CompiledBindings.cs`（编译绑定；松散 ResourceDictionary 的生成与注册）、`XamlSourceGenerator.Templates.cs`（Style/Setter/Trigger、`FrameworkElementFactory` 模板树、`TemplateBinding`、按类型转换字面量）、`LibreWpfCompiledBindings` / `LibreWpfXsgResourceAssemblyName` 开关（`LibreWpfCompiledBindings` 与另两个功能开关一样默认跟随总开关，没开总开关时报 `LWXSG5001`）。诊断：`LWXSG0005`（`x:DataType` 找不到类型，错误）、`LWXSG0006`（绑定路径找不到属性，错误）、`LWXSG0007`（绑定无法编译，仍走反射，警告）、`LWXSG0008`（尚未生成的构造被漏掉，警告；第 3 节"诊断纪律"的第一步，AOT 模式下计划升级为错误）。已知限制：生成器每次编译变化都重新生成全部 XAML（WS3 第 9 点）。测试 94 个。
- **运行时注册表**（`eec98b514`）：`System.Windows.Markup.XamlSourceGeneratedResources`（公开 API，按程序集名 + 资源路径注册和查找；`ref` 同步）；`SystemResources.LoadDictionaryOrGenerated` 在加载主题程序集和 BAML 之前查询注册表。注册表为空时（所有 JIT/BAML 应用）与原行为等价，且查询没有副作用：它不加载、不初始化任何东西，生成的库靠自己的模块初始化器注册（运行时在该程序集的代码首次执行时运行它，NativeAOT 在启动时全部运行）。测试 14 个，其中一个驱动真实的 `SystemResources.ResourceDictionaries`。**未覆盖**：`assembly == null` 的按名字查找分支（NativeAOT 的原因所在）没有单元测试，它需要一个无法加载的外部主题程序集；目前只有原型示例覆盖。
- **ProGPU.Wpf**（`b5f17caac`）：`SilkPlatformRegistration`；`ProGpuWpfWindowHost` 仅在没有动态代码（NativeAOT）时显式注册 Silk 的 GLFW 平台，JIT 应用的平台发现完全不变；注册串行化，成功后才置位，失败可重试；`Silk.NET.Windowing.Glfw` / `Silk.NET.Input.Glfw` 显式声明。测试 4 个。
- 端到端：合并这三个提交后，`samples/XsgNativeAot` 在 macOS arm64 上 `PublishAot=true` 发布，原生可执行文件自检 7 项全部通过。
- 测试环境注记：`PresentationFramework.Tests` 在 macOS 上有 15 个失败（`PortableFlowDocumentTests`、`RichTextBoxTests`、`PortableWindowActivationServiceTests`，缺少原生文档/文本服务），与主题和资源无关；未在干净基线上对照，只是依据错误信息和代码路径判断。

## 附录 C：IDE（OpenDevelop）一侧的结论

- 新语法 `x:DataType` / `x:CompileBindings` **不会**使 OpenDevelop 的 WPF 设计器打不开文档：设计器用自己的 XamlDom 解析器，它跳过 XAML 命名空间里除 `x:Name` 以外的所有属性；运行时的 System.Xaml 则会拒绝它们（"Cannot set unknown member"），所以不能把设计器的行为推断给运行时，也不能反过来。语言服务同样不报告这两个指令。两条结论都有回归测试（OpenDevelop 的 `XsgProject_CompiledBindingDirectives_DoNotStopTheDesigner`，WinAppVSCE 的 `CompiledBindingDirectiveTests`）。
- 设计器和语言服务对 XSG 专用特性（Simpler XAML、`{cs:}`、编译绑定指令）的处理原则：只在项目启用了 XSG 且对应功能开关为开时才放宽，BAML 项目保持原样；运行时 System.Xaml 保持原版。
