using Iced.Intel;

namespace RayaTrainer.Core.Agent;

public sealed record SignatureCompatibilityVerificationResult(
    bool Compatible,
    string? Reason = null);

/// <summary>
/// Verifies that a relocated hook still has the same x86 instruction structure as the
/// known profile. Module-internal addresses and relative branch targets may move; opcodes,
/// registers, addressing modes, and structure offsets may not.
/// </summary>
public static class SignatureCompatibilityVerifier
{
    private const ulong MaximumModuleSpan = 0x02000000;

    public static SignatureCompatibilityVerificationResult Verify(
        IReadOnlyList<byte> expectedBytes,
        ulong expectedInstructionPointer,
        IReadOnlyList<byte> actualBytes,
        ulong actualInstructionPointer,
        ulong expectedModuleBase,
        ulong actualModuleBase)
    {
        var expected = Decode(expectedBytes, expectedInstructionPointer);
        var actual = Decode(actualBytes, actualInstructionPointer);
        if (expected is null || actual is null)
        {
            return new(false, "Hook 字节无法完整解码为 x86 指令。");
        }

        if (expected.Count != actual.Count)
        {
            return new(false, $"指令数量变化：expected={expected.Count}, actual={actual.Count}。");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var mismatch = CompareInstruction(
                expected[index],
                actual[index],
                expectedModuleBase,
                actualModuleBase);
            if (mismatch is not null)
            {
                return new(false, $"第 {index + 1} 条指令不兼容：{mismatch}");
            }
        }

        return new(true);
    }

    private static IReadOnlyList<Instruction>? Decode(IReadOnlyList<byte> bytes, ulong instructionPointer)
    {
        var buffer = bytes as byte[] ?? bytes.ToArray();
        var decoder = Decoder.Create(32, buffer, instructionPointer, DecoderOptions.None);
        var end = instructionPointer + (uint)buffer.Length;
        var instructions = new List<Instruction>();
        while (decoder.IP < end)
        {
            var instruction = decoder.Decode();
            if (instruction.Code == Code.INVALID || instruction.Length == 0 || instruction.NextIP > end)
            {
                return null;
            }

            instructions.Add(instruction);
        }

        return decoder.IP == end ? instructions : null;
    }

    private static string? CompareInstruction(
        in Instruction expected,
        in Instruction actual,
        ulong expectedModuleBase,
        ulong actualModuleBase)
    {
        if (expected.Code != actual.Code)
        {
            return $"opcode 变化（{expected.Code} -> {actual.Code}）";
        }

        if (expected.Length != actual.Length ||
            expected.HasLockPrefix != actual.HasLockPrefix ||
            expected.HasRepPrefix != actual.HasRepPrefix ||
            expected.HasRepePrefix != actual.HasRepePrefix ||
            expected.HasRepnePrefix != actual.HasRepnePrefix)
        {
            return "指令长度或前缀变化";
        }

        if (expected.OpCount != actual.OpCount)
        {
            return $"操作数数量变化（{expected.OpCount} -> {actual.OpCount}）";
        }

        for (var operand = 0; operand < expected.OpCount; operand++)
        {
            var expectedKind = expected.GetOpKind(operand);
            var actualKind = actual.GetOpKind(operand);
            if (expectedKind != actualKind)
            {
                return $"操作数 {operand + 1} 类型变化（{expectedKind} -> {actualKind}）";
            }

            switch (expectedKind)
            {
                case OpKind.Register:
                    if (expected.GetOpRegister(operand) != actual.GetOpRegister(operand))
                    {
                        return $"操作数 {operand + 1} 寄存器变化";
                    }
                    break;

                case OpKind.NearBranch16:
                case OpKind.NearBranch32:
                case OpKind.NearBranch64:
                    // Relative branch targets may drift only when both targets stay inside
                    // their respective game modules.
                    if (expected.NearBranchTarget != actual.NearBranchTarget &&
                        !AreRelocatedModuleAddresses(
                            expected.NearBranchTarget,
                            actual.NearBranchTarget,
                            expectedModuleBase,
                            actualModuleBase))
                    {
                        return $"操作数 {operand + 1} 相对分支目标越出模块";
                    }
                    break;

                case OpKind.Immediate8:
                case OpKind.Immediate8_2nd:
                case OpKind.Immediate16:
                case OpKind.Immediate32:
                case OpKind.Immediate64:
                case OpKind.Immediate8to16:
                case OpKind.Immediate8to32:
                case OpKind.Immediate8to64:
                case OpKind.Immediate32to64:
                    var expectedImmediate = expected.GetImmediate(operand);
                    var actualImmediate = actual.GetImmediate(operand);
                    if (expectedImmediate != actualImmediate &&
                        !AreRelocatedModuleAddresses(
                            expectedImmediate,
                            actualImmediate,
                            expectedModuleBase,
                            actualModuleBase))
                    {
                        return $"操作数 {operand + 1} 常量变化";
                    }
                    break;

                case OpKind.Memory:
                    var memoryMismatch = CompareMemoryOperand(
                        expected,
                        actual,
                        expectedModuleBase,
                        actualModuleBase);
                    if (memoryMismatch is not null)
                    {
                        return $"操作数 {operand + 1} {memoryMismatch}";
                    }
                    break;

                case OpKind.FarBranch16:
                case OpKind.FarBranch32:
                    if (expected.FarBranchSelector != actual.FarBranchSelector ||
                        expected.FarBranch32 != actual.FarBranch32)
                    {
                        return $"操作数 {operand + 1} 远跳转目标变化";
                    }
                    break;
            }
        }

        return null;
    }

    private static string? CompareMemoryOperand(
        in Instruction expected,
        in Instruction actual,
        ulong expectedModuleBase,
        ulong actualModuleBase)
    {
        if (expected.MemoryBase != actual.MemoryBase ||
            expected.MemoryIndex != actual.MemoryIndex ||
            expected.MemoryIndexScale != actual.MemoryIndexScale ||
            expected.MemoryDisplSize != actual.MemoryDisplSize ||
            expected.MemorySize != actual.MemorySize ||
            expected.SegmentPrefix != actual.SegmentPrefix)
        {
            return "寻址结构变化";
        }

        var expectedDisplacement = expected.MemoryDisplacement64;
        var actualDisplacement = actual.MemoryDisplacement64;
        if (expectedDisplacement == actualDisplacement)
        {
            return null;
        }

        var isAbsoluteAddress = expected.MemoryBase == Register.None && expected.MemoryIndex == Register.None;
        return isAbsoluteAddress && AreRelocatedModuleAddresses(
            expectedDisplacement,
            actualDisplacement,
            expectedModuleBase,
            actualModuleBase)
            ? null
            : "位移/结构偏移变化";
    }

    private static bool AreRelocatedModuleAddresses(
        ulong expected,
        ulong actual,
        ulong expectedModuleBase,
        ulong actualModuleBase)
    {
        return expected >= expectedModuleBase &&
               expected - expectedModuleBase < MaximumModuleSpan &&
               actual >= actualModuleBase &&
               actual - actualModuleBase < MaximumModuleSpan;
    }
}
