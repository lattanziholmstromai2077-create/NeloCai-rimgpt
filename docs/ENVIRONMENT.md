# 本地环境与构建

| 项目 | 2026-11-26 实测 |
| --- | --- |
| Git for Windows | 2.49.0.windows.1 |
| PowerShell | 7.6.9，存在 `$PSHOME/ref` |
| 游戏 | `1.7.5014 rev1195` |
| 编译依赖 | 游戏根目录的 `Assembly-CSharp.dll`、`UnityStubs.dll`，工坊 HAR 与 Harmony 的 `1.7/Assemblies` |
| 方法比对依赖 | 本机 HAR `1.2/Assemblies/Mono.Cecil.dll` |
| 兼容回归额外依赖 | Facial Animation 工坊项 `1635901197/1.6/Assemblies/FacialAnimation.dll` |

`dotnet --list-sdks` 无输出。当前构建使用 PowerShell 的 Roslyn 编译入口和宿主自带引用程序集。

## 暂存构建

在仓库根目录用 PowerShell 7.6 或更新版本运行。下述路径适用于 Nelo 的机器，使用桌面维护档案的本机路径映射。其他机器可用实际路径替换同名参数。

```powershell
$archive = Join-Path $HOME 'Desktop/rimgpt'
$paths = Get-Content -Raw -LiteralPath (Join-Path $archive 'paths.json') | ConvertFrom-Json
$uap = Join-Path $archive 'changes/20261120-legacy-local-audit/UapLocalization.cs'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('RimGPT-' + [Guid]::NewGuid().ToString('N'))
$dll = ./tools/Build-Diagnostics.ps1 -GameRoot $paths.game -WorkshopRoot $paths.workshop -UserDataRoot $paths.user_data -UapLocalizationSource $uap -OutputDirectory $stage
```

构建命令输出暂存 DLL 的路径，并在同目录写入 `build-evidence.json`。需选择已存在的父目录下一个新目录，放在游戏、工坊和用户数据目录之外。它通过 SHA256 核验本地 UAP 源文件，所需版本及范围见 [决策记录](decisions/0001-diagnostics-source-scope.md)。

## 比对与回归

沿用上一步的变量。两个回归脚本请分别在独立 PowerShell 进程中运行，避免程序集加载和运行时补丁互相干扰。

```powershell
$installed = Join-Path $paths.game 'Mods/NeloLocalFixes/Assemblies/NeloRepairDiagnostics.dll'
$cecil = Join-Path $paths.workshop '839005762/1.2/Assemblies/Mono.Cecil.dll'
./tools/Compare-Diagnostics.ps1 -InstalledDll $installed -StagedDll $dll -CecilDll $cecil
pwsh -NoLogo -NoProfile -File ./tests/Test-DiagnosticsAudit.ps1 -FixPath $dll -GameRoot $paths.game -WorkshopRoot $paths.workshop
pwsh -NoLogo -NoProfile -File ./tests/Test-DiagnosticsCompatibility.ps1 -FixPath $dll -GameRoot $paths.game -WorkshopRoot $paths.workshop
```

部署、隔离实机和恢复见 [验证清单](../tests/README.md)。
