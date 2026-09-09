# 导出与依赖边界

> **关联代码** | [FYAsset](../../Assets/FYAsset/Scripts/) · [边界场景](../../tests/scenario/s3_resource_boundary/)

AA、AB 与 Shared 的目录划分表达职责目标，不等于将目录复制到空工程后即可编译。当前没有完成独立空工程导出验证；源码词法门禁也不能替代依赖闭包、程序集和资源 GUID 验证。

## 需要携带的依赖

| 部分 | 依赖 |
|---|---|
| AA | Shared、Addressables、通用文件/Hash/Serialization 工具及实际引用的公共基础类 |
| AB | Shared、通用文件/Hash/Serialization 工具及实际引用的公共基础类；Editor 采集配置另行携带 |
| Compat | 宿主后端选择、门面、Lua 接入、Cloudflare 接入，以及当前项目的双后端序列化注册/生成清单 |
| Tests | 专门的构建/E2E测试入口，不是 Compat 正式运行代码 |
| XLuaFramework | XLua 与其自身依赖；运行时资源由 ILuaAssetLoader 注入 |

`Compat/Serialization` 当前组合注册 AA 与 AB 根类型，适合本项目双后端集成。单后端导出不能直接照搬双后端注册入口，需要保留对应类型的注册与生成结果。通用 Serialization 工具本身不反向引用 FYAsset。

## 已知边界限制

Shared 的 PublishTargetPanel 当前直接更新 AA/AB Settings，现存 ExportBoundary 场景会报告此引用。不能在文档宣称 Shared 已完全无后端依赖；本轮不通过放宽测试来掩盖它。

Collector 配置资产引用 AB 脚本 GUID；AA 导出不应无条件携带。移动文件必须保留对应 .meta，目录 .meta 也需随归属调整。仅确认文件存在不证明资源引用全部正确。

## 验证顺序

1. 源码互引门禁、缺失路径和重复 GUID 检查。
2. 在独立空工程验证各自依赖闭包及 Unity 编译。
3. 分别生成与读取 Manifest，验证初始化注册顺序。
4. 再验证真实资源加载、构建和发布。

历史场景结果、未完成验收与本轮命令记录保留在 requirements；本说明不把任何未执行步骤标为通过。
