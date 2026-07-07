using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

try
{
    var options = SmokeOptions.Parse(args);
    if (options.ShowHelp)
    {
        SmokeOptions.PrintHelp();
        return 0;
    }

    Console.WriteLine($"启动器: {options.LauncherPath}");
    Console.WriteLine($"启动参数: {options.LauncherArguments}");
    Console.WriteLine($"目标程序: {options.TargetProcess}");
    Console.WriteLine($"分析目录: {options.AnalysisDirectory}");
    Console.WriteLine($"等待超时: {options.TimeoutSeconds}s");
    if (options.CompatibilitySample)
    {
        Console.WriteLine($"兼容性采样: before={options.CompatibilitySampleBefore}, after={options.CompatibilitySampleAfter}");
    }
    if (options.MonitorSeconds > 0)
    {
        Console.WriteLine($"注入后稳定性监控: {options.MonitorSeconds}s");
    }
    if (options.AgentProbe || options.AgentFeatureScan || options.AgentGameApiSmoke || options.AgentRuntimeFeatureSmoke)
    {
        Console.WriteLine($"DLL Agent: {options.AgentDllPath}");
    }

    var manifest = TrainerRuntimeAssets.LoadManifest();
    var imageVerified = false;
    if (options.ImageOnly)
    {
        return SmokeDiagnostics.PrintImageHookVerification(manifest, options.TargetProcess) ? 0 : 5;
    }

    if (options.VerifyImageHooks && File.Exists(options.TargetProcess))
    {
        imageVerified = true;
        if (!SmokeDiagnostics.PrintImageHookVerification(manifest, options.TargetProcess))
        {
            return 5;
        }
    }

    if (options.StartLauncher)
    {
        if (string.IsNullOrWhiteSpace(options.LauncherPath))
        {
            Console.Error.WriteLine("启动器路径为空：请传入 --launcher <path>，或省略 --launcher 只等待已有目标进程。");
            return 2;
        }

        var gameLauncher = new GameLauncher();
        var launcher = Path.GetExtension(options.LauncherPath).Equals(".game", StringComparison.OrdinalIgnoreCase)
            ? gameLauncher.StartCommandLine(
                $"\"{Path.GetFullPath(options.LauncherPath)}\" {options.LauncherArguments}".TrimEnd(),
                Path.GetDirectoryName(Path.GetFullPath(options.LauncherPath))
                    ?? Environment.CurrentDirectory)
            : gameLauncher.Start(options.LauncherPath, options.LauncherArguments);
        Console.WriteLine($"已启动启动器进程 PID={launcher.Id}");
    }
    else
    {
        Console.WriteLine("已跳过启动器启动，仅等待目标进程。");
    }

    var featureScanItems = FeatureScanPlanner.Create(manifest.Features);
    var diagnosticIterations = options.AgentFeatureScan
        ? featureScanItems.Count(item => item.CanScan) + 1
        : 1;
    var runBudget = DiagnosticTimeoutBudget.Calculate(
        options.TimeoutSeconds,
        options.MonitorSeconds,
        diagnosticIterations,
        includeIterationOverhead: options.AgentFeatureScan);
    Console.WriteLine($"诊断总超时预算: {runBudget.TotalSeconds:0}s");
    using var cancellation = new CancellationTokenSource(runBudget);
    var locator = new TrainerProcessLocator();
    var waiter = new GameProcessWaiter(locator.Find);
    var target = await waiter.WaitForAsync(
        options.TargetProcess,
        TimeSpan.FromSeconds(options.TimeoutSeconds),
        cancellation.Token);

    if (target is null)
    {
        Console.Error.WriteLine($"超时：{options.TimeoutSeconds}s 内未检测到 {options.TargetProcess}。");
        SmokeDiagnostics.PrintSnapshot(locator.Snapshot(), options.TargetProcess);
        return 3;
    }

    SmokeDiagnostics.PrintTarget(target);
    SmokeDiagnostics.PrintSnapshot(locator.Snapshot(), options.TargetProcess);
    if (options.VerifyImageHooks && !imageVerified)
    {
        if (!SmokeDiagnostics.PrintImageHookVerification(manifest, target.ModulePath, target))
        {
            return 5;
        }
    }

    if (options.DumpHooks)
    {
        SmokeDiagnostics.PrintHookBytes(target, manifest);
    }

    if (options.CompatibilitySample)
    {
        if (!SmokeDiagnostics.PrintCompatibilitySample(
            target,
            manifest,
            options.AnalysisDirectory,
            new CompatibilitySampleOptions(
                options.CompatibilitySampleBefore,
                options.CompatibilitySampleAfter),
            options.CompatibilitySampleOutput))
        {
            return 7;
        }
    }

    if (!target.Is32Bit || !target.VersionSupported)
    {
        var profileId = target.VersionProfileId ?? "(未识别)";
        Console.Error.WriteLine($"检测失败：目标进程不是受支持的 RA3 32 位进程（profile={profileId}）。");
        return 4;
    }

    if (options.AgentProbe || options.AgentGameApiSmoke || options.AgentRuntimeFeatureSmoke)
    {
        if (!await SmokeDiagnostics.ProbeAgentAsync(
            target,
            manifest,
            options.AnalysisDirectory,
            options.AgentDllPath,
            options.AgentGameApiSmoke,
            options.AgentRuntimeFeatureSmoke,
            TimeSpan.FromSeconds(options.MonitorSeconds),
            cancellation.Token))
        {
            return 8;
        }
    }

    if (options.AgentFeatureScan)
    {
        if (!await SmokeDiagnostics.ProbeAgentFeaturesAsync(
            target,
            manifest,
            options.AnalysisDirectory,
            options.AgentDllPath,
            TimeSpan.FromSeconds(options.MonitorSeconds),
            cancellation.Token))
        {
            return 9;
        }
    }

    Console.WriteLine("Smoke 测试通过：启动器可拉起目标游戏进程，且目标进程通过基础检测。");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

internal sealed record SmokeOptions(
    string LauncherPath,
    string LauncherArguments,
    string TargetProcess,
    string AnalysisDirectory,
    int TimeoutSeconds,
    bool StartLauncher,
    bool DumpHooks,
    bool VerifyImageHooks,
    bool ImageOnly,
    bool AgentProbe,
    bool AgentFeatureScan,
    bool AgentGameApiSmoke,
    bool AgentRuntimeFeatureSmoke,
    string AgentDllPath,
    bool CompatibilitySample,
    string? CompatibilitySampleOutput,
    int CompatibilitySampleBefore,
    int CompatibilitySampleAfter,
    int MonitorSeconds,
    bool ShowHelp)
{
    private const string DefaultLauncherPath = "";
    private const string DefaultLauncherArguments = "-win -xres 512 -yres 384";
    private const string DefaultTargetProcess = "ra3_1.12.game";
    private const string DefaultAnalysisDirectory = "analysis";
    private const string DefaultAgentDllPath = "artifacts/native/Release/Win32/RayaTrainer.Agent.dll";
    private const int DefaultTimeoutSeconds = 30;
    private const int DefaultCompatibilitySampleBefore = 16;
    private const int DefaultCompatibilitySampleAfter = 96;

    public static SmokeOptions Parse(IReadOnlyList<string> args)
    {
        var launcherPath = DefaultLauncherPath;
        var launcherArguments = DefaultLauncherArguments;
        var targetProcess = DefaultTargetProcess;
        var analysisDirectory = DefaultAnalysisDirectory;
        var timeoutSeconds = DefaultTimeoutSeconds;
        var startLauncher = false;
        var dumpHooks = false;
        var verifyImageHooks = false;
        var imageOnly = false;
        var agentProbe = false;
        var agentFeatureScan = false;
        var agentGameApiSmoke = false;
        var agentRuntimeFeatureSmoke = false;
        var agentDllPath = DefaultAgentDllPath;
        var compatibilitySample = false;
        string? compatibilitySampleOutput = null;
        var compatibilitySampleBefore = DefaultCompatibilitySampleBefore;
        var compatibilitySampleAfter = DefaultCompatibilitySampleAfter;
        var monitorSeconds = 0;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--launcher":
                    launcherPath = ReadValue(args, ref index, arg);
                    startLauncher = true;
                    break;
                case "--launcher-args":
                    launcherArguments = ReadValue(args, ref index, arg);
                    break;
                case "--target":
                    targetProcess = ReadValue(args, ref index, arg);
                    break;
                case "--analysis":
                    analysisDirectory = ReadValue(args, ref index, arg);
                    break;
                case "--timeout":
                    timeoutSeconds = int.Parse(ReadValue(args, ref index, arg));
                    break;
                case "--no-launch":
                    startLauncher = false;
                    break;
                case "--dump-hooks":
                    dumpHooks = true;
                    break;
                case "--verify-image-hooks":
                    verifyImageHooks = true;
                    break;
                case "--image-only":
                    imageOnly = true;
                    verifyImageHooks = true;
                    startLauncher = false;
                    break;
                case "--agent-probe":
                    agentProbe = true;
                    break;
                case "--agent-feature-scan":
                    agentFeatureScan = true;
                    monitorSeconds = monitorSeconds <= 0 ? 1 : monitorSeconds;
                    break;
                case "--agent-game-api-smoke":
                    agentGameApiSmoke = true;
                    break;
                case "--agent-runtime-feature-smoke":
                    agentRuntimeFeatureSmoke = true;
                    break;
                case "--agent-dll":
                    agentDllPath = ReadValue(args, ref index, arg);
                    break;
                case "--compat-sample":
                    compatibilitySample = true;
                    break;
                case "--compat-sample-output":
                    compatibilitySampleOutput = ReadValue(args, ref index, arg);
                    compatibilitySample = true;
                    break;
                case "--compat-sample-before":
                    compatibilitySampleBefore = int.Parse(ReadValue(args, ref index, arg));
                    compatibilitySample = true;
                    break;
                case "--compat-sample-after":
                    compatibilitySampleAfter = int.Parse(ReadValue(args, ref index, arg));
                    compatibilitySample = true;
                    break;
                case "--monitor-seconds":
                    monitorSeconds = int.Parse(ReadValue(args, ref index, arg));
                    break;
                default:
                    if (index == 0 && !arg.StartsWith('-'))
                    {
                        launcherPath = arg;
                        startLauncher = true;
                        break;
                    }

                    throw new ArgumentException($"未知参数: {arg}");
            }
        }

        return new SmokeOptions(
            launcherPath,
            launcherArguments,
            targetProcess,
            analysisDirectory,
            timeoutSeconds,
            startLauncher,
            dumpHooks,
            verifyImageHooks,
            imageOnly,
            agentProbe,
            agentFeatureScan,
            agentGameApiSmoke,
            agentRuntimeFeatureSmoke,
            agentDllPath,
            compatibilitySample,
            compatibilitySampleOutput,
            compatibilitySampleBefore,
            compatibilitySampleAfter,
            monitorSeconds,
            showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("RA3 Trainer smoke test");
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project tools/RayaTrainer.Smoke -- --target ra3_1.12.game --timeout 30");
        Console.WriteLine("  dotnet run --project tools/RayaTrainer.Smoke -- --launcher \"D:/Games/RA3/RA3.exe\" --launcher-args \"-win -xres 512 -yres 384\" --target ra3_1.12.game --timeout 30");
        Console.WriteLine("参数:");
        Console.WriteLine("  --launcher <path>   RA3 启动器路径；未提供时不启动游戏，只等待已有目标进程");
        Console.WriteLine("  --launcher-args <args> 启动器参数，默认 -win -xres 512 -yres 384");
        Console.WriteLine("  --target <name|path> 目标游戏进程名或完整模块路径，默认 ra3_1.12.game");
        Console.WriteLine("  --analysis <path>    analysis 目录，默认 analysis");
        Console.WriteLine("  --timeout <seconds> 等待目标进程出现的秒数，默认 30");
        Console.WriteLine("  --no-launch         不启动启动器，只等待已有目标进程");
        Console.WriteLine("  --dump-hooks        捕获目标后读取 22 个 hook 当前字节并与 manifest 期望字节对比");
        Console.WriteLine("  --verify-image-hooks 从目标 PE 文件读取 hook 字节并与 manifest 期望字节对比");
        Console.WriteLine("  --image-only        只做 PE 文件 hook 字节校验，不启动/等待游戏");
        Console.WriteLine("  --agent-probe       捕获目标后注入 DLL Agent，执行 Ping/GetStatus/InstallPatches/RestorePatches");
        Console.WriteLine("  --agent-feature-scan 捕获目标后通过 DLL Agent 逐个触发可扫描功能并监控稳定性");
        Console.WriteLine("  --agent-game-api-smoke 捕获目标后通过 Native 游戏线程 dispatcher 单项 smoke GetThingClass");
        Console.WriteLine("  --agent-runtime-feature-smoke 定向验证秘密协议与后台运行，不扫描其他功能");
        Console.WriteLine("  --agent-dll <path>  DLL Agent 路径，默认 artifacts/native/Release/Win32/RayaTrainer.Agent.dll");
        Console.WriteLine("  --compat-sample     只读采样所有 hook 与 bootstrap 原生依赖内存点，不安装 patch");
        Console.WriteLine("  --compat-sample-output <path> 兼容性采样 JSON 输出路径");
        Console.WriteLine("  --compat-sample-before <n> 每个采样点前置字节数，默认 16");
        Console.WriteLine("  --compat-sample-after <n> 每个采样点起始后的字节数，默认 96");
        Console.WriteLine("  --monitor-seconds <n> Agent 注入后监控目标进程 n 秒再恢复");
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"{optionName} 缺少参数值。");
        }

        index++;
        return args[index];
    }
}

