# 主兼容程序集的源码入库范围

2026-11-26，已实施。

原维护工作区和桌面档案的早期源码哈希一致。2026-11-20 本地复审沿用 `NovemberCompatibility.cs` 与 `UapLocalization.cs`，修改了自有 `Diagnostics.cs` 的诊断开关和初始化路径。实际安装的 `NeloRepairDiagnostics.dll` 与该复审产物的 SHA256 一致。

| 文件 | 11 月 20 日来源快照的 SHA256 | 处理 |
| --- | --- | --- |
| `Diagnostics.cs` | `5BBFF9D4D85AE43F77486092D3D7CED7C5351ED0859286B8CC35FFC7A5F603E0` | 自有兼容处理入库，文本按仓库规范统一为 LF |
| `NovemberCompatibility.cs` | `34EF02DB2716A761AC4253274FAEB31E9DD843D4B71BB816F5B11EEBC6AEFC80` | 自有兼容补丁入库 |
| `UapLocalization.cs` | `B98C1938A7881D6A0A02ADF25AE99E704E2FCE95DA32A01419CAAFFDFC2022A5` | 本地输入，构建前核对快照哈希 |

两份入库文件来自 Nelo 的本机维护工作，主要使用依赖的类型、方法、定义标识符和少量精确匹配短语。UAP 桥接文件含整句第三方界面说明用于文本匹配，本次未确认这些文案的转载许可。保留本地输入可以复现现有程序集，并避免为迁移改变玩法行为。游戏和第三方程序集继续由本机安装提供。

如果后续确认转载许可，可保留作者声明后纳入桥接源码，或将匹配数据单独留在本机。按 [构建说明](../ENVIRONMENT.md) 重新验证 146 个方法，再变更当前输入策略。原文件和已部署版本的回退依据在本地 `changes/20261120-legacy-local-audit/manifest.json`。
