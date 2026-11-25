# 架构与源码入口

| 组成 | 在本机的已核对入口 | 作用 |
| --- | --- | --- |
| RimGPT 维护仓库 | README、项目状态、环境、验证清单 | 保存经审查的维护资料 |
| 现有本地维护档案 | 桌面 `rimgpt`，含 `changes`、`docs`、`sources` | 保存源码快照、历史构建和恢复证据 |
| 主兼容源码快照 | `sources/legacy-work/{Diagnostics,UapLocalization,NovemberCompatibility}.cs` | 本地档案记载的 Mod 入口、语言注入及兼容修复 |
| Shella 独立辅助程序集 | `sources/shella-work/NeloHarmonyContext.cs` | 本地档案记载的程序集上下文辅助 |
| 正式游戏加载目录 | `Mods/NeloLocalFixes` 与工坊模组 | 游戏运行时加载，构建输入来自本机安装 |

主兼容程序集的构建依赖 RimWorld、UnityStubs、Harmony 和 HAR。历史快照之间可能对应不同部署版本。导入前逐文件核对来源、版权与许可证，涉及第三方资产时先验证适用的许可范围。

已查到的 Axen 性能模组本地检出是单独的第三方仓库。其提交 `fa2d07054cdd630ba14336180310ad12aa6fc9a3` 包含 GPL v3 许可证文本。此项来源记录单独适用于该检出。
