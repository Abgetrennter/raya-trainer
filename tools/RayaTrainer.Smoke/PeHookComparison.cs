internal sealed record HookComparisonLine(
    string Address,
    string SectionTitle,
    string Location,
    byte[] ExpectedBytes,
    byte[] ActualBytes,
    bool Matches);
