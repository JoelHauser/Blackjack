using System.Collections.Concurrent;
using Blackjack.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server;

/// <summary>
/// One player's seat: their table, the currency the current round is denominated
/// in, and how much has actually been taken from them so far.
/// </summary>
public sealed class PlayerSession
{
    public required BlackjackTable Table { get; init; }

    public Wallet Wallet { get; set; } = Wallet.Roubles;

    /// <summary>
    /// Roubles (or dollars, or euros) already debited for the live round. Doubling
    /// and splitting raise the table's stake after the fact, so the difference
    /// between this and <c>Table.TotalWagered</c> is what still needs collecting.
    /// </summary>
    public int Staked { get; set; }
}

/// <summary>
/// Live tables, keyed by session.
///
/// Deliberately in memory only. A half-played hand has no business surviving a
/// server restart, and keeping it out of the profile means this mod never changes
/// the profile schema -- so uninstalling it cannot corrupt a save. The player's
/// actual money is in their stash, which SPT already persists.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class TableStore
{
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();

    public PlayerSession For(MongoId sessionId) =>
        _sessions.GetOrAdd(
            sessionId.ToString(),
            _ => new PlayerSession { Table = new BlackjackTable(new Rules()) });

    /// <summary>Drops a seat, abandoning any in-progress round.</summary>
    public void Clear(MongoId sessionId) => _sessions.TryRemove(sessionId.ToString(), out _);
}
