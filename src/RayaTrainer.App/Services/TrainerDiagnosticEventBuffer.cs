using RayaTrainer.Core.Diagnostics;

namespace RayaTrainer.App.Services;

internal sealed class TrainerDiagnosticEventBuffer
{
    public const int Capacity = 200;

    private readonly object _sync = new();
    private readonly List<TrainerDiagnosticEvent> _events = [];
    private long _nextSequence;

    public IReadOnlyList<TrainerDiagnosticEvent> Snapshot(int maxCount = Capacity)
    {
        lock (_sync)
        {
            return _events.TakeLast(Math.Clamp(maxCount, 0, Capacity)).ToArray();
        }
    }

    public bool Add(
        DiagnosticEventSeverity severity,
        string code,
        string message,
        string? detail = null)
    {
        lock (_sync)
        {
            var previous = _events.LastOrDefault();
            if (severity == DiagnosticEventSeverity.Error &&
                previous is not null &&
                previous.Severity == severity &&
                previous.Code.Equals(code, StringComparison.Ordinal) &&
                previous.Message.Equals(message, StringComparison.Ordinal) &&
                string.Equals(previous.Detail, detail, StringComparison.Ordinal))
            {
                return false;
            }

            _events.Add(new TrainerDiagnosticEvent(
                ++_nextSequence,
                DateTimeOffset.Now,
                severity,
                code,
                message,
                detail));
            if (_events.Count > Capacity)
            {
                _events.RemoveRange(0, _events.Count - Capacity);
            }

            return true;
        }
    }
}
