using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

public class WalletTests
{
    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly FakeStats _stats = new();
    private readonly TableStore _tables = new();

    private BlackjackService WithDeal(string cards)
    {
        _tables.Seed(_session, new BlackjackTable(
            new Rules { MinBet = 1, MaxBet = int.MaxValue },
            Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));

        return new BlackjackService(_bank, _profiles, _tables, _stats);
    }

    [Fact]
    public void EveryWalletHasLimitsAndATemplate()
    {
        foreach (var wallet in Enum.GetValues<Wallet>())
        {
            var info = WalletInfo.For(wallet);

            Assert.False(info.Tpl.IsEmpty, $"{wallet} has no template id.");
            Assert.True(info.MinBet > 0, $"{wallet} allows a zero bet.");
            Assert.True(info.MaxBet >= info.MinBet, $"{wallet} limits are inverted.");
            Assert.False(string.IsNullOrWhiteSpace(info.Symbol));
        }
    }

    [Fact]
    public void CurrencyAndValuablesAreSeparateSets()
    {
        var currency = WalletInfo.OfKind(WalletKind.Currency).Select(w => w.Wallet).ToList();
        var valuables = WalletInfo.OfKind(WalletKind.Valuable).Select(w => w.Wallet).ToList();

        Assert.Equal([Wallet.Roubles, Wallet.Dollars, Wallet.Euros], currency);
        Assert.Equal([Wallet.GpCoins, Wallet.Bitcoin, Wallet.LegaMedals], valuables);

        // Valuables are staked by the piece, so they all step by one.
        Assert.All(WalletInfo.OfKind(WalletKind.Valuable), w => Assert.Equal(1, w.Step));
        Assert.All(WalletInfo.OfKind(WalletKind.Valuable), w => Assert.Equal(1, w.MinBet));
    }

    [Fact]
    public void TemplateIdsAreDistinct()
    {
        // A copy-paste in the wallet table would silently make one currency pay out
        // in another, which the money tests could never catch.
        var tpls = WalletInfo.All.Select(w => w.Tpl.ToString()).ToList();

        Assert.Equal(tpls.Count, tpls.Distinct().Count());
    }

    [Theory]
    [InlineData(Wallet.Bitcoin, 11)]
    [InlineData(Wallet.LegaMedals, 6)]
    [InlineData(Wallet.GpCoins, 51)]
    [InlineData(Wallet.Dollars, 5001)]
    public async Task StakesAboveAWalletCeilingAreRefused(Wallet wallet, int wager)
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(wallet, 1_000_000);

        var response = await service.DealAsync(
            new DealRequest { Wager = wager, Wallet = wallet.ToString() },
            _session);

        Assert.False(response.Ok);
        Assert.Contains("bets run from", response.Error);
        Assert.Empty(_bank.Debits);
    }

    [Fact]
    public async Task ASingleBitcoinCanBeStakedEvenThoughRoublesStartAtAThousand()
    {
        // The engine's own limits are deliberately wide so the per-wallet ones govern.
        // A rouble minimum of 1,000 would otherwise make every bitcoin bet illegal.
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(Wallet.Bitcoin, 3);

        var response = await service.DealAsync(
            new DealRequest { Wager = 1, Wallet = nameof(Wallet.Bitcoin) },
            _session);

        Assert.True(response.Ok, response.Error);
        Assert.Equal([(Wallet.Bitcoin, 1)], _bank.Debits);
    }

    [Fact]
    public async Task AnOddValuableStakeRoundsTheNaturalUpRatherThanShortingThePlayer()
    {
        // One bitcoin at 3:2 settles on two and a half, which cannot exist. Rounding
        // down would quietly turn a natural into even money.
        var service = WithDeal("AS 9H KH 7D");
        _bank.SetBalance(Wallet.Bitcoin, 5);

        var response = await service.DealAsync(
            new DealRequest { Wager = 1, Wallet = nameof(Wallet.Bitcoin) },
            _session);

        Assert.True(response.Ok, response.Error);
        Assert.Equal([(Wallet.Bitcoin, 3)], _bank.Credits);

        // Which is exactly why the panel has to warn on odd valuable stakes.
        Assert.False(WalletInfo.For(Wallet.Bitcoin).SettlesExactly(1));
        Assert.True(WalletInfo.For(Wallet.Bitcoin).SettlesExactly(2));
    }

    [Fact]
    public async Task AnEvenValuableStakePaysExactlyThreeToTwo()
    {
        var service = WithDeal("AS 9H KH 7D");
        _bank.SetBalance(Wallet.Bitcoin, 5);

        var response = await service.DealAsync(
            new DealRequest { Wager = 2, Wallet = nameof(Wallet.Bitcoin) },
            _session);

        // 2 staked, 5 back: the stake plus 3 profit. No rounding involved.
        Assert.Equal([(Wallet.Bitcoin, 5)], _bank.Credits);
    }

    [Fact]
    public void CurrencyStakesNeverNeedTheWarning()
    {
        // Roubles round within a unit nobody can see, so an odd stake is not a problem.
        Assert.True(WalletInfo.For(Wallet.Roubles).SettlesExactly(10_001));
        Assert.True(WalletInfo.For(Wallet.Dollars).SettlesExactly(333));
    }
}
