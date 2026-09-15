namespace RayaTrainer.Core.Agent;

public enum ScriptValueKind : byte
{
    Null = 0,
    Integer = 1,
    Real = 2,
    Boolean = 3,
    String = 4,
    PlayerRef = 5,
    ObjectRef = 6,
    Unavailable = 255,
}

public readonly record struct ScriptValue(
    ScriptValueKind Kind,
    long IntegerValue,
    double RealValue,
    bool BooleanValue,
    string? TextValue)
{
    public static ScriptValue Null() => new(ScriptValueKind.Null, 0, 0, false, null);

    public static ScriptValue Integer(long value) => new(ScriptValueKind.Integer, value, 0, false, null);

    public static ScriptValue Real(double value) => new(ScriptValueKind.Real, 0, value, false, null);

    public static ScriptValue Boolean(bool value) => new(ScriptValueKind.Boolean, 0, 0, value, null);

    public static ScriptValue String(string value) => new(ScriptValueKind.String, 0, 0, false, value);

    public static ScriptValue PlayerRef(string selector) => new(ScriptValueKind.PlayerRef, 0, 0, false, selector);

    public static ScriptValue ObjectRef(string selector) => new(ScriptValueKind.ObjectRef, 0, 0, false, selector);

    public static ScriptValue Unavailable(string reason) => new(ScriptValueKind.Unavailable, 0, 0, false, reason);
}
