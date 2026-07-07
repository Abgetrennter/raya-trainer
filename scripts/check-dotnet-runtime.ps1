# 检测是否安装了运行 RA3 修改器所需的 .NET 8 x86 运行库
# 需要两个运行库：Windows Desktop Runtime + ASP.NET Core Runtime（均为 x86 32位）

param(
    [switch]$Quiet,
    [switch]$LaunchAfterCheck,
    [string]$AppPath
)

$ErrorActionPreference = "Continue"
$dotnetExe = "dotnet"

# 架构提示
$archNote = @"

========================================
  RA3 修改器 - 运行库检测
========================================

本程序是 32 位 (x86) 程序，需要 **x86 版** 的 .NET 8 运行库。
x64 运行库无法被 32 位程序使用！

"@

function Test-DotNetCommand {
    try {
        $result = & $dotnetExe --list-runtimes 2>$null
        return ($LASTEXITCODE -eq 0), $result
    } catch {
        return $false, $null
    }
}

function Find-Runtime {
    param([string]$Pattern, [string]$RuntimeList)
    if (-not $RuntimeList) { return $false }
    return ($RuntimeList | Select-String -Pattern $Pattern -SimpleMatch | Where-Object { $_ -match "x86" } | Measure-Object).Count -gt 0
}

$hasDotNet, $runtimes = Test-DotNetCommand

if (-not $hasDotNet) {
    if (-not $Quiet) {
        Write-Host $archNote -ForegroundColor Yellow
        Write-Host "[错误] 未检测到任何 .NET 运行库" -ForegroundColor Red
        Write-Host ""
        Write-Host "请下载并安装以下两个运行库（均为 x86 32位）：" -ForegroundColor White
        Write-Host ""
        Write-Host "1. .NET 8.0 Windows Desktop Runtime (x86)" -ForegroundColor Cyan
        Write-Host "   下载地址: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Gray
        Write-Host "   进入页面后选择 'Windows Desktop' -> 'Run desktop apps' -> 切换到 'x86'" -ForegroundColor Gray
        Write-Host ""
        Write-Host "2. ASP.NET Core Runtime 8.0 (x86)" -ForegroundColor Cyan
        Write-Host "   同一页面，选择 'ASP.NET Core' -> 'Run server apps' -> 切换到 'x86'" -ForegroundColor Gray
        Write-Host ""
        Write-Host "或者直接下载 self-contained 版：无需安装任何运行库，解压即用" -ForegroundColor Green
        Write-Host ""
        Read-Host "按 Enter 打开下载页面"
        Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
    }
    exit 1
}

$hasDesktop = Find-Runtime -Pattern "Microsoft.WindowsDesktop.App 8." -RuntimeList $runtimes
$hasAspNetCore = Find-Runtime -Pattern "Microsoft.AspNetCore.App 8." -RuntimeList $runtimes

$missing = @()
if (-not $hasDesktop) { $missing += "Windows Desktop Runtime 8.0 (x86) - 桌面程序运行库" }
if (-not $hasAspNetCore) { $missing += "ASP.NET Core Runtime 8.0 (x86) - 网络服务运行库" }

if ($missing.Count -eq 0) {
    if (-not $Quiet) {
        Write-Host "[通过] 所有必需运行库已安装" -ForegroundColor Green
    }
    if ($LaunchAfterCheck -and $AppPath) {
        Start-Process -FilePath $AppPath
    }
    exit 0
}

if (-not $Quiet) {
    Write-Host $archNote -ForegroundColor Yellow
    Write-Host "[警告] 缺少以下运行库：" -ForegroundColor Red
    foreach ($m in $missing) {
        Write-Host "  x $m" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "已安装的运行库：" -ForegroundColor Gray
    $runtimes | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    Write-Host ""
    Write-Host "下载 .NET 8.0 (x86) 运行库：" -ForegroundColor White
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "重要提示：" -ForegroundColor Yellow
    Write-Host "  1. 页面上不要直接点大按钮下载（大按钮下载的是 x64 版）" -ForegroundColor Yellow
    Write-Host "  2. 在 'Desktop Apps' 栏，把架构切换到 'x86' 再下载" -ForegroundColor Yellow
    Write-Host "  3. 同样需要下载 'ASP.NET Core Runtime' (x86)" -ForegroundColor Yellow
    Write-Host "  4. Windows 一键整合包（如 .NET Packages AIO）通常只装 x64，不装 x86" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "或者下载 self-contained 版：GitHub Release 页中文件名带 self-contained 的 ZIP" -ForegroundColor Green
    Write-Host ""

    $choice = Read-Host "按 Enter 打开下载页面，输入 q 退出"
    if ($choice -ne "q") {
        Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
    }
}

exit 2
