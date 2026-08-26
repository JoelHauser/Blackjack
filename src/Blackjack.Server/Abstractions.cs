using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server;

/// <summary>
/// Everything the game logic needs to do with the player's money.
///
/// This exists as an interface because SPT's InventoryHelper and ProfileHelper are
/// concrete classes with non-virtual methods -- depending on them directly makes
/// the calling code impossible to test without a running server. SPT's DI registers
/// a class against every interface it implements, so <see cref="Bank"/> resolves
/// for this with no extra wiring.
///
/// Note it takes a session id rather than a PmcData: that keeps every SPT profile
/// model out of the game logic entirely.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>Takes money. False means nothing was touched.</summary>
    bool TryDebit(MongoId sessionId, Wallet wallet, int amount);

    void Credit(MongoId sessionId, Wallet wallet, int amount);
}

public interface IProfileGateway
{
    bool HasProfile(MongoId sessionId);

    /// <summary>Flushes money changes to disk. Money that is not saved did not move.</summary>
    Task SaveAsync(MongoId sessionId);
}
