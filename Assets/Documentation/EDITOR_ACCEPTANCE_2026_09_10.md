# Editor 九项修复验收报告

日期：2026-09-10。范围：本轮场景整理、Inspector 展示、主题、浮动窗口、帧率设置、注解边界以及 Editor Scene/Game resize。此前仓库中的修改予以保留；本报告不将此前全部 2D 产品线目标重新宣称为完成。

## 问题 5 续修：透明接缝

首次验收对这一项的判断不充分：关闭按钮和分隔线已处理，但标题与正文仍存在透出背后文字的抗锯齿接缝。
根因是两块独立圆角背景在共用边界分别衰减 alpha，去掉 `FrameBorderSize` 无法解决。

续修改为先绘制完整、连续的窗口背景，再叠加标题色。保留圆角与抗锯齿，不追加遮盖线，不改变 Runtime/2D。
修正由 ImGui 原生构建工具生成受严格锚点验证的源码 overlay，macOS 与 Windows 共用；第三方 checkout 未修改。
Debug/Release 原生库均已重建，Editor 输出已更新；已经运行的旧进程必须重启才能加载新 native 库。

新增 6 组缩放/DPI 接缝覆盖回归：修复前全部失败，修复后全部通过；对真实 native 三角形同时采样亚像素位置
和设备像素中心。完整脚本/Inspector 测试 76 项通过，最终 UI 布局子集 10 项通过，Editor 构建 0 Warning / 0 Error。
Metal 实际浮动 Inspector 覆盖在 Hierarchy 白色文字上，已确认标题/正文接缝不再透底。

续修证据：[标题接缝原始像素特写](../../Logs/Acceptance-2026-09-10-seam/closeup.png)、[完整窗口截图](../../Logs/Acceptance-2026-09-10-seam/floating-inspector.png)。

## 逐项结果

| 项目 | 交付结果 | 验证方式 |
| --- | --- | --- |
| 1. 示例层级 | 四个空父物体 `=== CAMERAS ===`、`=== LIGHTINGS ===`、`=== OBJECTS ===`、`=== SHADOWS ===`。Camera、32 个灯光及绘制/阴影物体按组件类型归组，保留世界变换。移除旧 Square/Circle/Triangle 与 Mask Showcase 演示对象，不删除程序化图形功能。 | 实际保存并多次重新打开 SampleScene；原文件备份保留。 |
| 2. HelpBox | 独立的提示卡片：语义图标、浅色边框、状态色侧条、低对比底色、自动换行；不再是字母 `i` 加普通文字。 | 原生 ImGui 布局回归检查换行高度和无横向溢出。 |
| 3. Tooltip | 复用统一 Tooltip 样式；根据所属 viewport 工作区测量宽高，边缘自动翻转和位置约束；缩窄宽度后重新计算换行高度。Header 描述仍仅悬停显示。 | 640×480、360×240、1×/1.5× UI 缩放的右下边缘完整文本回归。 |
| 4. 深色主题 | File Browser 与 Hierarchy 共用更深的 collection 色阶，保留低对比交替行、悬停反馈和紫色选择。 | 原生 Editor 截图，包含场景根节点和资产列表。 |
| 5. 浮窗标题 | 主窗口内未停靠 Panel 也有右上角 X，走统一 Panel 开关状态；关闭按钮的命中区域覆盖标题栏。额外分隔线与背景透明接缝分别修正，后者采用连续原生窗口背景，见上方续修说明。 | 原生浮窗截图、鼠标关闭测试、6 组接缝覆盖回归。 |
| 6. 帧率 | `Edit → Settings… → Editor → Rendering` 提供 `Vertical Sync` 和 `Maximum Frame Rate`。默认 VSync 关闭、最大帧率 0（Unlimited）；Apply 后立即生效，不需重启，复用现有持久化和设置历史。 | 原生页面实际设置 30 上限，观测约 25 FPS（上限而非保证恒定帧率）；恢复 0 后约 283 FPS。软件限帧独立于固定步长模拟。 |
| 7. Transform | 增加 `World` 开关、Local/World Space 分组、悬停说明；统一 XYZ 和一位小数展示/精确输入。World 编辑通过真实世界变换 API 换算至 Local 持久数据，继续使用 SceneEdits。零缩放父节点不可逆时给出提示，Local 编辑仍可用。 | 原生开关截图；含非单位父节点的 World Position → Undo → Redo 回归。 |
| 8. 注解分层 | 新建 `Inno.Editor.Annotations`，从 Core.Serialization 移走全部 Inspector 展示注解，无旧命名空间别名。脚本使用 `InnoEditor.Annotations`；自定义注解与 Drawer 仍可扩展。 | 宿主和 IDE 脚本构建、注解元数据测试、Player IL 中不存在 Editor 程序集引用或自定义展示注解声明。 |
| 9. resize | SDL 原生 live resize 请求完整 Shell 帧，而非重放半帧 UI；Scene/Game 提取、Render Graph 执行、BGFX frame 和资源退休均继续推进。FrameBuffer 缓存按真实 attachments 匹配，不按易变化的请求标签重复创建。 | Metal 上连续拉伸主窗口和内部 Game 浮窗；鼠标未释放的两张截图帧号 352157 → 352214，时间 1291.91 → 1292.86 秒。没有 framebuffer 创建异常。 |

