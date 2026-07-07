using System.Globalization;
using Iced.Intel;
using RayaTrainer.Core.Codegen;
using static Iced.Intel.AssemblerRegisters;

namespace RayaTrainer.Core.Patching;

internal static partial class RestoreAssemblyEncoder
{
    public static byte[] Encode(IReadOnlyList<string> assembly)
    {
        var assembler = new Assembler(32);
        foreach (var instruction in assembly)
        {
            Emit(assembler, instruction.Trim());
        }

        using var stream = new MemoryStream();
        assembler.Assemble(new StreamCodeWriter(stream), 0);
        return stream.ToArray();
    }

    private static void Emit(Assembler assembler, string instruction)
    {
        var normalized = instruction.ToLowerInvariant();
        if (normalized == "nop")
        {
            assembler.nop();
            return;
        }

        var parts = SplitInstruction(instruction);
        switch (parts.Mnemonic)
        {
            case "add":
                assembler.add(Register32(parts.Operands[0]), Memory(parts.Operands[1]));
                return;
            case "cmp":
                if (IsMemory(parts.Operands[1]))
                {
                    assembler.cmp(Register32(parts.Operands[0]), Memory(parts.Operands[1]));
                    return;
                }
                EmitCmpRegisterRegister(assembler, parts.Operands[0], parts.Operands[1]);
                return;
            case "cvttss2si":
                assembler.cvttss2si(Register32(parts.Operands[0]), Memory(parts.Operands[1]));
                return;
            case "fld":
                assembler.fld(DwordMemory(parts.Operands[0]));
                return;
            case "mov":
                EmitMov(assembler, parts.Operands);
                return;
            case "movss":
                EmitMovss(assembler, parts.Operands);
                return;
            case "sub":
                assembler.sub(Register32(parts.Operands[0]), Memory(parts.Operands[1]));
                return;
            case "test":
                assembler.test(Register32(parts.Operands[0]), Register32(parts.Operands[1]));
                return;
            default:
                throw new NotSupportedException($"Unsupported restore instruction '{instruction}'.");
        }
    }

    private static void EmitMov(Assembler assembler, IReadOnlyList<string> operands)
    {
        if (IsMemory(operands[0]))
        {
            assembler.mov(Memory(operands[0]), Register32(operands[1]));
            return;
        }

        if (!IsMemory(operands[1]))
        {
            EmitMovRegisterRegister(assembler, operands[0], operands[1]);
            return;
        }

        assembler.mov(Register32(operands[0]), Memory(operands[1]));
    }

    private static void EmitMovss(Assembler assembler, IReadOnlyList<string> operands)
    {
        if (IsMemory(operands[0]))
        {
            assembler.movss(Memory(operands[0]), RegisterXmm(operands[1]));
            return;
        }

        assembler.movss(RegisterXmm(operands[0]), Memory(operands[1]));
    }

    private static void EmitCmpRegisterRegister(Assembler assembler, string destination, string source)
    {
        // RA3's original bytes use CMP r32,r/m32 (3B /r); Iced's default emits the equivalent 39 /r form.
        var modRm = 0xC0 | (RegisterIndex(destination) << 3) | RegisterIndex(source);
        assembler.db(0x3B, (byte)modRm);
    }

    private static void EmitMovRegisterRegister(Assembler assembler, string destination, string source)
    {
        // RA3's original bytes use MOV r32,r/m32 (8B /r); Iced's default may emit the equivalent 89 /r form.
        var modRm = 0xC0 | (RegisterIndex(destination) << 3) | RegisterIndex(source);
        assembler.db(0x8B, (byte)modRm);
    }

    private static (string Mnemonic, IReadOnlyList<string> Operands) SplitInstruction(string instruction)
    {
        var firstSpace = instruction.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (instruction.ToLowerInvariant(), Array.Empty<string>());
        }

        var mnemonic = instruction[..firstSpace].ToLowerInvariant();
        var operands = instruction[(firstSpace + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (mnemonic, operands);
    }

    private static bool IsMemory(string operand) => operand.TrimStart().StartsWith("[", StringComparison.Ordinal);

    private static AssemblerMemoryOperand Memory(string operand)
    {
        var body = operand.Trim();
        body = body.Replace("dword ptr ", "", StringComparison.OrdinalIgnoreCase);
        if (!body.StartsWith("[", StringComparison.Ordinal) || !body.EndsWith("]", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported memory operand '{operand}'.");
        }

        body = body[1..^1].Trim();
        if (body.Contains('*', StringComparison.Ordinal))
        {
            return IndexedMemory(body);
        }

        var signIndex = body.IndexOfAny(['+', '-']);
        if (signIndex < 0)
        {
            return __[Register32(body)];
        }

        var register = body[..signIndex].Trim();
        var displacement = ParseNumber(body[(signIndex + 1)..]);
        if (body[signIndex] == '-')
        {
            displacement = -displacement;
        }

        return Register32(register) + displacement;
    }

    private static AssemblerMemoryOperand DwordMemory(string operand)
    {
        return __dword_ptr[Memory(operand)];
    }

    private static AssemblerMemoryOperand IndexedMemory(string body)
    {
        var plusIndex = body.IndexOf('+');
        var baseRegister = Register32(body[..plusIndex].Trim());
        var indexParts = body[(plusIndex + 1)..].Split('*', StringSplitOptions.TrimEntries);
        return baseRegister + Register32(indexParts[0]) * int.Parse(indexParts[1], CultureInfo.InvariantCulture);
    }

    private static AssemblerRegister32 Register32(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "eax" => eax,
            "ebx" => ebx,
            "ecx" => ecx,
            "edx" => edx,
            "esi" => esi,
            "edi" => edi,
            "esp" => esp,
            "ebp" => ebp,
            _ => throw new NotSupportedException($"Unsupported 32-bit register '{name}'.")
        };
    }

    private static int RegisterIndex(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "eax" => 0,
            "ecx" => 1,
            "edx" => 2,
            "ebx" => 3,
            "esp" => 4,
            "ebp" => 5,
            "esi" => 6,
            "edi" => 7,
            _ => throw new NotSupportedException($"Unsupported 32-bit register '{name}'.")
        };
    }

    private static AssemblerRegisterXMM RegisterXmm(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "xmm0" => xmm0,
            _ => throw new NotSupportedException($"Unsupported XMM register '{name}'.")
        };
    }

    private static int ParseNumber(string value)
    {
        return X86NumberParser.ParseInt32(value);
    }
}
