# 本地环境与构建

## 已检查的环境

| 项目 | 2026-11-26 结果 |
| --- | --- |
| Git for Windows | 2.49.0.windows.1 |
| PowerShell | 7.6.9，存在 `$PSHOME/ref` |
| dotnet | 已安装运行时，`dotnet --list-sdks` 无输出 |
| 游戏 | 磁盘版本 `1.7.5014 rev1195` |
| 构建依赖 | 游戏根目录 `Assembly-CSharp.dll` 和 `UnityStubs.dll`，工坊 AlienRace.dll（839005762）及 0Harmony.dll（2009463077）的 `1.7/Assemblies` 内版本 |

## 本机位置

在 Nelo 的机器上，维护档案为 `$HOME/Desktop/rimgpt`，游戏为 `E:/SteamLibrary/steamapps/common/RimWorld`，工坊根为 `E:/SteamLibrary/steamapps/workshop/content/294100`。跨机器使用时按实际安装位置调整。完整游戏配置与验证证据留在本机维护档案。

## 暂存构建

以下命令复用本地旧构建脚本的三份 C# 源码、引用布局与 Roslyn 编译参数。需在 PowerShell 7 中运行。输出使用独立临时目录。

```powershell
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $HOME 'Desktop/rimgpt/sources/legacy-work'
$gameRoot = 'E:/SteamLibrary/steamapps/common/RimWorld'
$workshopRoot = 'E:/SteamLibrary/steamapps/workshop/content/294100'
$references = @(
    "$gameRoot/Assembly-CSharp.dll"
    "$gameRoot/UnityStubs.dll"
    "$workshopRoot/839005762/1.7/Assemblies/AlienRace.dll"
    "$workshopRoot/2009463077/1.7/Assemblies/0Harmony.dll"
)
$sources = @(
    "$sourceRoot/Diagnostics.cs"
    "$sourceRoot/UapLocalization.cs"
    "$sourceRoot/NovemberCompatibility.cs"
)
foreach ($file in ($references + $sources)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing build input: $file"
    }
}
$references += (Get-ChildItem -LiteralPath "$PSHOME/ref" -Filter '*.dll' -File).FullName
$stage = Join-Path ([IO.Path]::GetTempPath()) ('RimGPT-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$output = Join-Path $stage 'NeloRepairDiagnostics.dll'
Add-Type -Path $sources -ReferencedAssemblies $references -OutputAssembly $output -OutputType Library -CompilerOptions /nowarn:1701
[Reflection.AssemblyName]::GetAssemblyName($output)
Get-FileHash -LiteralPath $output -Algorithm SHA256
```

暂存 DLL 的编译与游戏内加载是两项独立检查。部署、日志对比及恢复见 [验证清单](../tests/README.md)。