## HelpBox、Text、Header 和 Tooltip 的区别

- `[Text("...")]`：常驻、中性的背景说明，没有警告含义。
- `[HelpBox(...)]`：需要被注意的状态提示，区分 Info/Warning/Error，可由条件控制出现，例如未分配 Sprite 时提示哪些功能不可用。
- `[Header("标题", "说明")]`：视觉分组；第二个参数只在悬停时显示，不占正文空间。
- `[Tooltip("...")]`：某个属性的操作说明，仅悬停显示。

截图中二者相似，是原 HelpBox 绘制只输出符号和普通文字，未表现其状态语义。本轮已用同一通用控件实现语义卡片，不在 2D Drawer 中复制另一套 UI。

## resize 根因与修复边界

原生窗口拖拽期间，操作系统进入窗口跟踪循环，普通主循环暂时不能持续返回。原来的回调只重放 Editor/ImGui 绘制：它会产生新的渲染请求，却没有运行完整 Rendering Runtime 输出阶段与 BGFX 帧推进。因此请求前缀持续增长（如 `Request[8]`），目标/FrameBuffer 创建与真正资源退休失去配对，最终触及后端句柄限制；画面也只能在拖拽结束后补更新。

修复由通用平台事件连接到唯一完整帧入口。入口禁止重入，平台回调异常捕获后回到 managed 边界抛出，不能跨 native ABI 传播。没有提高 FrameBuffer 上限，没有关闭 Bloom、灯光、阴影，没有 catch 后忽略 framebuffer 失败，也没有为 Scene/Game 或某个 2D Pass 名称加入后门。

macOS 的原生窗口跟踪事件本身可能按显示刷新节奏回调；拖拽中观察到约 60 次/秒并不代表引擎重新限帧。正常运行时已记录约 366 FPS；这只是本机示例的观察值，不是 10 万实例的性能承诺。

## 主引擎源码涉及哪些职责

| 层 | 本轮主要改动 | 可复用性 |
| --- | --- | --- |
| Platform / SDL3 | `IPlatformApplication.redrawRequested`；刷新实时窗口尺寸；准备 ImGui viewport；native 回调异常转交。 | 所有产品窗口与图形后端集成，不认识 2D。 |
| Shell | 统一完整帧入口、重入保护、`FramePacingOptions`。 | Editor/Player 等 Shell 使用方的通用帧调度。 |
| Rendering / BGFX | `IRenderDevice.SetVerticalSync`；安全帧边界应用呈现设置；按 attachments 复用 FrameBuffer。 | 任意 Render Graph，包含未来 3D、离屏、后处理。 |
| Scripting | `Authoring` 导出作用域；Player 语义裁剪；项目 Game/Editor 创作代际共同退休。 | 任意脚本/Editor 扩展，不维护 2D 类型白名单。 |
| Editor | Annotations、Inspection、ImGui 主题/控件、Transform Drawer、Rendering 设置页。 | 用户 scripts 和未来插件可共用；不进入 Player runtime closure。 |
| 2D 插件 | 组件 using 更新、示例整理与保存。 | 2D 语义继续由插件所有。 |

