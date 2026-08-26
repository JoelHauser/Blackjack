namespace Blackjack.Game;

/// <summary>
/// Table rules. Defaults are a standard 6-deck shoe game: dealer stands on all
/// 17s, blackjack pays 3:2. Every value here is exposed as mod config, so the
/// engine must never assume a default is in force.
/// </summary>
public sealed record Rules
{
    public int DeckCount { get; init; } = 6;

    /// <summary>When true the dealer draws to soft 17 instead of standing.</summary>
    public bool DealerHitsSoft17 { get; init; }

    /// <summary>
    /// Profit multiplier on a natural. 1.0 means every win pays the same -- double
    /// the stake back -- which is the rule this table runs.
    ///
    /// Casino blackjack pays 3:2 (1.5). Setting that back reintroduces a problem for
    /// valuables: one bitcoin at 3:2 settles on two and a half, and half a bitcoin
    /// does not exist. Even money divides cleanly in every currency.
    /// </summary>
    public double BlackjackPayout { get; init; } = 1.0;

    public bool DoubleAfterSplit { get; init; } = true;

    /// <summary>Number of splits allowed, so 3 means up to four hands.</summary>
    public int MaxSplits { get; init; } = 3;

    /// <summary>
    /// Split aces normally receive exactly one card each and are then forced to
    /// stand. Turning this off makes split aces play like any other hand.
    /// </summary>
    public bool OneCardAfterAceSplit { get; init; } = true;

    public bool AllowResplitAces { get; init; }

    /// <summary>Fraction of the shoe dealt before it is reshuffled.</summary>
    public double ShufflePenetration { get; init; } = 0.75;

    public int MinBet { get; init; } = 1_000;

    public int MaxBet { get; init; } = 500_000;
}
