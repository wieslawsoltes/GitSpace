namespace GitSpace.Core;

public sealed partial record GitRequest
{
    /// <summary>Review hashes include existence, mode and exact UTF-8 content, not just HEAD.</summary>
    public string BeforeHash { get; init; } = "";
    public string AfterHash { get; init; } = "";
    public string IndexHash { get; init; } = "";
    public bool Remove { get; init; }
    public int Limit { get; init; } = 200;
}
public sealed partial record GitResult
{
    public string BeforeHash { get; init; } = "";
    public string AfterHash { get; init; } = "";
    public bool BeforeExists { get; init; }
    public bool AfterExists { get; init; }
    public string BaseText { get; init; } = "";
    public bool BaseExists { get; init; }
}
public sealed partial record GitSnapshot
{
    public string IndexHash { get; init; } = "";
    public bool HasMoreHistory { get; init; }
    public int HistoryLimit { get; init; } = 200;
}