新注解程序集只依赖 System.Attribute 与 Scripting.Api 导出声明，不引用 Serialization、Scene、Rendering、ImGui 或 Panel。`SerializableProperty` 仍只属于数据序列化；展示标注不自动赋予可序列化资格。

## 验收中发现并修正的热重载问题

真实原生重载暴露了 Editor 泛型扩展闭合于 Game 类型时的 CLR 加载器依赖：旧 Editor 普通对象已释放，但只退休 EditorScripts 上下文仍会被旧 RuntimeScripts 加载器保留。堆转储验证了该引用链。

同一项目的 GameScripts 与 EditorScripts 现作为一个创作代际共同切换；编译产物可复用，但加载上下文不能拆开退休。继续通过既有 Scene 状态事务恢复数据，继续要求 Full GC → Finalizers → Full GC 与全部弱 monitor 完成，不延长超时或跳过验证。

验证包括 Editor-only 源码改动触发的双代卸载回归；真实 Editor 从第 1 代切换到第 2 代，旧代 shadow 目录清理，Scene/Game 恢复后继续交互与渲染。

## 构建与测试

本机：Apple M2（8 核 GPU）、arm64、macOS 26.6.2 / 25G83、Metal。SDK 命令使用 `/Users/aaronliao/.dotnet/dotnet`。

| 验证 | 结果 |
| --- | --- |
| Inno.Editor.Application 构建（warnings as errors） | 0 Warning / 0 Error |
| Inno.EditorScripts IDE 项目构建（warnings as errors） | 0 Warning / 0 Error |
| Inno.Editor.Scripting.Tests | 70 Passed / 0 Failed / 0 Skipped |
| Inno.Editor.Interactions.Tests | 91 Passed / 0 Failed / 0 Skipped |
| Inno.Adapter.Rendering.Bgfx.Tests | 23 Passed / 0 Failed / 0 Skipped |
| 合计 | 184 Passed |
| 两个仓库 git diff --check | 通过 |
| 原生 Editor 完整会话 | 21:23:29 至 22:01:30，约 38 分钟；退出码 0，Dispose 完成，BGFX render thread 和设备正常退出；无 framebuffer/代际卸载异常。 |

测试覆盖还包括同一 attachment 在不同 Request/Pass 标签下复用、连续尺寸变更的资源生命周期、现有 Settings/History 事务、脚本扩展元数据和 Player 裁剪。原生验收不是只启动空窗口：实际加载整理后的 SampleScene，包含灯光、阴影、HDR/Bloom、Scene 和 Game。

## 本地证据与恢复

证据目录：[Logs/Acceptance-2026-09-10](../../Logs/Acceptance-2026-09-10)。其中保存构建/测试日志、深色样式、不限帧、World/浮动 Game、Settings 和鼠标按住期间的两张 resize 截图。该目录是本机验收产物，不作为运行时资产发布。

原场景备份：[SampleScene.before-2026-09-10-inspector-resize.iscene.bak](../../Logs/SampleScene.before-2026-09-10-inspector-resize.iscene.bak)。删除的演示物体可从该备份恢复。没有清除用户历史日志或其他未提交修改。

验证边界：本报告的原生窗口/GPU 实测为 macOS/Metal，不能外推为 Windows D3D11/D3D12/Vulkan 实机通过；也不代表此前规划的 Inno.Text 或十万实例/零分配性能门槛已在本轮验收。没有播放提示音。
