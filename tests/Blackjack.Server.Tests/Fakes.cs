using Blackjack.Server;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

/// <summary>
/// A wallet in memory. Records every movement so a test can assert not just the
/// final balance but that money moved the expected number of times -- a double
/// charged twice and a double charged once both end on the same balance if the
/// payout is also wrong.
/// </summary>
internal sealed class FakeBank : IBank
{
    private readonly Dictionary<Wallet, int> _balances =
        Enum.GetValues<Wallet>().ToDictionary(w => w, w => w switch
        {
            Wallet.Roubles => 1_000_000,
            Wallet.Dollars or Wallet.Euros => 10_000,
            _ => 0,
        });

    internal List<(Wallet Wallet, int Amount)> Debits { get; } = [];

    internal List<(Wallet Wallet, int Amount)> Credits { get; } = [];

    /// <summary>Forces TryDebit to fail, simulating money vanishing mid-round.</summary>
    internal bool RefuseDebits { get; set; }

    internal void SetBalance(Wallet wallet, int amount) => _balances[wallet] = amount;

    public int GetBalance(MongoId sessionId, Wallet wallet) => _balances[wallet];

    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount)
    {
        if (RefuseDebits || amount <= 0 || _balances[wallet] < amount)
        {
            return false;
        }

        _balances[wallet] -= amount;
        Debits.Add((wallet, amount));
        return true;
    }

    public void Credit(MongoId sessionId, Wallet wallet, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _balances[wallet] += amount;
        Credits.Add((wallet, amount));
    }
}

internal sealed class FakeProfiles : IProfileGateway
{
    internal bool Exists { get; set; } = true;

    internal int Saves { get; private set; }

    public bool HasProfile(MongoId sessionId) => Exists;

    public Task SaveAsync(MongoId sessionId)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeStats : IStatsStore
{
    private readonly Dictionary<string, PlayerStats> _stats = [];

    internal int Saves { get; private set; }

    public PlayerStats Get(MongoId sessionId)
    {
        var key = sessionId.ToString();
        if (!_stats.TryGetValue(key, out var stats))
        {
            stats = new PlayerStats();
            _stats[key] = stats;
        }

        return stats;
    }

    public void Save(MongoId sessionId, PlayerStats stats)
    {
        _stats[sessionId.ToString()] = stats;
        Saves++;
    }
}
