using Iced.Intel;
using RayaTrainer.Core.Memory;

namespace RayaTrainer.Core.Diagnostics;

public sealed class CompatibilitySampler
{
    private readonly IProcessMemory _memory;

    public CompatibilitySampler(IProcessMemory memory)
    {
        _memory = memory;
    }

    public CompatibilitySampleResult Capture(
        CompatibilitySamplePoint point,
        CompatibilitySampleOptions options)
    {
        if (options.BytesBefore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BytesBefore must be non-negative.");
        }
        if (options.BytesAfter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BytesAfter must be positive.");
        }

        var rangeStart = point.AbsoluteAddress - options.BytesBefore;
        var count = options.BytesBefore + options.BytesAfter;
        try
        {
            var bytes = _memory.ReadBytes(rangeStart, count);
            var actualBytes = point.ExpectedBytes is null
                ? null
                : _memory.ReadBytes(point.AbsoluteAddress, point.ExpectedBytes.Length);
            bool? matchesExpected = point.ExpectedBytes is null
                ? null
                : actualBytes!.SequenceEqual(point.ExpectedBytes);
            var instructions = point.Disassemble
                ? Disassemble(bytes, unchecked((ulong)rangeStart))
                : Array.Empty<CompatibilityInstruction>();

            return new CompatibilitySampleResult(
                point,
                rangeStart,
                bytes,
                actualBytes,
                matchesExpected,
                instructions,
                Error: null);
        }
        catch (Exception ex)
        {
            return new CompatibilitySampleResult(
                point,
                rangeStart,
                Array.Empty<byte>(),
                ActualBytes: null,
                MatchesExpected: null,
                Array.Empty<CompatibilityInstruction>(),
                ex.Message);
        }
    }

    public IReadOnlyList<CompatibilitySampleResult> Capture(
        IReadOnlyList<CompatibilitySamplePoint> points,
        CompatibilitySampleOptions options)
    {
        return points.Select(point => Capture(point, options)).ToArray();
    }

    public static IReadOnlyList<CompatibilityInstruction> Disassemble(byte[] bytes, ulong ip)
    {
        var decoder = Decoder.Create(32, bytes, ip, DecoderOptions.None);
        var formatter = new NasmFormatter();
        var output = new StringFormatterOutput();
        var instructions = new List<CompatibilityInstruction>();

        while (decoder.IP < ip + (uint)bytes.Length)
        {
            var instruction = decoder.Decode();
            output.Reset();
            formatter.Format(instruction, output);
            var start = unchecked((int)(instruction.IP - ip));
            var instructionBytes = start >= 0 && start + instruction.Length <= bytes.Length
                ? bytes.Skip(start).Take(instruction.Length).ToArray()
                : Array.Empty<byte>();
            instructions.Add(new CompatibilityInstruction(
                instruction.IP,
                instruction.Length,
                FormatBytes(instructionBytes),
                output.ToString()));

            if (instruction.Length == 0)
            {
                break;
            }
        }

        return instructions;
    }

    private static string FormatBytes(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(value => value.ToString("X2")));
    }

    private sealed class StringFormatterOutput : FormatterOutput
    {
        private readonly System.Text.StringBuilder _builder = new();

        public void Reset()
        {
            _builder.Clear();
        }

        public override void Write(string text, FormatterTextKind kind)
        {
            _builder.Append(text);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
