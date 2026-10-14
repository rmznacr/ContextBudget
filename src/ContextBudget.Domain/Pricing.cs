namespace ContextBudget.Domain;

public sealed class PricingProfile
{
    private PricingProfile() { }
    public PricingProfile(string provider, string modelId, string currency, decimal inputPricePerMillionTokens, decimal outputPricePerMillionTokens, decimal? cachedInputPricePerMillionTokens, decimal? cacheWritePricePerMillionTokens, DateTimeOffset effectiveFromUtc, DateTimeOffset? effectiveToUtc, string pricingMode, string sourceDescription)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 80) throw new ArgumentException("Provider is required (max 80 characters).");
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 200) throw new ArgumentException("Model is required (max 200 characters).");
        if (currency?.Length != 3 || currency.Any(c => !char.IsAsciiLetter(c))) throw new ArgumentException("Currency must be a 3-letter ISO code.");
        if (inputPricePerMillionTokens < 0 || outputPricePerMillionTokens < 0 || cachedInputPricePerMillionTokens < 0 || cacheWritePricePerMillionTokens < 0) throw new ArgumentException("Prices cannot be negative.");
        if (new[] { inputPricePerMillionTokens, outputPricePerMillionTokens, cachedInputPricePerMillionTokens, cacheWritePricePerMillionTokens }.Where(x => x.HasValue).Any(x => x!.Value >= 1_000_000_000_000m || decimal.Round(x.Value, 12) != x.Value)) throw new ArgumentException("Prices must fit numeric(24,12) without rounding.");
        if (effectiveToUtc <= effectiveFromUtc) throw new ArgumentException("EffectiveTo must be later than EffectiveFrom.");
        if (string.IsNullOrWhiteSpace(sourceDescription) || sourceDescription.Length > 500) throw new ArgumentException("Source description is required (max 500 characters).");
        var normalizedMode = string.IsNullOrWhiteSpace(pricingMode) ? "cached-included-in-input" : pricingMode.Trim();
        if (normalizedMode != "cached-included-in-input") throw new ArgumentException("Only cached-included-in-input billing semantics are supported.");
        Id = Guid.NewGuid(); Provider = provider.Trim(); ModelId = modelId.Trim(); Currency = currency.ToUpperInvariant(); InputPricePerMillionTokens = inputPricePerMillionTokens; OutputPricePerMillionTokens = outputPricePerMillionTokens; CachedInputPricePerMillionTokens = cachedInputPricePerMillionTokens; CacheWritePricePerMillionTokens = cacheWritePricePerMillionTokens; EffectiveFromUtc = effectiveFromUtc.ToUniversalTime(); EffectiveToUtc = effectiveToUtc?.ToUniversalTime(); PricingMode = normalizedMode; SourceDescription = sourceDescription.Trim(); CreatedAtUtc = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; } public string Provider { get; private set; } = ""; public string ModelId { get; private set; } = ""; public string Currency { get; private set; } = "";
    public decimal InputPricePerMillionTokens { get; private set; } public decimal OutputPricePerMillionTokens { get; private set; } public decimal? CachedInputPricePerMillionTokens { get; private set; } public decimal? CacheWritePricePerMillionTokens { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; } public DateTimeOffset? EffectiveToUtc { get; private set; } public string PricingMode { get; private set; } = "cached-included-in-input"; public string SourceDescription { get; private set; } = ""; public DateTimeOffset CreatedAtUtc { get; private set; }
    public void CloseAt(DateTimeOffset end) { if (EffectiveToUtc.HasValue) throw new InvalidOperationException("Pricing version is immutable once closed."); if (end <= EffectiveFromUtc) throw new ArgumentException("New version must start after the current version."); EffectiveToUtc = end.ToUniversalTime(); }
}
