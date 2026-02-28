# Unity XLua热更新框架项目介绍

## 项目简介
独立开发的一套基于Unity Addressables与XLua的高效热更新框架。实现从资源构建、版本差异管理到运行时Lua与C#深度互调的完整业务流，包含自动化工具链及组件化开发架构，大幅提升热更新迭代效率与运行性能。

## 技术亮点

### 1. 自动化差异快照热更构建管线
*   **痛点解决**：传统热更需人工管理资源分组，易漏配且风险高。
*   **实现方案**：开发基于差异对比的构建工具链（`DifferentialProcessor`）。通过计算项目资源的深度哈希并与线上（Head）快照比对，自动识别变更资源并将其移入Hotfix专属Addressables分组。
*   **成果**：实现热更资源的自动化隔离与快照状态轮转（Staged -> Head），构建过程0人工干预，确保增量更新准确无误。

### 2. 高效的运行时资源调度与缓存管理
*   **痛点解决**：Addressables原生接口使用繁琐，且缺乏针对Lua脚本的细粒度缓存与释放机制。
*   **实现方案**：
    *   **资源索引封装**：封装`AAPackageManager`，在构建期导出Type/Label到Keys的映射，运行时支持按标签或类型进行批量高效查询。
    *   **引用计数池**：内部维护基于引用计数的资源池，安全管理异步加载句柄（Handle）的释放。
    *   **Lua定制加载器**：自定义`XLuaLoader`，支持Editor/Addressables/Hybrid三态加载。运用“懒加载索引+本地字节流缓存”策略，极大提升`require`效率，并支持按Lua脚本容器（`LuaScriptContainer`）整体释放内存缓存。

### 3. 组件化Lua-C#桥接与生命周期同步架构
*   **痛点解决**：Lua与Unity原生MonoBehaviour生命周期割裂，且强耦合会导致跨语言通信开销大。
*   **实现方案**：
    *   **生命周期桥接**：设计`LuaBehaviourBridge`组件挂载于GameObject，按固定且严谨的顺序（SO -> Input -> Physics2D -> Anim -> UI等）异步注入各子系统桥接组件。
    *   **模块解耦**：缓存`Update/Awake`等高频调用的Lua函数指针，避免每帧反射或字符串查表开销。支持Class（面向对象实例化）和Module（静态）两种Lua脚本模式。

### 4. 健壮的跨语言事件中心（EventCentre）
*   **痛点解决**：Lua注册C#事件后，由于Lua闭包的不可预测性，常导致反注册失败、内存泄漏或重复触发。
*   **实现方案**：构建四向互通端口（C#-C#, Lua-Lua, C#-Lua, Lua-C#）的事件系统。在Lua端注册事件时，底层自动生成对应的C# Delegate，并以`Tuple(端口, 事件名, Lua函数指针)`为键缓存到字典（`luaDelegateMap`）中。注销时复用该Delegate，彻底解决跨语言委托注销无法匹配的难题。

### 5. 跨语言协程调度与性能优化实践
*   **协程双向桥接**：实现`CoroutineBridge`，允许在Lua中优雅等待C#异步结果，或在C#中阻塞等待Lua协程，内部自动维护等待关系链并清理过期关联（`CSharpCoroutineScheduler` / `LuaCoroutineScheduler`）。
*   **内存优化基建**：深度运用XLua底层的`ObjectPool`（基于Freelist的免分配池）与`ObjectTranslatorPool`，减少跨语言互调时的Wrapper装箱（Boxing）与对象分配，配合项目中总结的Struct GC优化指南，严格控制运行时堆内存增长。

### 6. 完善的编辑器自动化工具链
*   **效率提升**：开发大量Editor工具，包括但不限于：Lua文件批量转TextAsset后缀（`LuaBatchConverterWindow`）、可视化跨语言事件调试器（`EventViewerWindow`）、以及配置格式（CSV/JSON/XML）一键转换工具，全面覆盖从代码编写到打包发布的各个开发环节。