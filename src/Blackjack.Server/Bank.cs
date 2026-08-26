using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace Blackjack.Server;

public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
}

/// <summary>
/// Moves currency in and out of the player's stash.
///
/// This deliberately does not use PaymentService. Both of its entry points derive
/// the currency from a trader -- GiveProfileMoney reads trader.Currency, and the
/// no-trader path in PayMoney is hardcoded to RUB -- so neither can settle a bet
/// denominated in dollars or euros. Walking the stacks directly is the only way
/// to support all three.
/// </summary>
[Injectable]
public class Bank(InventoryHelper inventoryHelper, ItemHelper itemHelper, ProfileHelper profileHelper)
    : IBank
{
    public static MongoId TplFor(Wallet wallet) => wallet switch
    {
        Wallet.Roubles => Money.ROUBLES,
        Wallet.Dollars => Money.DOLLARS,
        Wallet.Euros => Money.EUROS,
        _ => throw new ArgumentOutOfRangeException(nameof(wallet), wallet, "Unknown wallet."),
    };

    /// <summary>
    /// Total of every stack of this currency the profile holds. Counts money in
    /// containers as well as loose in the stash, which matches what the player
    /// would consider their balance.
    /// </summary>
    public int GetBalance(MongoId sessionId, Wallet wallet)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            return 0;
        }

        return StacksOf(pmcData, TplFor(wallet)).Sum(item => item.GetItemStackSize());
    }

    /// <summary>
    /// Takes the stake. Returns false without touching anything if the player is
    /// short -- the caller must not deal a hand it cannot collect on.
    /// </summary>
    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null || amount <= 0 || GetBalance(sessionId, wallet) < amount)
        {
            return false;
        }

        var output = new ItemEventRouterResponse();

        var tpl = TplFor(wallet);
        var remaining = amount;

        // Smallest stacks first, so the stash ends up with fewer loose piles rather
        // than more.
        foreach (var stack in StacksOf(pmcData, tpl).OrderBy(item => item.GetItemStackSize()))
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, stack.GetItemStackSize());
            inventoryHelper.RemoveItemByCount(pmcData, stack.Id, take, sessionId, output);
            remaining -= take;
        }

        return remaining == 0;
    }

    /// <summary>Pays winnings back into the stash, respecting max stack size.</summary>
    public void Credit(MongoId sessionId, Wallet wallet, int amount)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null || amount <= 0)
        {
            return;
        }

        var output = new ItemEventRouterResponse();

        var tpl = TplFor(wallet);

        // Roubles cap at 500,000 per stack. One oversized stack would be rejected
        // by the client, so the payout is split before it is handed over.
        var maxStack = itemHelper.GetItem(tpl).Value?.Properties?.StackMaxSize ?? amount;
        var remaining = amount;

        while (remaining > 0)
        {
            var size = (int)Math.Min(remaining, maxStack);
            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { StackObjectsCount = size },
            };

            inventoryHelper.AddItemToStash(
                sessionId,
                new AddItemDirectRequest
                {
                    ItemWithModsToAdd = [item],
                    FoundInRaid = false,
                    UseSortingTable = true,
                },
                pmcData,
                output);

            remaining -= size;
        }
    }

    private static IEnumerable<Item> StacksOf(PmcData pmcData, MongoId tpl) =>
        pmcData.Inventory?.Items?.Where(item => item.Template == tpl) ?? [];
}