internal static class SmokeDiagnostics
{
    public static void PrintTarget(TrainerTarget target)
    {
        Console.WriteLine("已检测到目标程序:");
        Console.WriteLine($"  PID: {target.ProcessId?.ToString() ?? "未知"}");
        Console.WriteLine($"  进程/模块名: {target.ProcessName}");
        Console.WriteLine($"  模块路径: {Blank(target.ModulePath)}");
        Console.WriteLine($"  模块基址: 0x{target.ModuleBase:X}");
        Console.WriteLine($"  文件版本: {Blank(target.FileVersion)}");
        Console.WriteLine($"  32位: {target.Is32Bit}");
        Console.WriteLine($"  版本支持: {target.VersionSupported}");
        Console.WriteLine($"  版本 Profile: {Blank(target.VersionProfileId ?? string.Empty)}");
    }

    public static void PrintSnapshot(IReadOnlyList<TrainerProcessCandidate> candidates, string target)
    {
        var relevant = candidates
            .Where(candidate => IsRelevant(candidate, target))
            .OrderBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();

        Console.WriteLine("RA3 相关进程快照:");
        if (relevant.Length == 0)
        {
            Console.WriteLine("  未发现进程名或模块路径包含 RA3 / ra3_1.12 的候选进程。");
            return;
        }

        foreach (var candidate in relevant)
        {
            Console.WriteLine($"  PID={candidate.ProcessId}, Process={candidate.ProcessName}, Module={candidate.ModuleName}");
            Console.WriteLine($"    Path={candidate.ModulePath}");
            Console.WriteLine($"    Base=0x{candidate.ModuleBase:X}, Is32Bit={candidate.Is32Bit}, Version={Blank(candidate.FileVersion)}");
        }
    }

