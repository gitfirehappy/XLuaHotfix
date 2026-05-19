## 序列化模块草稿计划（草案）

> **Status**: Archived — 2026-05-19; superseded by S1/S2/S3/S4 serialization plans and implemented infrastructure

目标
--
- 为资源管理链路（Build ↔ Snapshot ↔ Runtime）提供一套轻量且可扩展的序列化工具层，作为“通用工具”（类似 HashGenerator），不与业务模块反向耦合。
- 初始目标：统一 `ABManifest` / `version_state.json` / `manifest.json` / `BuildIndex.json` 的读写入口，保证可插拔 codec（JSON / Binary / Protobuf / MessagePack），并保留向后兼容策略。

背景与动机
--
- 当前代码库在多处直接使用 `JsonUtility.FromJson/ToJson` 与 `File.ReadAllText/WriteAllText`（BuildProjectManager、LocalStatusExporter、HotfixManager、ABManifest 等）。
- 若要支持二进制导出或替换底层 JSON 库（例如不再使用 `JsonUtility`），需要一个统一契约与兼容层，避免格式漂移、重复实现与发布回滚难以追踪的问题。

设计要点（高层）
--
1. SerializationEnvelope（线序化包头）
   - 目的：在磁盘/网络上存放可自识别的二进制/文本包，包含：Magic(4 bytes)、FormatVersion(uint16)、CodecId(string 或 enum)、SchemaId(string，可选)、Flags、PayloadLength、Payload
   - 优点：便于检测格式，快速回退/兼容性判断，支持多 codec 共存

2. ISserializationCodec（接口）
   - C# 接口示例（草案）:
     - interface ISerializationCodec {
         byte[] Serialize<T>(T obj);
         T Deserialize<T>(byte[] bytes);
       }
   - 提供 JsonCodec（基于 JsonUtility）作为默认实现；后续提供 MessagePackCodec/ProtobufCodec/MemoryPackCodec 等。

3. SerializationUtility（工具类）
   - 提供高层 API：
     - byte[] Serialize(object obj, string codecId)
     - T Deserialize<T>(byte[] data)
     - void WriteToFile<T>(string path, T obj, string codecId)
     - T ReadFromFile<T>(string path)
   - 默认 codec 为 json (JsonCodec)，文件后缀建议：.json 或 .bin，可通过 header 自动识别

4. Schema / Version 管理
   - 每种重要数据类型（ABManifest, VersionState, BuildIndex）在初期都记录 schemaId（例如 "ABManifest:v1"），用于兼容性检查与日志追踪。
   - 维护一个轻量的 schema registry 文档（repository 内的 YAML/MD），描述字段要点与不可兼容变更流程。

集成点（非侵入式接入）
--
- BuildProjectManager.GenerateVersionStateFile：将 `JsonUtility.ToJson(...)` 调用替换为 `SerializationUtility.WriteToFile(versionStatePath, versionState, codecId)` （phase 1 使用 codecId="json"）
- LocalStatusExporter.ExportBuildIndex：同上
- ABManifest.DeserializeFromJson / SerializeToJson：新增 `DeserializeFromFile(path)` / `SerializeToFile(path, codec)` 的 wrapper，保留现有 API 作为兼容方法
- HotfixManager.StepDownloadManifestAsync / StepCheckVersionAsync：将 `JsonUtility.FromJson` 替换为 `SerializationUtility.ReadFromFile<Manifest>(path)`，并在读取失败时降级回文本 JSON 解析作为最后防线（仅在兼容阶段）
- DifferentialProcessor / ConfirmRelease：在 ConfirmRelease 流程中增加序列化兼容性校验（确保 runtime 能读 build 输出）

分阶段迁移计划（建议）
--
Phase 0 — 评审与 schema 列表（短）
 - 输出：requirements/refactor-2026/plan-serialization.md（本文件）与 context/schema-registry.md 草案
 - 目标：确定需要被统一的所有数据类型（ABManifest, VersionState, BuildIndex, Manifest 等）与初版 schemaId

Phase 1 — 工具层与 JSON 封装（短，1-2 人日）
 - 实现：SerializationUtility + ISerializationCodec + JsonCodec（内部使用 JsonUtility）
 - 替换点：把 BuildProjectManager 与 LocalStatusExporter 写入/读取抽象到工具；保持文件格式不变（即兼容现有 JSON 文件）
 - 验证：单元测试（round-trip），CI 在 ConfirmRelease 前运行验证任务

Phase 2 — 运行期读取适配（中，1-2 人日）
 - 在 HotfixManager 与 AssetPackageManager 的 manifest 加载处，调用工具层读取
 - 若读取失败或 header 指示非 JSON，则正确返回错误并记录版本/codec信息

Phase 3 — 引入 Binary Codec（MessagePack/Protobuf）（中，2-3 人日）
 - 提供 MessagePackCodec 或 ProtobufCodec（可选），并在 DifferentialProcessor 的导出阶段增加写二进制的选项
 - ConfirmRelease 内置兼容性校验（runtime 对 build 的二进制能读）

Phase 4 — 逐步替换/回滚与运维（逐步）
 - 采用 Feature-flag 控制导出格式，先对小型包或非关键资源启用二进制
 - 收集兼容性 telemetry（可选），若问题立即回滚并修补 codec

测试计划
--
- 单元测试：各 codec 的 round-trip（Serialize->Deserialize），edge case（null、字段缺失、额外字段）
- 兼容测试：读取旧版 JSON/二进制并断言核心字段存在
- CI Gate：在 ConfirmRelease 步骤加入序列化兼容性检查（测试 runtime loader 能读 build 输出）

估算与交付物
--
- Phase1（工具 + JSON 包装）: 1–2 人日（含单元测试）
- Phase2（运行时接入）: 1 人日
- Phase3（Binary codec）: 2–3 人日（依赖选定第三方库）
- 文档：schema registry 草案、审查清单、回滚步骤（半天）

风险与注意事项
--
- 不要把业务逻辑（如 AssetPackageManager 内部索引）写进序列化库；序列化库只负责 wire-format
- 避免在初期做大范围替换：分阶段、feature-flag、按子系统逐步迁移
- 性能：二进制格式提升读取性能，但要注意内存分配与平台兼容（IL2CPP、WebGL）

审批清单（用于 requirements/ 下的审批）
--
- [ ] 同意引入 SerializationUtility 工具层（默认 codec = json）
- [ ] 同意在 BuildProjectManager 中先以非破坏性方式调用工具（保持现有 JSON 文件结构）
- [ ] 确认二进制 codec（MessagePack 或 Protobuf）优先级与第三方依赖选型

下一步建议
--
1. 若认可，生成 `requirements/refactor-2026/plan-serialization.md`（已生成）并发起审批清单（ask_user）。
2. 我可以继续生成代码骨架（SerializationUtility + ISerializationCodec + JsonCodec）并写入 `Assets/Tools/Serialization/`，或先等待你的确认/审查后再写入仓库。

附录：示意 C# 接口（草案）
--
```csharp
public interface ISerializationCodec
{
    byte[] Serialize<T>(T obj);
    T Deserialize<T>(byte[] payload);
    string CodecId { get; }
}

public static class SerializationUtility
{
    // 默认 json
    public static void WriteToFile<T>(string path, T obj, string codecId = "json") { /* ... */ }
    public static T ReadFromFile<T>(string path) { /* detect header / codecId -> delegate to codec */ }
}
```
