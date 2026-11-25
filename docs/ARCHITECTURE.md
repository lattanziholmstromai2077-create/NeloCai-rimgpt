# 架构与源码入口

| 组成 | 入口 | 作用 |
| --- | --- | --- |
| 主 Mod 入口与诊断 | [Diagnostics.cs](../src/NeloRepairDiagnostics/Diagnostics.cs) | 注册 Harmony 补丁，初始化衣物策略缓存，管理诊断开关和本地兼容处理 |
| 兼容补丁 | [NovemberCompatibility.cs](../src/NeloRepairDiagnostics/NovemberCompatibility.cs) | 桥接旧接口，处理地图状态、面部设置和只读查询 |
| UAP 本地输入 | 11 月 20 日本地复审的 `UapLocalization.cs` | 运行时本地化桥接及原界面文本匹配 |
| 构建与验证 | [环境与构建](ENVIRONMENT.md) | 暂存 DLL、比对现装方法、运行隔离回归 |
| 本机档案 | 桌面 `rimgpt` 的 `changes`、`docs`、`sources` | 保管历史源码快照、实机结果和恢复证据 |
| 正式游戏加载目录 | `Mods/NeloLocalFixes` 与 Workshop | 游戏运行与本机依赖来源 |

主兼容程序集依赖 RimWorld、UnityStubs、Harmony 和 HAR。通过反射接入可选模组，实际触发仍取决于启用列表和版本。Shella 的上下文辅助程序集及其他本地补丁分别维护。

Axen 性能模组是单独的第三方检出。其提交 `fa2d07054cdd630ba14336180310ad12aa6fc9a3` 包含 GPL v3 许可证文本。此项来源记录单独适用于该检出。
