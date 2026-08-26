namespace Blackjack.Game;

public enum RoundPhase
{
    /// <summary>No hand in progress -- the table is waiting for a bet.</summary>
    AwaitingBet,
    PlayerTurn,
    DealerTurn,
    Settled,
}

public enum PlayerAction
{
    Hit,
    Stand,
    Double,
    Split,
}

public enum HandOutcome
{
    Pending,
    Win,
    Lose,
    Push,
    Blackjack,
    Bust,
}

/// <summary>
/// What one hand looks like to the client. Sent over the wire, so it carries the
/// derived values (total, soft, outcome) rather than making the client recompute
/// rules it should not know.
/// </summary>
public sealed record HandView(
    IReadOnlyList<string> Cards,
    int Value,
    bool IsSoft,
    int Wager,
    HandStatus Status,
    HandOutcome Outcome,
    int Returned);

/// <summary>
/// The complete snapshot handed back after every action. This is the only thing
/// the client ever sees -- see <see cref="BlackjackTable.View"/> for why the
/// dealer's hole card is absent from it during the player's turn.
/// </summary>
public sealed record RoundView(
    RoundPhase Phase,
    IReadOnlyList<HandView> PlayerHands,
    HandView Dealer,
    int ActiveHandIndex,
    IReadOnlyList<PlayerAction> AvailableActions,
    int TotalWagered,
    int TotalReturned,
    int ShoeRemaining)
{
    /// <summary>Profit or loss for the round. Negative means the house won.</summary>
    public int Net => TotalReturned - TotalWagered;
}