    public static bool PrintImageHookVerification(TrainerManifest manifest, string imagePath)
    {
        return PrintImageHookVerification(manifest, imagePath, target: null);
    }

    public static bool PrintImageHookVerification(TrainerManifest manifest, string imagePath, TrainerTarget? target)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            Console.Error.WriteLine($"PE hook 校验失败：目标文件不存在: {Blank(imagePath)}");
            return false;
        }

        Console.WriteLine($"PE hook 字节校验: {imagePath}");
        var image = PeImage.Load(imagePath);
        PrintPeCalibration(imagePath, image);
        var profile = target is null
            ? ResolveImageProfile(imagePath)
            : ResolveTargetProfile(target);
        if (profile is not null)
        {
            Console.WriteLine($"  VersionProfile={profile.Id} ({profile.DisplayName})");
        }
        var results = profile is null
            ? PatchHookImageVerifier.Verify(manifest.PatchManifest, image)
            : PatchHookImageVerifier.Verify(manifest.PatchManifest, image, profile);
        PrintHookComparisonResults(results.Select(result => new HookComparisonLine(
            result.Address,
            result.SectionTitle,
            $"rva=0x{result.Rva:X}, raw=0x{result.RawOffset:X}, section={result.SectionName}",
            result.ExpectedBytes,
            result.ImageBytes,
            result.Matches)));
        return results.All(result => result.Matches);
    }

    private static Ra3VersionProfile? ResolveImageProfile(string imagePath)
    {
        var fileVersion = FileVersionInfo.GetVersionInfo(imagePath).FileVersion;
        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            return null;
        }

        var profile = Ra3VersionProfileRegistry.FindByFileVersion(fileVersion);
        return profile?.MatchesProcessName(Path.GetFileName(imagePath)) == true
            ? profile
            : null;
    }

    public static void PrintHookBytes(TrainerTarget target, TrainerManifest manifest)
    {
        if (target.ProcessId is null)
        {
            Console.WriteLine("Hook 字节快照: 目标 PID 未知，跳过。");
            return;
        }

        Console.WriteLine("Hook 字节快照:");
        using var memory = new Win32ProcessMemory(target.ProcessId.Value);
        var profile = ResolveTargetProfile(target);
        var resolver = new AddressResolver(target.ModuleBase, new Dictionary<string, nint>(), profile);
        var snapshots = PatchHookInspector.Capture(manifest.PatchManifest, memory, resolver, profile);
        PrintHookComparisonResults(snapshots.Select(snapshot => new HookComparisonLine(
            snapshot.Address,
            snapshot.SectionTitle,
            $"absolute=0x{snapshot.AbsoluteAddress:X}",
            snapshot.ExpectedBytes,
            snapshot.ActualBytes,
                snapshot.Matches)));
    }

    public static bool PrintCompatibilitySample(
        TrainerTarget target,
        TrainerManifest manifest,
        string analysisDirectory,
        CompatibilitySampleOptions options,
        string? outputPath)
    {
        if (target.ProcessId is null)
        {
            Console.Error.WriteLine("兼容性采样失败：目标 PID 未知。");
            return false;
        }

        Console.WriteLine("兼容性采样: 只读读取 Native Hook 内存点。");
        using var memory = new Win32ProcessMemory(target.ProcessId.Value);
        var profile = ResolveTargetProfile(target);
        var resolver = new AddressResolver(target.ModuleBase, new Dictionary<string, nint>(), profile);
        var points = CompatibilitySamplePlanner.Create(
            manifest,
            resolver,
            profile);
        var results = new CompatibilitySampler(memory).Capture(points, options);
        var mismatchResults = results
            .Where(result => result.MatchesExpected == false)
            .ToArray();
        var readFailures = results
            .Where(result => result.Error is not null)
            .ToArray();
        var hookCount = results.Count(result => result.Point.Category == CompatibilitySamplePointCategory.Hook);
        var dependencyCount = results.Count - hookCount;

        Console.WriteLine(
            $"兼容性采样摘要: total={results.Count}, hooks={hookCount}, dependencies={dependencyCount}, " +
            $"mismatches={mismatchResults.Length}, readFailures={readFailures.Length}");

        if (mismatchResults.Length > 0)
        {
            Console.WriteLine("Hook mismatch:");
            foreach (var result in mismatchResults)
            {
                Console.WriteLine($"  [MISMATCH] {result.Point.AddressExpression} {result.Point.Title} absolute=0x{result.Point.AbsoluteAddress:X}");
                Console.WriteLine($"    expected={FormatBytes(result.Point.ExpectedBytes ?? [])}");
                Console.WriteLine($"    actual  ={FormatBytes(result.ActualBytes ?? [])}");
            }
        }

        if (readFailures.Length > 0)
        {
            Console.Error.WriteLine("读取失败:");
            foreach (var result in readFailures)
            {
                Console.Error.WriteLine($"  [READ-FAIL] {result.Point.AddressExpression} {result.Point.Title}: {result.Error}");
            }
        }

        var reportPath = string.IsNullOrWhiteSpace(outputPath)
            ? DefaultCompatibilitySampleOutputPath()
            : outputPath;
        WriteCompatibilitySampleReport(reportPath, target, options, results);
        Console.WriteLine($"兼容性采样报告: {Path.GetFullPath(reportPath)}");
        return readFailures.Length == 0;
    }

    public static async Task<bool> ProbeAgentAsync(
        TrainerTarget target,
        TrainerManifest manifest,
        string analysisDirectory,
        string agentDllPath,
        bool smokeGetThingClass,
        bool smokeRuntimeFeatures,
        TimeSpan monitorDuration,
        CancellationToken cancellationToken = default)
    {
        if (target.ProcessId is null)
        {
            Console.Error.WriteLine("DLL Agent 探针失败：目标 PID 未知。");
            return false;
        }

        var resolvedAgentDllPath = Path.GetFullPath(agentDllPath);
        if (!File.Exists(resolvedAgentDllPath))
        {
            Console.Error.WriteLine($"DLL Agent 探针失败：找不到 Agent DLL: {resolvedAgentDllPath}");
            return false;
        }

        var gameApiStep = smokeGetThingClass ? " -> SmokeGetThingClass" : string.Empty;
        var runtimeFeatureStep = smokeRuntimeFeatures ? " -> SecretProtocol/BackgroundRun" : string.Empty;
        Console.WriteLine(monitorDuration > TimeSpan.Zero
            ? $"DLL Agent 探针: 注入 -> Ping/GetStatus -> InstallPatches{gameApiStep}{runtimeFeatureStep} -> 监控 {monitorDuration.TotalSeconds:0}s -> RestorePatches -> 校验 hook 字节。"
            : $"DLL Agent 探针: 注入 -> Ping/GetStatus -> InstallPatches{gameApiStep}{runtimeFeatureStep} -> RestorePatches -> 校验 hook 字节。");
        Console.WriteLine($"  Agent DLL: {resolvedAgentDllPath}");

        var injector = new AgentInjector();
        var client = new AgentNamedPipeClient();
        var timeout = TimeSpan.FromSeconds(5);
        var injected = false;
        Exception? restoreError = null;
        ProcessStabilityResult? stability = null;
        IReadOnlyDictionary<string, uint>? scannedAddresses = null;
        IReadOnlyDictionary<string, byte[]>? hookByteBaseline = null;

        try
        {
            var injection = injector.Inject(target.ProcessId.Value, resolvedAgentDllPath, timeout);
            if (!injection.Success)
            {
                Console.Error.WriteLine($"DLL Agent 探针失败：{injection.Message}");
                return false;
            }

            injected = true;
            Console.WriteLine($"  Inject: {injection.Message} module=0x{injection.RemoteModuleHandle:X}");

            var ping = await client.PingAsync(target.ProcessId.Value, timeout, cancellationToken);
            Console.WriteLine($"  Ping: status={ping.StatusCode}, version={ping.AgentVersion}, pid={ping.ProcessId}, moduleBase=0x{ping.ModuleBase:X}");
            if (ping.StatusCode != AgentStatusCode.Ok)
            {
                return false;
            }

            var status = await client.GetStatusAsync(target.ProcessId.Value, timeout, cancellationToken);
            PrintAgentStatus("  Status", status);
            if (status.StatusCode != AgentStatusCode.Ok)
            {
                return false;
            }

            scannedAddresses = await ScanAgentAddressesAsync(
                client,
                target,
                timeout,
                cancellationToken);
            hookByteBaseline = CaptureAgentHookBaseline(target, manifest, scannedAddresses);

            var request = AgentPatchPayloadBuilder.Build(
                manifest,
                target,
                status,
                scannedAddresses: scannedAddresses);
            Console.WriteLine($"  Install request: writes={request.Writes.Count}, hooks={request.Hooks.Count}");

            var install = await client.InstallPatchesAsync(target.ProcessId.Value, request, timeout, cancellationToken);
            Console.WriteLine($"  InstallPatches: status={install.StatusCode}, installedHookCount={install.InstalledHookCount}");
            if (install.StatusCode != AgentStatusCode.Ok)
            {
                if (install.StatusCode == AgentStatusCode.PatchMismatch)
                {
                    await PrintAgentMismatchAsync(client, target.ProcessId.Value, timeout, cancellationToken);
                }

                return false;
            }

            if (!await DeliverAgentNativeCatalogAsync(
                    client,
                    target,
                    status.ModuleBase,
                    scannedAddresses,
                    timeout,
                    cancellationToken))
            {
                return false;
            }

            var gameMode = await client.GetGameModeAsync(target.ProcessId.Value, timeout, cancellationToken);
            Console.WriteLine($"  GetGameMode: status={gameMode.StatusCode}, value={gameMode.GameMode}");
            if (gameMode.StatusCode != AgentStatusCode.Ok)
            {
                return false;
            }

            if (smokeGetThingClass)
            {
                var gameApiCommandTimeout = TimeSpan.FromSeconds(8);
                var gameApi = await client.SmokeGetThingClassAsync(
                    target.ProcessId.Value,
                    new AgentGameApiGetThingClassRequest(
                        UnitTypeId: 0x6586A5A0,
                        TimeoutMilliseconds: 5000,
                        EnableDirectGameApi: true),
                    gameApiCommandTimeout,
                    cancellationToken);
                Console.WriteLine(
                    $"  SmokeGetThingClass: status={gameApi.StatusCode}, dispatch={gameApi.DispatchStatus}, " +
                    $"request={gameApi.RequestId}, tick={gameApi.GameThreadTickBefore}->{gameApi.GameThreadTickAfter}, " +
                    $"unit=0x{gameApi.UnitTypeId:X8}, thingClass=0x{gameApi.ThingClassAddress:X8}");
                if (gameApi.DispatchStatus == GameApiDispatchStatus.NoGameTick)
                {
                    Console.Error.WriteLine("  SmokeGetThingClass: NoGameTick，当前场景没有进入 Native dispatcher 的游戏线程 tick。");
                }
                if (gameApi.StatusCode != AgentStatusCode.Ok ||
                    gameApi.DispatchStatus != GameApiDispatchStatus.Completed ||
                    gameApi.ThingClassAddress == 0)
                {
                    return false;
                }

                var selectedUnit = await client.ReadSelectedUnitSnapshotViaGameApiAsync(
                    target.ProcessId.Value,
                    new AgentGameApiReadSelectedUnitCodeRequest(
                        TimeoutMilliseconds: 5000,
                        EnableDirectGameApi: true),
                    gameApiCommandTimeout,
                    cancellationToken);
                Console.WriteLine(
                    $"  ReadSelectedUnitCode(API): status={selectedUnit.StatusCode}, dispatch={selectedUnit.DispatchStatus}, " +
                    $"request={selectedUnit.RequestId}, tick={selectedUnit.GameThreadTickBefore}->{selectedUnit.GameThreadTickAfter}, " +
                    $"unit=0x{selectedUnit.UnitTypeId:X8}, thingClass=0x{selectedUnit.ThingClassAddress:X8}");
                if (selectedUnit.DispatchStatus == GameApiDispatchStatus.NoGameTick)
                {
                    Console.Error.WriteLine("  ReadSelectedUnitCode(API): NoGameTick，当前场景没有进入 Native dispatcher 的游戏线程 tick。");
                }
                if (selectedUnit.DispatchStatus == GameApiDispatchStatus.NoSelectedUnit)
                {
                    Console.Error.WriteLine("  ReadSelectedUnitCode(API): NoSelectedUnit，当前场景没有可读取的选中单位。");
                }
                if (selectedUnit.StatusCode != AgentStatusCode.Ok ||
                    selectedUnit.DispatchStatus != GameApiDispatchStatus.Completed ||
                    selectedUnit.UnitTypeId == 0 ||
                    selectedUnit.ThingClassAddress == 0)
                {
                    return false;
                }
            }

            if (smokeRuntimeFeatures && !await SmokeRuntimeFeaturesAsync(
                    client,
                    target,
                    manifest,
                    status with { InstalledHookCount = install.InstalledHookCount },
                    cancellationToken))
            {
                return false;
            }

            if (monitorDuration > TimeSpan.Zero)
            {
                stability = await new ProcessStabilityMonitor()
                    .MonitorAsync(target.ProcessId.Value, monitorDuration, cancellationToken);
                PrintStabilityResult(stability);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("DLL Agent 探针失败：诊断总超时预算已耗尽。");
            PrintTargetProcessState(target.ProcessId.Value);
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"DLL Agent 探针失败：{ex.Message}");
            PrintTargetProcessState(target.ProcessId.Value);
            return false;
        }
        finally
        {
            if (injected)
            {
                try
                {
                    var restored = await client.RestorePatchesAsync(target.ProcessId.Value, timeout, CancellationToken.None);
                    Console.WriteLine($"  RestorePatches: status={restored.StatusCode}, installedHookCount={restored.InstalledHookCount}");
                    if (restored.StatusCode != AgentStatusCode.Ok)
                    {
                        restoreError = new InvalidOperationException($"Agent RestorePatches returned {restored.StatusCode}.");
                    }
                }
                catch (Exception ex)
                {
                    restoreError = ex;
                }
            }
        }

        if (stability?.StayedAlive == false)
        {
            if (restoreError is not null)
            {
                Console.Error.WriteLine($"目标进程已退出，DLL Agent 恢复 patch 时也失败：{restoreError.Message}");
            }

            return false;
        }

        if (restoreError is not null)
        {
            Console.Error.WriteLine($"DLL Agent 探针失败：恢复 patch 时失败：{restoreError.Message}");
            return false;
        }

        using var memory = new Win32ProcessMemory(target.ProcessId.Value);
        var restoredHooks = PatchHookInspector.CaptureResolved(
            manifest.PatchManifest,
            memory,
            scannedAddresses ?? throw new InvalidOperationException("Agent signature scan did not complete."),
            hookByteBaseline ?? throw new InvalidOperationException("Agent hook byte baseline was not captured."),
            skipUnresolved: true);
        var allRestored = restoredHooks.All(snapshot => snapshot.Matches);
        Console.WriteLine(allRestored
            ? "DLL Agent 探针通过：注入、Ping/GetStatus、安装、读取、恢复均完成，恢复后所有 hook 字节匹配。"
            : "DLL Agent 探针失败：恢复后仍有 hook 字节不匹配。");
        if (!allRestored)
        {
            PrintHookComparisonResults(restoredHooks.Select(snapshot => new HookComparisonLine(
                snapshot.Address,
                snapshot.SectionTitle,
                $"absolute=0x{snapshot.AbsoluteAddress:X}",
                snapshot.ExpectedBytes,
                snapshot.ActualBytes,
                snapshot.Matches)));
        }

        return allRestored;
    }

    public static async Task<bool> ProbeAgentFeaturesAsync(
        TrainerTarget target,
        TrainerManifest manifest,
        string analysisDirectory,
        string agentDllPath,
        TimeSpan monitorDuration,
        CancellationToken cancellationToken = default)
    {
        if (target.ProcessId is null)
        {
            Console.Error.WriteLine("DLL Agent 功能扫描失败：目标 PID 未知。");
            return false;
        }

        var resolvedAgentDllPath = Path.GetFullPath(agentDllPath);
        if (!File.Exists(resolvedAgentDllPath))
        {
            Console.Error.WriteLine($"DLL Agent 功能扫描失败：找不到 Agent DLL: {resolvedAgentDllPath}");
            return false;
        }

        var items = FeatureScanPlanner.Create(manifest.Features);
        var scannableCount = items.Count(item => item.CanScan);
        var skippedCount = items.Count - scannableCount;
        Console.WriteLine(
            $"DLL Agent 功能扫描: 注入 -> Ping/GetStatus -> InstallPatches -> 基线监控 {monitorDuration.TotalSeconds:0}s -> " +
            $"通过 Agent 逐个触发 {scannableCount} 个可扫描功能 -> RestorePatches -> 校验 hook 字节。");
        Console.WriteLine($"  Agent DLL: {resolvedAgentDllPath}");
        if (skippedCount > 0)
        {
            Console.WriteLine($"DLL Agent 功能扫描: {skippedCount} 个功能将跳过。");
        }

        var injector = new AgentInjector();
        var client = new AgentNamedPipeClient();
        var timeout = TimeSpan.FromSeconds(5);
        var injected = false;
        Exception? scanError = null;
        Exception? resetError = null;
        Exception? restoreError = null;
        ProcessStabilityResult? failedStability = null;
        string? failurePoint = null;
        IReadOnlyDictionary<string, uint>? scannedAddresses = null;
        IReadOnlyDictionary<string, byte[]>? hookByteBaseline = null;

        try
        {
            var injection = injector.Inject(target.ProcessId.Value, resolvedAgentDllPath, timeout);
            if (!injection.Success)
            {
                Console.Error.WriteLine($"DLL Agent 功能扫描失败：{injection.Message}");
                return false;
            }

            injected = true;
            Console.WriteLine($"  Inject: {injection.Message} module=0x{injection.RemoteModuleHandle:X}");

            var ping = await client.PingAsync(target.ProcessId.Value, timeout, cancellationToken);
            Console.WriteLine($"  Ping: status={ping.StatusCode}, version={ping.AgentVersion}, pid={ping.ProcessId}, moduleBase=0x{ping.ModuleBase:X}");
            if (ping.StatusCode != AgentStatusCode.Ok)
            {
                return false;
            }

            var status = await client.GetStatusAsync(target.ProcessId.Value, timeout, cancellationToken);
            PrintAgentStatus("  Status", status);
            if (status.StatusCode != AgentStatusCode.Ok)
            {
                return false;
            }

            scannedAddresses = await ScanAgentAddressesAsync(
                client,
                target,
                timeout,
                cancellationToken);
            hookByteBaseline = CaptureAgentHookBaseline(target, manifest, scannedAddresses);

            var request = AgentPatchPayloadBuilder.Build(
                manifest,
                target,
                status,
                scannedAddresses: scannedAddresses);
            Console.WriteLine($"  Install request: writes={request.Writes.Count}, hooks={request.Hooks.Count}");

            var install = await client.InstallPatchesAsync(target.ProcessId.Value, request, timeout, cancellationToken);
            Console.WriteLine($"  InstallPatches: status={install.StatusCode}, installedHookCount={install.InstalledHookCount}");
            if (install.StatusCode != AgentStatusCode.Ok)
            {
                if (install.StatusCode == AgentStatusCode.PatchMismatch)
                {
                    await PrintAgentMismatchAsync(client, target.ProcessId.Value, timeout, cancellationToken);
                }

                return false;
            }

            if (!await DeliverAgentNativeCatalogAsync(
                    client,
                    target,
                    status.ModuleBase,
                    scannedAddresses,
                    timeout,
                    cancellationToken))
            {
                return false;
            }

            var controller = new AgentFeatureController(
                client,
                target.ProcessId.Value,
                status with { InstalledHookCount = install.InstalledHookCount });

            if (monitorDuration > TimeSpan.Zero)
            {
                Console.WriteLine("--- DLL Agent 功能扫描基线: 完整 hook 安装后 ---");
                var stability = await new ProcessStabilityMonitor()
                    .MonitorAsync(target.ProcessId.Value, monitorDuration, cancellationToken);
                PrintStabilityResult(stability);
                if (!stability.StayedAlive)
                {
                    failedStability = stability;
                    failurePoint = "完整 hook 基线";
                }
            }

            var index = 0;
            foreach (var item in items)
            {
                if (!item.CanScan)
                {
                    Console.WriteLine($"--- DLL Agent 功能扫描 SKIP: {item.Feature.DisplayName} ---");
                    Console.WriteLine($"跳过原因: {item.SkipReason}");
                    continue;
                }

                if (failurePoint is not null)
                {
                    break;
                }

                index++;
                Console.WriteLine(
                    $"--- DLL Agent 功能扫描 {index}/{scannableCount}: " +
                    $"{item.Feature.DisplayName} ({DescribeFeatureScanKind(item.Kind)}) ---");
                try
                {
                    if (item.Kind == FeatureScanKind.Toggle)
                    {
                        controller.SetToggle(item.Feature, true);
                    }
                    else
                    {
                        var dispatch = await controller.TriggerActionAndWaitForConsumptionAsync(
                            item.Feature,
                            timeout: TimeSpan.FromMilliseconds(750),
                            pollInterval: TimeSpan.FromMilliseconds(50),
                            cancellationToken: cancellationToken);
                        Console.WriteLine($"  ActionDispatch: {dispatch}");
                    }

                    if (monitorDuration > TimeSpan.Zero)
                    {
                        var stability = await new ProcessStabilityMonitor()
                            .MonitorAsync(target.ProcessId.Value, monitorDuration, cancellationToken);
                        PrintStabilityResult(stability);
                        if (!stability.StayedAlive)
                        {
                            failedStability = stability;
                            failurePoint = item.Feature.DisplayName;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scanError = ex;
                    failurePoint = item.Feature.DisplayName;
                }
                finally
                {
                    try
                    {
                        controller.Reset(item.Feature);
                    }
                    catch (Exception ex)
                    {
                        resetError = ex;
                        failurePoint ??= item.Feature.DisplayName;
                    }
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            scanError = ex;
            failurePoint ??= "DLL Agent 功能扫描";
        }
        catch (Exception ex)
        {
            scanError = ex;
            failurePoint ??= "DLL Agent 功能扫描";
        }
        finally
        {
            if (injected)
            {
                try
                {
                    var restored = await client.RestorePatchesAsync(target.ProcessId.Value, timeout, CancellationToken.None);
                    Console.WriteLine($"  RestorePatches: status={restored.StatusCode}, installedHookCount={restored.InstalledHookCount}");
                    if (restored.StatusCode != AgentStatusCode.Ok)
                    {
                        restoreError = new InvalidOperationException($"Agent RestorePatches returned {restored.StatusCode}.");
                    }
                }
                catch (Exception ex)
                {
                    restoreError = ex;
                }
            }
        }

        if (failedStability?.StayedAlive == false)
        {
            Console.Error.WriteLine($"DLL Agent 功能扫描失败点：{failurePoint}。");
            if (restoreError is not null)
            {
                Console.Error.WriteLine($"目标进程已退出，DLL Agent 恢复 patch 时也失败：{restoreError.Message}");
            }

            return false;
        }

        if (scanError is OperationCanceledException)
        {
            Console.Error.WriteLine("DLL Agent 功能扫描失败：诊断总超时预算已耗尽。");
            PrintTargetProcessState(target.ProcessId.Value);
            return false;
        }

        if (scanError is not null)
        {
            Console.Error.WriteLine($"DLL Agent 功能扫描失败点：{failurePoint}。");
            Console.Error.WriteLine($"DLL Agent 功能扫描失败：{scanError.Message}");
            PrintTargetProcessState(target.ProcessId.Value);
            return false;
        }

        if (resetError is not null)
        {
            Console.Error.WriteLine($"DLL Agent 功能扫描失败点：{failurePoint}。");
            Console.Error.WriteLine($"DLL Agent 功能复位失败：{resetError.Message}");
            PrintTargetProcessState(target.ProcessId.Value);
            return false;
        }

        if (restoreError is not null)
        {
            Console.Error.WriteLine($"DLL Agent 功能扫描失败：恢复 patch 时失败：{restoreError.Message}");
            return false;
        }

        using var memory = new Win32ProcessMemory(target.ProcessId.Value);
        var restoredHooks = PatchHookInspector.CaptureResolved(
            manifest.PatchManifest,
            memory,
            scannedAddresses ?? throw new InvalidOperationException("Agent signature scan did not complete."),
            hookByteBaseline ?? throw new InvalidOperationException("Agent hook byte baseline was not captured."),
            skipUnresolved: true);
        var allRestored = restoredHooks.All(snapshot => snapshot.Matches);
        Console.WriteLine(allRestored
            ? "DLL Agent 功能扫描通过：所有可扫描功能已经通过 Agent 触发/复位，恢复后所有 hook 字节匹配。"
            : "DLL Agent 功能扫描失败：恢复后仍有 hook 字节不匹配。");
        if (!allRestored)
        {
            PrintHookComparisonResults(restoredHooks.Select(snapshot => new HookComparisonLine(
                snapshot.Address,
                snapshot.SectionTitle,
                $"absolute=0x{snapshot.AbsoluteAddress:X}",
                snapshot.ExpectedBytes,
                snapshot.ActualBytes,
                snapshot.Matches)));
        }

        return allRestored;
    }

    private static void PrintStabilityResult(ProcessStabilityResult result)
    {
        if (result.StayedAlive)
        {
            Console.WriteLine($"稳定性监控通过：PID={result.ProcessId} 在 {result.ObservedFor.TotalSeconds:0.0}s 内保持运行。");
            return;
        }

        var exitCode = result.ExitCode is null
            ? "未知"
            : $"{result.ExitCode} (0x{result.ExitCode.Value & 0xFFFFFFFF:X8})";
        var exitTime = result.ExitedAt is null
            ? "未知"
            : result.ExitedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        Console.Error.WriteLine($"稳定性监控失败：PID={result.ProcessId} 在 {result.ObservedFor.TotalSeconds:0.0}s 后退出。");
        Console.Error.WriteLine($"  ExitCode={exitCode}");
        Console.Error.WriteLine($"  ExitTime={exitTime}");
    }

    private static void PrintAgentStatus(string label, AgentStatusPayload status)
    {
        Console.WriteLine(
            $"{label}: status={status.StatusCode}, version={status.AgentVersion}, pid={status.ProcessId}, " +
            $"moduleBase=0x{status.ModuleBase:X}, nativeCapabilities=0x{status.NativeRuntimeCapabilities:X8}, " +
            $"hooks={status.InstalledHookCount}");
    }

    private static async Task<IReadOnlyDictionary<string, uint>?> ScanAgentAddressesAsync(
        AgentNamedPipeClient client,
        TrainerTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var profile = ResolveTargetProfile(target);
        if (profile?.SupportsSignatureScanning != true)
        {
            Console.WriteLine("  ScanSignatures: skip (当前 profile 未启用内置签名目录)");
            return null;
        }

        var scan = await client.ScanSignaturesAsync(
            target.ProcessId!.Value,
            timeout,
            cancellationToken);
        Console.WriteLine(
            $"  ScanSignatures: status={scan.StatusCode}, matched={scan.MatchedCount}/{scan.EntryCount}");
        if (scan.StatusCode != AgentStatusCode.Ok || scan.EntryCount == 0)
        {
            throw new InvalidOperationException(
                $"Agent signature scan failed: status={scan.StatusCode}, entries={scan.EntryCount}.");
        }

        // Tolerate unverified and explicitly unused profile symbols. Active entries remain
        // fail-closed exactly like InjectedAgentBackend validation.
        var optionalSymbols = profile.Hooks
            .Where(kv => kv.Value.Status != AddressSupportStatus.Verified)
            .Select(kv => kv.Key)
            .Concat(profile.OptionalSignatureSymbols)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolved = scan.Addresses
            .Where(entry => entry.Value == 0 && !optionalSymbols.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToArray();
        if (unresolved.Length > 0)
        {
            throw new InvalidOperationException(
                $"Agent signature scan incomplete: {string.Join(", ", unresolved.Take(10))}");
        }

        var tolerated = scan.Addresses
            .Where(entry => entry.Value == 0 && optionalSymbols.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToArray();
        if (tolerated.Length > 0)
        {
            Console.WriteLine(
                $"  ScanSignatures: tolerated {tolerated.Length} optional symbols: {string.Join(", ", tolerated.Take(5))}...");
        }

        return scan.Addresses;
    }

    private static async Task PrintAgentMismatchAsync(
        AgentNamedPipeClient client,
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var mismatch = await client.GetMismatchDiagnosticsAsync(
            processId,
            timeout,
            cancellationToken);
        if (!mismatch.HasMismatch)
        {
            Console.Error.WriteLine($"  Mismatch diagnostics: status={mismatch.StatusCode}");
            return;
        }

        Console.Error.WriteLine($"  Mismatch address: 0x{mismatch.HookAddress:X8}");
        Console.Error.WriteLine($"    expected={FormatBytes(mismatch.ExpectedBytes)}");
        Console.Error.WriteLine($"    actual  ={FormatBytes(mismatch.ActualBytes)}");
    }

    private static async Task<bool> SmokeRuntimeFeaturesAsync(
        AgentNamedPipeClient client,
        TrainerTarget target,
        TrainerManifest manifest,
        AgentStatusPayload status,
        CancellationToken cancellationToken)
    {
        var features = TrainerFeatureCatalog.CreateGridFeatures(manifest.Features);
        var secretProtocol = features.Single(feature =>
            feature.RawName.Equals("Secret Protocol Dependency Bypass", StringComparison.Ordinal));
        var backgroundRun = features.Single(feature =>
            feature.RawName.Equals("Run In Background", StringComparison.Ordinal));
        var controller = new AgentFeatureController(client, target.ProcessId!.Value, status);

        try
        {
            controller.SetToggle(secretProtocol, true);
            controller.SetToggle(backgroundRun, true);
            var secretEnabled = controller.ReadToggleState(secretProtocol);
            var backgroundEnabled = controller.ReadToggleState(backgroundRun);
            Console.WriteLine(
                $"  Runtime toggles: secretProtocol={secretEnabled}, backgroundRun={backgroundEnabled}");
            if (!secretEnabled || !backgroundEnabled)
            {
                return false;
            }

            var commandTimeout = TimeSpan.FromSeconds(8);
            var probe = await client.SecretProtocolBindingProbeAsync(
                target.ProcessId.Value,
                new AgentGameApiSecretProtocolBindingProbeRequest(
                    TimeoutMilliseconds: 5000,
                    EnableDirectGameApi: true),
                commandTimeout,
                cancellationToken);
            var probeDetails = controller.ReadSecretProtocolBindingProbeResult();
            Console.WriteLine(
                $"  SecretProtocolBindingProbe: status={probe.StatusCode}, dispatch={probe.DispatchStatus}, " +
                $"result={(SecretProtocolBindingProbeStatus)probe.ProbeResult}, request={probe.RequestId}, " +
                $"tick={probe.GameThreadTickBefore}->{probe.GameThreadTickAfter}");
            Console.WriteLine(
                $"    player=0x{probeDetails.PlayerAddress:X8}, science=0x{probeDetails.ScienceManagerAddress:X8}, " +
                $"AirPower={probeDetails.AirPowerStatus}, EnhancedKamikaze={probeDetails.EnhancedKamikazeStatus}");
            if (probe.StatusCode != AgentStatusCode.Ok ||
                probe.DispatchStatus != GameApiDispatchStatus.Completed ||
                probe.ProbeResult != (uint)SecretProtocolBindingProbeStatus.Completed ||
                probeDetails.Status != SecretProtocolBindingProbeStatus.Completed ||
                probeDetails.AirPowerStatus != SecretProtocolBindingItemStatus.TechAndUpgradeGranted ||
                probeDetails.EnhancedKamikazeStatus is not (
                    SecretProtocolBindingItemStatus.TechAndUpgradeGranted or
                    SecretProtocolBindingItemStatus.TechGrantedUpgradeManuallyGranted))
            {
                return false;
            }

            var focusCycle = await WindowFocusProbe.CycleGameToBackgroundAsync(
                target.ProcessId.Value,
                cancellationToken);
            Console.WriteLine(
                $"  Background focus cycle: gameActivated={focusCycle.GameActivated}, " +
                $"returnedToPrevious={focusCycle.ReturnedToPrevious}");
            if (!focusCycle.GameActivated || !focusCycle.ReturnedToPrevious)
            {
                return false;
            }

            var backgroundTick = await client.SmokeGetThingClassAsync(
                target.ProcessId.Value,
                new AgentGameApiGetThingClassRequest(
                    UnitTypeId: 0x6586A5A0,
                    TimeoutMilliseconds: 5000,
                    EnableDirectGameApi: true),
                commandTimeout,
                cancellationToken);
            Console.WriteLine(
                $"  Background dispatcher: status={backgroundTick.StatusCode}, dispatch={backgroundTick.DispatchStatus}, " +
                $"tick={backgroundTick.GameThreadTickBefore}->{backgroundTick.GameThreadTickAfter}, " +
                $"thingClass=0x{backgroundTick.ThingClassAddress:X8}");
            return backgroundTick.StatusCode == AgentStatusCode.Ok &&
                backgroundTick.DispatchStatus == GameApiDispatchStatus.Completed &&
                backgroundTick.GameThreadTickAfter > backgroundTick.GameThreadTickBefore &&
                backgroundTick.ThingClassAddress != 0;
        }
        finally
        {
            controller.Reset(backgroundRun);
            controller.Reset(secretProtocol);
        }
    }

    private static IReadOnlyDictionary<string, byte[]> CaptureAgentHookBaseline(
        TrainerTarget target,
        TrainerManifest manifest,
        IReadOnlyDictionary<string, uint>? scannedAddresses)
    {
        if (target.ProcessId is null || scannedAddresses is null)
        {
            return new Dictionary<string, byte[]>();
        }

        using var memory = new Win32ProcessMemory(target.ProcessId.Value);
        return PatchHookInspector.CaptureResolved(manifest.PatchManifest, memory, scannedAddresses, skipUnresolved: true)
            .ToDictionary(
                snapshot => snapshot.Address,
                snapshot => snapshot.ActualBytes,
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> DeliverAgentNativeCatalogAsync(
        AgentNamedPipeClient client,
        TrainerTarget target,
        uint moduleBase,
        IReadOnlyDictionary<string, uint>? scannedAddresses,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var profile = ResolveTargetProfile(target);
        if (profile is null)
        {
            Console.WriteLine("  SetNativeCatalog: skip (未识别 profile)");
            return true;
        }

        IReadOnlyList<uint> rvas;
        try
        {
            // NativeAgentRefs contains both module RVAs and structure offsets. A signature
            // scan can refine code locations, but values such as PlayerScienceManagerOffset
            // are deliberately not scanner symbols; use the complete profile contract just
            // like the production backend does.
            rvas = profile.BuildNativeAgentCatalogRvas();
        }
        catch (UnsupportedSymbolException)
        {
            Console.WriteLine($"  SetNativeCatalog: skip (profile {profile.Id} 的 NativeAgentRefs 尚未全部 Verified)");
            return true;
        }

        var result = await client.SetNativeCatalogAsync(target.ProcessId!.Value, rvas, timeout, cancellationToken);
        Console.WriteLine($"  SetNativeCatalog: status={result.StatusCode}, profile={profile.Id}, entries={rvas.Count}");
        return result.StatusCode == AgentStatusCode.Ok;
    }


    private static string DescribeFeatureScanKind(FeatureScanKind kind)
    {
        return kind switch
        {
            FeatureScanKind.Toggle => "toggle",
            FeatureScanKind.Action => "action",
            _ => "unsupported"
        };
    }

    private static void PrintTargetProcessState(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            if (!process.HasExited)
            {
                Console.Error.WriteLine($"目标进程状态：PID={processId} 仍在运行。");
                return;
            }

            var exitTime = process.ExitTime.ToString("yyyy-MM-dd HH:mm:ss zzz");
            Console.Error.WriteLine($"目标进程状态：PID={processId} 已退出，ExitCode={process.ExitCode} (0x{process.ExitCode & 0xFFFFFFFF:X8})，ExitTime={exitTime}。");
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"目标进程状态：PID={processId} 不存在或已退出。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"目标进程状态：读取 PID={processId} 失败：{ex.Message}");
        }
    }

    private static bool IsRelevant(TrainerProcessCandidate candidate, string target)
    {
        var normalizedTarget = TrainerProcessName.ToProcessName(target);
        return Contains(candidate.ProcessName, "ra3")
            || Contains(candidate.ModuleName, "ra3")
            || Contains(candidate.ModulePath, "ra3")
            || Contains(candidate.ProcessName, normalizedTarget)
            || Contains(candidate.ModuleName, normalizedTarget)
            || Contains(candidate.ModulePath, normalizedTarget);
    }

    private static void PrintPeCalibration(string imagePath, PeImage image)
    {
        var fileInfo = FileVersionInfo.GetVersionInfo(imagePath);
        Console.WriteLine("PE 文件校准:");
        Console.WriteLine($"  FileName={Path.GetFileName(imagePath)}");
        Console.WriteLine($"  FileVersion={Blank(fileInfo.FileVersion ?? string.Empty)}");
        Console.WriteLine($"  ProductVersion={Blank(fileInfo.ProductVersion ?? string.Empty)}");
        Console.WriteLine($"  SHA256={ComputeSha256(imagePath)}");
        Console.WriteLine($"  Machine=0x{image.Metadata.Machine:X}");
        Console.WriteLine($"  ImageBase=0x{image.Metadata.ImageBase:X}");
        Console.WriteLine($"  SizeOfImage=0x{image.Metadata.SizeOfImage:X}");
        Console.WriteLine($"  TimeDateStamp=0x{image.Metadata.TimeDateStamp:X8}");
        Console.WriteLine("  Sections:");
        foreach (var section in image.Sections)
        {
            Console.WriteLine(
                $"    {section.Name}: va=0x{section.VirtualAddress:X}, vsize=0x{section.VirtualSize:X}, " +
                $"raw=0x{section.RawPointer:X}, rawSize=0x{section.RawSize:X}");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static bool Contains(string value, string fragment)
    {
        return value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    private static Ra3VersionProfile? ResolveTargetProfile(TrainerTarget target)
    {
        return Ra3VersionProfileRegistry.ResolveTargetProfile(target);
    }

    private static string Blank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(空)" : value;
    }

    private static void PrintHookComparisonResults(IEnumerable<HookComparisonLine> lines)
    {
        foreach (var line in lines)
        {
            var status = line.Matches ? "OK" : "MISMATCH";
            Console.WriteLine($"  [{status}] {line.Address} {line.SectionTitle} {line.Location}");
            Console.WriteLine($"    expected={FormatBytes(line.ExpectedBytes)}");
            Console.WriteLine($"    actual  ={FormatBytes(line.ActualBytes)}");
        }
    }

    private static string FormatBytes(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(value => value.ToString("X2")));
    }

    private static string DefaultCompatibilitySampleOutputPath()
    {
        return Path.Combine(
            "artifacts",
            "diagnostics",
            $"compatibility-sample-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
    }

    private static void WriteCompatibilitySampleReport(
        string outputPath,
        TrainerTarget target,
        CompatibilitySampleOptions options,
        IReadOnlyList<CompatibilitySampleResult> results)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var hookCount = results.Count(result => result.Point.Category == CompatibilitySamplePointCategory.Hook);
        var report = new CompatibilitySampleReportDto(
            new CompatibilitySampleTargetDto(
                target.ProcessId,
                target.ProcessName,
                target.ModulePath,
                $"0x{target.ModuleBase:X}",
                target.FileVersion,
                target.Is32Bit,
                target.VersionSupported),
            new CompatibilitySampleOptionsDto(options.BytesBefore, options.BytesAfter),
            new CompatibilitySampleSummaryDto(
                results.Count,
                hookCount,
                results.Count - hookCount,
                results.Count(result => result.MatchesExpected == false),
                results.Count(result => result.Error is not null)),
            results.Select(ToReportPoint).ToArray());

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, jsonOptions));
    }

    private static CompatibilitySamplePointDto ToReportPoint(CompatibilitySampleResult result)
    {
        return new CompatibilitySamplePointDto(
            result.Point.AddressExpression,
            $"0x{result.Point.AbsoluteAddress:X}",
            result.Point.Category.ToString(),
            result.Point.Title,
            result.Point.EnableFlags.ToArray(),
            result.Point.ExpectedBytes is null ? null : FormatBytes(result.Point.ExpectedBytes),
            result.ActualBytes is null ? null : FormatBytes(result.ActualBytes),
            result.MatchesExpected,
            $"0x{result.RangeStart:X}",
            FormatBytes(result.Bytes),
            result.Instructions.Select(instruction => new CompatibilitySampleInstructionDto(
                $"0x{instruction.Ip:X}",
                instruction.Length,
                instruction.Bytes,
                instruction.Text)).ToArray(),
            result.Error);
    }
}
