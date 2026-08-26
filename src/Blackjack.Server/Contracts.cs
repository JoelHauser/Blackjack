using Blackjack.Game;
using SPTarkov.Server.Core.Models.Utils;

namespace Blackjack.Server;

public record DealRequest : IRequestData
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    public int Wager { get; set; }
}

public record ActionRequest : IRequestData
{
    /// <summary>Hit, Stand, Double or Split. Parsed case-insensitively.</summary>
    public string Action { get; set; } = string.Empty;
}

public record StateRequest : IRequestData;

public record StatsRequest : IRequestData;

/// <summary>
/// What every route returns. <see cref="Ok"/> false means the request was refused
/// before anything changed -- the client should show <see cref="Error"/> and keep
/// displaying the round it already had.
/// </summary>
public record BlackjackResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    public RoundView? Round { get; init; }

    /// <summary>
    /// Set when the round proceeded but something went wrong behind it -- notably a
    /// stake that could not be collected. The request still succeeded; the server
    /// operator needs to know, the player does not.
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Balance in the wallet the round is denominated in, after settlement.
    ///
    /// Sent explicitly because a custom static route does not flow through the
    /// ItemEventRouter, so the client's own inventory model is stale until it
    /// refreshes. The UI must trust this number over anything it computes locally.
    /// </summary>
    public int Balance { get; init; }

    public string Wallet { get; init; } = nameof(Server.Wallet.Roubles);

    public static BlackjackResponse Failed(string error) => new() { Ok = false, Error = error };
}
