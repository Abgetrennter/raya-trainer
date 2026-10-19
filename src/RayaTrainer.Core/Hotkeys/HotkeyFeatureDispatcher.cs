namespace RayaTrainer.Core.Hotkeys;

public sealed record HotkeyActionBinding(
    HotkeyGesture Gesture,
    Action Execute,
    Func<bool>? CanExecute = null,
    bool AllowRepeat = false);

public sealed class HotkeyFeatureDispatcher
{
    public static readonly TimeSpan DefaultRepeatInterval = TimeSpan.FromMilliseconds(200);

    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<HotkeyGesture, DateTimeOffset> _lastDispatchTimes = [];
    private readonly TimeSpan _repeatInterval;
    private readonly Func<DateTimeOffset> _getNow;
    private IReadOnlyDictionary<HotkeyGesture, HotkeyActionBinding[]> _bindings =
        new Dictionary<HotkeyGesture, HotkeyActionBinding[]>();

    public HotkeyFeatureDispatcher()
        : this(DefaultRepeatInterval)
    {
    }

    public HotkeyFeatureDispatcher(TimeSpan repeatInterval, Func<DateTimeOffset>? getNow = null)
    {
        if (repeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatInterval), "Repeat interval cannot be negative.");
        }

        _repeatInterval = repeatInterval;
        _getNow = getNow ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled { get; private set; }

    public void Update(IEnumerable<HotkeyActionBinding> bindings, bool enabled)
    {
        _bindings = bindings
            .GroupBy(binding => binding.Gesture)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        Enabled = enabled;
        _pressedKeys.Clear();
        _lastDispatchTimes.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            _pressedKeys.Clear();
            _lastDispatchTimes.Clear();
        }
    }

    public bool TryDispatch(int virtualKey, HotkeyModifiers modifiers)
    {
        if (!Enabled)
        {
            return false;
        }

        var gesture = _bindings.Keys.FirstOrDefault(key =>
            key.VirtualKey == virtualKey && key.Modifiers == modifiers);
        if (gesture is null || !_bindings.TryGetValue(gesture, out var bindings))
        {
            return false;
        }

        var executableBindings = bindings
            .Where(binding => binding.CanExecute?.Invoke() ?? true)
            .ToArray();
        if (executableBindings.Length == 0)
        {
            return false;
        }

        var now = _getNow();
        if (!_pressedKeys.Add(virtualKey))
        {
            var repeatableBindings = executableBindings
                .Where(binding => binding.AllowRepeat)
                .ToArray();
            if (repeatableBindings.Length == 0 || IsRepeatThrottled(gesture, now))
            {
                return true;
            }

            _lastDispatchTimes[gesture] = now;
            foreach (var binding in repeatableBindings)
            {
                binding.Execute();
            }

            return true;
        }

        _lastDispatchTimes[gesture] = now;
        foreach (var binding in executableBindings)
        {
            binding.Execute();
        }

        return true;
    }

    public void Release(int virtualKey)
    {
        _pressedKeys.Remove(virtualKey);
        foreach (var gesture in _lastDispatchTimes.Keys.Where(gesture => gesture.VirtualKey == virtualKey).ToArray())
        {
            _lastDispatchTimes.Remove(gesture);
        }
    }

    private bool IsRepeatThrottled(HotkeyGesture gesture, DateTimeOffset now) =>
        _lastDispatchTimes.TryGetValue(gesture, out var lastDispatch) &&
        now - lastDispatch < _repeatInterval;
}
