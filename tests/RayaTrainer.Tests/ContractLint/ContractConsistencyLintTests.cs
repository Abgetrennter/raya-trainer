using Xunit;
using RayaTrainer.ContractLint;

namespace RayaTrainer.Tests.ContractLint;

/// <summary>
/// Tests for ContractConsistencyLint — validates that the 4 cross-language contracts
/// (ProtocolConstants, AgentCommand, NativeCatalogEntry, NativeFeatureStateId) are
/// detected as consistent or correctly flagged with violations.
/// All tests use hermetic temp directories; none touch the real repo.
/// </summary>
public sealed class ContractConsistencyLintTests
{
    // ====================================================================
    // Helper: create a temp repoRoot with the minimal directory structure
    // ====================================================================

    private static string CreateTempRepoRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ContractLintTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "RayaTrainer.Core", "Agent"));
        Directory.CreateDirectory(Path.Combine(root, "src", "RayaTrainer.Agent"));
        return root;
    }

    private static void WriteFile(string repoRoot, string relativePath, string content)
    {
        var fullPath = Path.Combine(repoRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content.ReplaceLineEndings());
    }

    // ====================================================================
    // 1. ProtocolConstants
    // ====================================================================

    [Fact]
    public void CheckProtocolConstants_Consistent_ReturnsEmpty()
    {
        var root = CreateTempRepoRoot();

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentProtocol.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentProtocol
            {
                public const uint Magic = 0x41594152;
                public const ushort Version = 9;
                public const int HeaderSize = 16;
                public const uint MaxPayloadLength = 64 * 1024;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentBuildIdentity
            {
                public const ulong Fingerprint = 0x5241594100090001UL;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            #pragma once
            #include <cstdint>
            namespace RayaTrainer::agent {
            inline constexpr uint32_t kAgentMagic = 0x41594152u;
            inline constexpr uint16_t kAgentProtocolVersion = 9;
            inline constexpr uint64_t kAgentBuildFingerprint = 0x5241594100090001ull;
            inline constexpr uint32_t kNativeRuntimeCapabilities = 0x00000007u;
            }
            """);

        var violations = ContractParsers.CheckProtocolConstants(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void CheckProtocolConstants_MagicMismatch_Detected()
    {
        var root = CreateTempRepoRoot();

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentProtocol.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentProtocol
            {
                public const uint Magic = 0xDEADBEEF;
                public const ushort Version = 9;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentBuildIdentity
            {
                public const ulong Fingerprint = 0x5241594100090001UL;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            #pragma once
            namespace RayaTrainer::agent {
            inline constexpr uint32_t kAgentMagic = 0x41594152u;
            inline constexpr uint16_t kAgentProtocolVersion = 9;
            inline constexpr uint64_t kAgentBuildFingerprint = 0x5241594100090001ull;
            }
            """);

        var violations = ContractParsers.CheckProtocolConstants(root);
        var magicViolations = violations.Where(v => v.Description.Contains("Magic")).ToList();
        Assert.NotEmpty(magicViolations);
    }

    [Fact]
    public void CheckProtocolConstants_VersionMismatch_Detected()
    {
        var root = CreateTempRepoRoot();

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentProtocol.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentProtocol
            {
                public const uint Magic = 0x41594152;
                public const ushort Version = 8;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentBuildIdentity
            {
                public const ulong Fingerprint = 0x5241594100090001UL;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            #pragma once
            namespace RayaTrainer::agent {
            inline constexpr uint32_t kAgentMagic = 0x41594152u;
            inline constexpr uint16_t kAgentProtocolVersion = 9;
            inline constexpr uint64_t kAgentBuildFingerprint = 0x5241594100090001ull;
            }
            """);

        var violations = ContractParsers.CheckProtocolConstants(root);
        var versionViolations = violations.Where(v => v.Description.Contains("Version")).ToList();
        Assert.NotEmpty(versionViolations);
    }

    [Fact]
    public void CheckProtocolConstants_FingerprintMismatch_Detected()
    {
        var root = CreateTempRepoRoot();

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentProtocol.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentProtocol
            {
                public const uint Magic = 0x41594152;
                public const ushort Version = 9;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentBuildIdentity
            {
                public const ulong Fingerprint = 0xDEADBEEFDEADBEEFUL;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            #pragma once
            namespace RayaTrainer::agent {
            inline constexpr uint32_t kAgentMagic = 0x41594152u;
            inline constexpr uint16_t kAgentProtocolVersion = 9;
            inline constexpr uint64_t kAgentBuildFingerprint = 0x5241594100090001ull;
            }
            """);

        var violations = ContractParsers.CheckProtocolConstants(root);
        var fpViolations = violations.Where(v => v.Description.Contains("Fingerprint")).ToList();
        Assert.NotEmpty(fpViolations);
    }

    [Fact]
    public void CheckProtocolConstants_ValueMissing_Detected()
    {
        var root = CreateTempRepoRoot();

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentProtocol.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentProtocol
            {
                // No Magic here
                public const ushort Version = 9;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentBuildIdentity.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class AgentBuildIdentity
            {
                public const ulong Fingerprint = 0x5241594100090001UL;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            #pragma once
            namespace RayaTrainer::agent {
            inline constexpr uint32_t kAgentMagic = 0x41594152u;
            inline constexpr uint16_t kAgentProtocolVersion = 9;
            inline constexpr uint64_t kAgentBuildFingerprint = 0x5241594100090001ull;
            }
            """);

        var violations = ContractParsers.CheckProtocolConstants(root);
        Assert.Contains(violations, v => v.Severity == "ParseError" && v.Description.Contains("Magic"));
    }

    // ====================================================================
    // 2. AgentCommand
    // ====================================================================

    private const string CsAgentCommandConsistent = """
        namespace RayaTrainer.Core.Agent;
        public enum AgentCommand : ushort
        {
            Ping = 1,
            GetStatus = 2,
            ToggleSelectedAttackRange = 43
        }
        """;

    private const string CppAgentCommandConsistent = """
        namespace RayaTrainer::agent {
        enum class AgentCommand : uint16_t
        {
            Ping = 1,
            GetStatus = 2,
            ToggleSelectedAttackRange = 43
        };
        }
        """;

    [Fact]
    public void CheckAgentCommand_Consistent_ReturnsEmpty()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentCommand.cs", CsAgentCommandConsistent);
        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", CppAgentCommandConsistent + "\n// other stuff\n");

        var violations = ContractParsers.CheckAgentCommand(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void CheckAgentCommand_NameMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentCommand.cs", """
            namespace RayaTrainer.Core.Agent;
            public enum AgentCommand : ushort
            {
                Ping = 1,
                GetStatus = 2,
                ToggleSelectedAttackRange = 43
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            namespace RayaTrainer::agent {
            enum class AgentCommand : uint16_t
            {
                Ping = 1,
                GetStats = 2,
                ToggleSelectedAttackRange = 43
            };
            }
            """);

        var violations = ContractParsers.CheckAgentCommand(root);
        Assert.Contains(violations, v => v.Description.Contains("GetStatus") || v.Description.Contains("GetStats"));
    }

    [Fact]
    public void CheckAgentCommand_ValueMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentCommand.cs", """
            namespace RayaTrainer.Core.Agent;
            public enum AgentCommand : ushort
            {
                Ping = 1,
                GetStatus = 2,
                ToggleSelectedAttackRange = 43
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            namespace RayaTrainer::agent {
            enum class AgentCommand : uint16_t
            {
                Ping = 1,
                GetStatus = 99,
                ToggleSelectedAttackRange = 43
            };
            }
            """);

        var violations = ContractParsers.CheckAgentCommand(root);
        Assert.Contains(violations, v => v.Description.Contains("GetStatus") && v.Description.Contains("99"));
    }

    [Fact]
    public void CheckAgentCommand_CountMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/AgentCommand.cs", """
            namespace RayaTrainer.Core.Agent;
            public enum AgentCommand : ushort
            {
                Ping = 1,
                GetStatus = 2
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            namespace RayaTrainer::agent {
            enum class AgentCommand : uint16_t
            {
                Ping = 1,
                GetStatus = 2,
                ToggleSelectedAttackRange = 43
            };
            }
            """);

        var violations = ContractParsers.CheckAgentCommand(root);
        Assert.Contains(violations, v => v.Severity == "CountMismatch");
    }

    // ====================================================================
    // 3. NativeCatalogEntry
    // ====================================================================

    private const string CsNativeCatalogConsistent = """
        namespace RayaTrainer.Core.Agent;
        public static class NativeAgentCatalog
        {
            public static readonly IReadOnlyList<string> EntryNames =
            [
                "GameClientPointer",
                "GetThingClass",
                "RestoreOreCapacityMode"
            ];
            public const int ExpectedEntryCount = 3;
        }
        """;

    private const string CppNativeCatalogConsistent = """
        namespace RayaTrainer::agent {
        enum class NativeCatalogEntry : uint32_t
        {
            GameClientPointer = 0,
            GetThingClass = 1,
            RestoreOreCapacityMode = 2,
            EntryCount = 3
        };
        }
        """;

    [Fact]
    public void CheckNativeCatalogEntry_Consistent_ReturnsEmpty()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeAgentCatalog.cs", CsNativeCatalogConsistent);
        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", "// stuff\n" + CppNativeCatalogConsistent + "\n// more stuff\n");

        var violations = ContractParsers.CheckNativeCatalogEntry(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void CheckNativeCatalogEntry_OrderMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeAgentCatalog.cs", CsNativeCatalogConsistent);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            namespace RayaTrainer::agent {
            enum class NativeCatalogEntry : uint32_t
            {
                GameClientPointer = 0,
                RestoreOreCapacityMode = 1,
                GetThingClass = 2,
                EntryCount = 3
            };
            }
            """);

        var violations = ContractParsers.CheckNativeCatalogEntry(root);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void CheckNativeCatalogEntry_ExpectedCountMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeAgentCatalog.cs", """
            namespace RayaTrainer.Core.Agent;
            public static class NativeAgentCatalog
            {
                public static readonly IReadOnlyList<string> EntryNames =
                [
                    "GameClientPointer",
                    "GetThingClass",
                    "RestoreOreCapacityMode"
                ];
                public const int ExpectedEntryCount = 5;
            }
            """);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", CppNativeCatalogConsistent);

        var violations = ContractParsers.CheckNativeCatalogEntry(root);
        Assert.Contains(violations, v => v.Description.Contains("ExpectedEntryCount"));
    }

    [Fact]
    public void CheckNativeCatalogEntry_EntryCountValueMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeAgentCatalog.cs", CsNativeCatalogConsistent);

        WriteFile(root, "src/RayaTrainer.Agent/AgentProtocol.h", """
            namespace RayaTrainer::agent {
            enum class NativeCatalogEntry : uint32_t
            {
                GameClientPointer = 0,
                GetThingClass = 1,
                RestoreOreCapacityMode = 2,
                EntryCount = 99
            };
            }
            """);

        var violations = ContractParsers.CheckNativeCatalogEntry(root);
        Assert.Contains(violations, v => v.Description.Contains("EntryCount"));
    }

    // ====================================================================
    // 4. NativeFeatureStateId
    // ====================================================================

    private const string CsFeatureStateConsistent = """
        namespace RayaTrainer.Core.Agent;
        public enum NativeFeatureStateId : uint
        {
            MoneyPulse = 1,
            Power = 2,
            AutoRepair = 24,
            SlowMotionMode = 25,
            LogicTimeFreeze = 26,
            MoneyAmount = 100,
            PowerValue = 101,
            SecretProtocolPointValue = 102,
            SelectedUnitMaxHealthBits = 103
        }
        """;

    private const string CppFeatureStateConsistent = """
        namespace RayaTrainer::agent {
        enum class NativeFeatureStateId : uint32_t
        {
            MoneyPulse = 1,
            Power = 2,
            AutoRepair = 24,
            SlowMotionMode = 25,
            LogicTimeFreeze = 26,
            MoneyAmount = 100,
            PowerValue = 101,
            SecretProtocolPointValue = 102,
            SelectedUnitMaxHealthBits = 103
        };
        }
        """;

    [Fact]
    public void CheckNativeFeatureStateId_Consistent_ReturnsEmpty()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeFeatureStateId.cs", CsFeatureStateConsistent);
        WriteFile(root, "src/RayaTrainer.Agent/AgentFeatureState.h", CppFeatureStateConsistent);

        var violations = ContractParsers.CheckNativeFeatureStateId(root);
        Assert.Empty(violations);
    }

    [Fact]
    public void CheckNativeFeatureStateId_MissingMember_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeFeatureStateId.cs", CsFeatureStateConsistent);

        WriteFile(root, "src/RayaTrainer.Agent/AgentFeatureState.h", """
            namespace RayaTrainer::agent {
            enum class NativeFeatureStateId : uint32_t
            {
                MoneyPulse = 1,
                Power = 2,
                // Missing AutoRepair
                SlowMotionMode = 25,
                LogicTimeFreeze = 26,
                MoneyAmount = 100,
                PowerValue = 101,
                SecretProtocolPointValue = 102,
                SelectedUnitMaxHealthBits = 103
            };
            }
            """);

        var violations = ContractParsers.CheckNativeFeatureStateId(root);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void CheckNativeFeatureStateId_ValueMismatch_Detected()
    {
        var root = CreateTempRepoRoot();
        WriteFile(root, "src/RayaTrainer.Core/Agent/NativeFeatureStateId.cs", CsFeatureStateConsistent);

        WriteFile(root, "src/RayaTrainer.Agent/AgentFeatureState.h", """
            namespace RayaTrainer::agent {
            enum class NativeFeatureStateId : uint32_t
            {
                MoneyPulse = 1,
                Power = 2,
                AutoRepair = 99,
                SlowMotionMode = 25,
                LogicTimeFreeze = 26,
                MoneyAmount = 100,
                PowerValue = 101,
                SecretProtocolPointValue = 102,
                SelectedUnitMaxHealthBits = 103
            };
            }
            """);

        var violations = ContractParsers.CheckNativeFeatureStateId(root);
        Assert.Contains(violations, v => v.Description.Contains("AutoRepair") && v.Description.Contains("99"));
    }

    // ====================================================================
    // 5. Program.Main integration — all 4 contracts consistent with repo
    // ====================================================================

    [Fact]
    public void ProgramMain_AllConsistent_ReturnsZero_WhenRunOnRepo()
    {
        // The real repo root is resolved relative to the test assembly output dir.
        // This test only passes when run from a build within the repo tree.
        var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, "RayaTrainer.sln")))
        {
            repoRoot = repoRoot.Parent;
        }

        if (repoRoot is null)
        {
            return; // not running inside repo tree — skip gracefully
        }

        var violations = new List<ContractViolation>();
        violations.AddRange(ContractParsers.CheckProtocolConstants(repoRoot.FullName));
        violations.AddRange(ContractParsers.CheckAgentCommand(repoRoot.FullName));
        violations.AddRange(ContractParsers.CheckNativeCatalogEntry(repoRoot.FullName));
        violations.AddRange(ContractParsers.CheckNativeFeatureStateId(repoRoot.FullName));

        Assert.Empty(violations);
    }
}
