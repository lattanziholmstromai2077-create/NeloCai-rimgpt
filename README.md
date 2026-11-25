# RimGPT

RimWorld 本地模组的维护仓库。维护范围包括自有兼容补丁、独立修复说明和验证记录。当前基线覆盖主兼容程序集 `NeloRepairDiagnostics`。构建需要本机的 RimWorld、依赖程序集和一份本地兼容源码；产物用于暂存与核验。

新会话依次阅读 [AGENTS.md](AGENTS.md) 和 [项目状态](docs/PROJECT_STATE.md)。从 [环境与构建](docs/ENVIRONMENT.md) 暂存编译，用 [验证清单](tests/README.md) 复跑方法指令比对和隔离回归。

安装或回退时先核对实际加载路径、目标文件与历史备份哈希。流程见 [日常维护](docs/MAINTENANCE.md)。
