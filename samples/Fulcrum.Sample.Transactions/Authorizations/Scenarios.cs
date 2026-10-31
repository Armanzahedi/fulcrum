using Fulcrum.Testing;

namespace Fulcrum.Sample.Transactions.Authorizations;

public static class Scenarios
{
    public static readonly IReadOnlyDictionary<string, (Authorization Authorization, Action<FakeSystemOne> Script)> All =
        new Dictionary<string, (Authorization, Action<FakeSystemOne>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["groceries"] = (
                new("tx-1001", "card-42", 86.40m, "EUR", "Carrefour Market", "FR", "FR", 2, 61.20m, CardPresent: true),
                fake => fake
                    .Answer(FraudScreen.RiskQ, Risk.Unlikely, confidence: 0.94)
                    .Answer(FraudScreen.ConsistentQ, 0.91)
                    .Answer(FraudScreen.MerchantQ, MerchantKind.Groceries, confidence: 0.97)),

            ["crypto"] = (
                new("tx-1002", "card-42", 1_500.00m, "EUR", "FastCoin Exchange", "EE", "FR", 1, 61.20m, CardPresent: false),
                fake => fake
                    .Answer(FraudScreen.RiskQ, Risk.Unlikely, confidence: 0.70)
                    .Answer(FraudScreen.ConsistentQ, 0.35)
                    .Answer(FraudScreen.MerchantQ, MerchantKind.Crypto, confidence: 0.96)),

            ["fraud"] = (
                new("tx-1003", "card-42", 2_999.00m, "USD", "Luxury Watches Online", "US", "FR", 9, 61.20m, CardPresent: false),
                fake => fake
                    .Answer(FraudScreen.RiskQ, Risk.Likely, confidence: 0.91)
                    .Answer(FraudScreen.ConsistentQ, 0.08)
                    .Answer(FraudScreen.MerchantQ, MerchantKind.Other, confidence: 0.80)),
        };
}
