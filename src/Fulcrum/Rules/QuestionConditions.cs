namespace Fulcrum;

public sealed partial record ChoiceQuestion<TEnum>
{
    public Condition Is(TEnum value) => new ChoiceIs(this, Enum.GetName(value) ?? value.ToString(), false);

    public Condition IsNot(TEnum value) => new ChoiceIs(this, Enum.GetName(value) ?? value.ToString(), true);

    public Condition ConfidenceAbove(double t) => new ConfidenceAbove(this, t);
}

public sealed partial record YesNoQuestion
{
    public Condition Above(double t) => new YesNoAbove(this, t);

    public Condition Below(double t) => new YesNoBelow(this, t);
}

public sealed partial record ScoreQuestion<TEnum>
{
    public Condition Is(TEnum level) => new ScoreIs(this, Position(level), false);

    public Condition IsNot(TEnum level) => new ScoreIs(this, Position(level), true);

    public Condition AtLeast(TEnum level) => new ScoreAtLeast(this, Position(level));

    public Condition AtMost(TEnum level) => new ScoreAtMost(this, Position(level));

    public Condition ConfidenceAbove(double t) => new ConfidenceAbove(this, t);

    private int Position(TEnum level)
    {
        for (var i = 0; i < Levels.Count; i++)
        {
            if (EqualityComparer<TEnum>.Default.Equals(Levels[i].Value, level))
                return i;
        }
        throw new FulcrumException($"{Name}: {level} is not a declared level");
    }
}

public sealed partial record BoxQuestion
{
    public Condition Found() => new BoxFound(this);

    public Condition None() => new BoxNone(this);

    public Condition FoundAbove(double confidence) => new BoxFoundAbove(this, confidence);

    public CountRef Count() => new(this);
}

public sealed class CountRef
{
    private readonly BoxQuestion _question;

    internal CountRef(BoxQuestion question) => _question = question;

    public Condition Is(int n) => new BoxCount(_question, CountOp.Is, n);

    public Condition AtLeast(int n) => new BoxCount(_question, CountOp.AtLeast, n);

    public Condition Above(int n) => new BoxCount(_question, CountOp.Above, n);

    public Condition Below(int n) => new BoxCount(_question, CountOp.Below, n);
}
