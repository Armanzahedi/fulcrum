namespace Fulcrum.Sample.Transactions.Authorizations;

public sealed record Authorization(
    string TransactionId,
    string CardId,
    decimal Amount,
    string Currency,
    string MerchantName,
    string MerchantCountry,
    string CardholderCountry,
    int TransactionsLast24h,
    decimal AverageTicketLast90d,
    bool CardPresent);

public enum MerchantKind { Groceries, Travel, Gambling, Crypto, Other }
public enum Risk { Unlikely, Possible, Likely }
public enum AuthOutcome { Approve, StepUp, Decline, Review }

public sealed class FraudScreen : Decision<Authorization, AuthOutcome>
{
    public static readonly YesNoQuestion ConsistentQ = Question.YesNo("consistent",
        "Is this purchase consistent with the cardholder's recent spending pattern?");

    public static readonly ScoreQuestion<Risk> RiskQ = Question.Score<Risk>("risk",
        "How likely is this transaction to be fraudulent?",
        (Risk.Unlikely, "No signals of fraud"),
        (Risk.Possible, "Some unusual signals"),
        (Risk.Likely,   "Strong signals of fraud"));

    public static readonly ChoiceQuestion<MerchantKind> MerchantQ = Question.Choice<MerchantKind>("merchant",
        "What kind of merchant is this?",
        (MerchantKind.Groceries, "Supermarkets, food, everyday retail"),
        (MerchantKind.Travel,    "Airlines, hotels, car rental"),
        (MerchantKind.Gambling,  "Casinos, betting, lotteries"),
        (MerchantKind.Crypto,    "Crypto exchanges and on-ramps"),
        (MerchantKind.Other,     "Anything else"));

    protected override Content Describe(Authorization a) => FormattableString.Invariant($"""
        Amount: {a.Amount} {a.Currency} ({(a.CardPresent ? "card present" : "online")})
        Merchant: {a.MerchantName}, {a.MerchantCountry}
        Cardholder country: {a.CardholderCountry}
        Transactions in last 24h: {a.TransactionsLast24h}
        Average ticket last 90 days: {a.AverageTicketLast90d} {a.Currency}
        """);

    protected override void Define(IRules<AuthOutcome> r)
    {
        r.When(RiskQ.Is(Risk.Likely) && RiskQ.ConfidenceAbove(0.8)).Then(AuthOutcome.Decline);
        r.When(RiskQ.Is(Risk.Likely)).Then(AuthOutcome.Review, name: "likely-but-unsure");
        r.When(MerchantQ.Is(MerchantKind.Crypto) || MerchantQ.Is(MerchantKind.Gambling)).Then(AuthOutcome.StepUp, name: "high-risk-merchant");
        r.When(RiskQ.Is(Risk.Possible) || ConsistentQ.Below(0.5)).Then(AuthOutcome.StepUp, name: "possible-risk");
        r.WhenAnyConfidenceBelow(0.6).Then(AuthOutcome.StepUp, name: "low-confidence");
        r.Default(AuthOutcome.Approve);
        r.OnError(AuthOutcome.StepUp);
    }

    protected override void React(IReactions<Authorization, AuthOutcome> r)
    {
        r.On(AuthOutcome.Approve).With<ApproveHandler>();
        r.On(AuthOutcome.StepUp).With<ThreeDSecureChallengeHandler>();
        r.On(AuthOutcome.Decline).With<DeclineHandler>();
        r.On(AuthOutcome.Review).With<AnalystQueueHandler>();
    }
}
