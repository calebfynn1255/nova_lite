using System.Text.RegularExpressions;

namespace NovaLite.Core.Services;

/// <summary>
/// Prevents an offline model from presenting changing market information as fact.
/// The local model has no live catalogue, retailer, or exchange-rate source.
/// </summary>
public static class ResponseReliabilityGuard
{
    private static readonly Regex CurrentMarketPattern = new(
        @"\b(?:price|prices|cost|costs|deal|deals|sale|sales|discount|stock|availability|available|exchange rate|convert|conversion|cedi|cedis|ghs|usd|dollar|dollars|pound|pounds|eur|euro|euros)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PurchasableProductPattern = new(
        @"\b(?:recommend|recommendation|buy|buying|purchase|looking for|need something with)\b.*\b(?:laptop|computer|pc|phone|tablet|gpu|graphics card|rtx|geforce|radeon|console|monitor|television|tv|car|camera|headphones?)\b|\b(?:rtx|geforce|radeon)\s*\d{3,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryCreateResponse(string userText, out string response)
    {
        response = string.Empty;
        if (string.IsNullOrWhiteSpace(userText))
            return false;

        if (CurrentMarketPattern.IsMatch(userText))
        {
            response = "I don't have live retailer or exchange-rate data in this local session, so I can't verify current prices or convert an estimate responsibly. I won't invent figures. Share a product listing or a verified exchange rate and I can compare the options or calculate the conversion exactly.";
            return true;
        }

        if (PurchasableProductPattern.IsMatch(userText))
        {
            response = "I can help you choose based on requirements, but I don't have a live product catalogue to verify current models, configurations, prices, or local availability. I won't guess. Tell me your budget and priorities, or share a few listings, and I'll compare them carefully.";
            return true;
        }

        return false;
    }
}
