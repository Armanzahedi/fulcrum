namespace Fulcrum;

internal enum QuestionKind { Choice, YesNo, Score, Box }

public abstract record Question
{
    private protected Question(string name, string text)
    {
        Name = name;
        Text = text;
    }

    public string Name { get; }

    public string Text { get; }

    internal abstract QuestionKind Kind { get; }

    internal virtual IReadOnlyList<(string Name, string Description)> Choices => [];

    internal virtual IEnumerable<string> Problems() => [];

    public static ChoiceQuestion<TEnum> Choice<TEnum>(string name, string text, params ReadOnlySpan<(TEnum, string)> options)
        where TEnum : struct, Enum => new(CheckName(name), text, options.ToArray());

    public static YesNoQuestion YesNo(string name, string text) => new(CheckName(name), text);

    public static ScoreQuestion<TEnum> Score<TEnum>(string name, string text, params ReadOnlySpan<(TEnum, string)> levels)
        where TEnum : struct, Enum => new(CheckName(name), text, levels.ToArray());

    public static BoxQuestion Box(string name, string text) => new(CheckName(name), text);

    private static string CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}

public sealed partial record ChoiceQuestion<TEnum> : Question where TEnum : struct, Enum
{
    internal ChoiceQuestion(string name, string text, IReadOnlyList<(TEnum Value, string Description)> options) : base(name, text)
    {
        Options = options;
        Choices = [.. options.Select(o => (Enum.GetName(o.Value) ?? o.Value.ToString(), o.Description))];
    }

    public IReadOnlyList<(TEnum Value, string Description)> Options { get; }

    internal override QuestionKind Kind => QuestionKind.Choice;

    internal override IReadOnlyList<(string Name, string Description)> Choices { get; }

    internal override IEnumerable<string> Problems()
    {
        if (Options.Count < 2)
            yield return $"'{Name}' needs at least two options";
        foreach (var repeated in Options.GroupBy(o => o.Value).Where(g => g.Count() > 1))
            yield return $"'{Name}' repeats option {repeated.Key}";
    }
}

public sealed partial record YesNoQuestion : Question
{
    internal YesNoQuestion(string name, string text) : base(name, text) { }

    internal override QuestionKind Kind => QuestionKind.YesNo;
}

public sealed partial record ScoreQuestion<TEnum> : Question where TEnum : struct, Enum
{
    internal ScoreQuestion(string name, string text, IReadOnlyList<(TEnum Value, string Description)> levels) : base(name, text)
    {
        Levels = levels;
        Choices = [.. levels.Select(l => (Enum.GetName(l.Value) ?? l.Value.ToString(), l.Description))];
    }

    public IReadOnlyList<(TEnum Value, string Description)> Levels { get; }

    internal override QuestionKind Kind => QuestionKind.Score;

    internal override IReadOnlyList<(string Name, string Description)> Choices { get; }

    internal override IEnumerable<string> Problems()
    {
        if (Levels.Count < 2)
            yield return $"'{Name}' needs at least two levels";
        foreach (var repeated in Levels.GroupBy(l => l.Value).Where(g => g.Count() > 1))
            yield return $"'{Name}' repeats level {repeated.Key}";
        foreach (var missing in Enum.GetValues<TEnum>().Where(v => !Levels.Any(l => EqualityComparer<TEnum>.Default.Equals(l.Value, v))))
            yield return $"'{Name}' does not list level {missing}";
    }
}

public sealed partial record BoxQuestion : Question
{
    internal BoxQuestion(string name, string text) : base(name, text) { }

    internal override QuestionKind Kind => QuestionKind.Box;
}
