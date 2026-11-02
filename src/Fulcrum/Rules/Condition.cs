using System.Globalization;

namespace Fulcrum;

/// <summary>
/// A rule condition built from question handles. <c>&amp;&amp;</c> and <c>||</c> build a tree and do not short-circuit:
/// both operands are always constructed, and every leaf is evaluated against the answers.
/// </summary>
public abstract record Condition
{
    private protected Condition()
    {
    }

    public static Condition operator &(Condition l, Condition r) => new AndCondition(l, r);

    public static Condition operator |(Condition l, Condition r) => new OrCondition(l, r);

    public static Condition operator !(Condition c) => new NotCondition(c);

    public static bool operator true(Condition c) => false;

    public static bool operator false(Condition c) => false;

    public abstract override string ToString();

    private protected static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record AndCondition(Condition Left, Condition Right) : Condition
{
    public override string ToString() => $"{Wrap(Left)} && {Wrap(Right)}";

    private static string Wrap(Condition c) => c is OrCondition ? $"({c})" : c.ToString();
}

internal sealed record OrCondition(Condition Left, Condition Right) : Condition
{
    public override string ToString() => $"{Wrap(Left)} || {Wrap(Right)}";

    private static string Wrap(Condition c) => c is AndCondition ? $"({c})" : c.ToString();
}

internal sealed record NotCondition(Condition Inner) : Condition
{
    public override string ToString() => Inner is Leaf ? $"!{Inner}" : $"!({Inner})";
}

internal abstract record Leaf(Question Question) : Condition;

internal sealed record ChoiceIs(Question Question, string Option, bool Negated) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}{(Negated ? "!=" : "=")}{Option}";
}

internal sealed record ConfidenceAbove(Question Question, double T) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}.confidence>{Num(T)}";
}

internal sealed record YesNoAbove(Question Question, double T) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}>{Num(T)}";
}

internal sealed record YesNoBelow(Question Question, double T) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}<{Num(T)}";
}

internal sealed record ScoreIs(Question Question, int Position, bool Negated) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}{(Negated ? "!=" : "=")}{Question.Choices[Position].Name}";
}

internal sealed record ScoreAtLeast(Question Question, int Position) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}>={Question.Choices[Position].Name}";
}

internal sealed record ScoreAtMost(Question Question, int Position) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}<={Question.Choices[Position].Name}";
}

internal sealed record BoxFound(Question Question) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}.found";
}

internal sealed record BoxNone(Question Question) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}.none";
}

internal sealed record BoxFoundAbove(Question Question, double T) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}.found>{Num(T)}";
}

internal enum CountOp { Is, AtLeast, Above, Below }

internal sealed record BoxCount(Question Question, CountOp Op, int N) : Leaf(Question)
{
    public override string ToString() => $"{Question.Name}.count{Op switch { CountOp.Is => "=", CountOp.AtLeast => ">=", CountOp.Above => ">", _ => "<" }}{N}";
}
