namespace Fulcrum;

public sealed class SystemOneException : Exception
{
    public SystemOneException(string message) : base(message) => Violations = [];

    public SystemOneException(string message, Exception inner) : base(message, inner) => Violations = [];

    public SystemOneException(IReadOnlyList<string> violations) : base(string.Join("; ", violations)) => Violations = violations;

    public IReadOnlyList<string> Violations { get; }
}
