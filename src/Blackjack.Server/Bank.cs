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
///
/// This is also the least proven code in the mod: every InventoryHelper call here
/// is one that has never run against a real profile. Hence the logging, and hence
/// the try/catch -- an exception escaping into the router would surface as an
/// opaque 500 with nothing to debug from.
/// </summary>
[Injectable]
public class Bank(
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    BlackjackLog log)
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
            log.Error($"GetBalance: no PMC profile for session '{sessionId}'.");
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
        if (pmcData is null)
        {
            log.Error($"TryDebit: no PMC profile for session '{sessionId}'.");
            return false;
        }

        if (amount <= 0)
        {
            return false;
        }

        var tpl = TplFor(wallet);
        var before = GetBalance(sessionId, wallet);
        if (before < amount)
        {
            log.Detail($"debit refused: wanted {amount:N0} {wallet}, player has {before:N0}.");
            return false;
        }

        var output = new ItemEventRouterResponse();
        var remaining = amount;

        // Smallest stacks first, so the stash ends up with fewer loose piles rather
        // than more.
        var stacks = StacksOf(pmcData, tpl).OrderBy(item => item.GetItemStackSize()).ToList();
        log.Detail($"debit {amount:N0} {wallet} across {stacks.Count} stack(s), balance {before:N0}");

        foreach (var stack in stacks)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, stack.GetItemStackSize());
            try
            {
                inventoryHelper.RemoveItemByCount(pmcData, stack.Id, take, sessionId, output);
            }
            catch (Exception ex)
            {
                // Partial removal may already have happened, so the player could be
                // short with no hand to show for it. Say so explicitly.
                log.Error(
                    $"RemoveItemByCount threw taking {take:N0} from stack {stack.Id}. "
                    + $"{amount - remaining:N0} of {amount:N0} {wallet} may already be gone.",
                    ex);
                return false;
            }

            remaining -= take;
        }

        var after = GetBalance(sessionId, wallet);
        log.Detail($"debit done: {wallet} {before:N0} -> {after:N0} (expected {before - amount:N0})");

        if (after != before - amount)
        {
            // The arithmetic disagreeing with the stash is the most valuable signal
            // there is: InventoryHelper did something other than what was asked, and
            // every balance the client shows is now suspect.
            log.Error($"debit mismatch: {wallet} is {after:N0} but should be {before - amount:N0}.");
        }

        return remaining == 0;
    }

    /// <summary>Pays winnings back into the stash, respecting max stack size.</summary>
    public void Credit(MongoId sessionId, Wallet wallet, int amount)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            log.Error($"Credit: no PMC profile for session '{sessionId}' -- {amount:N0} {wallet} not paid.");
            return;
        }

        if (amount <= 0)
        {
            return;
        }

        var tpl = TplFor(wallet);
        var before = GetBalance(sessionId, wallet);

        // Roubles cap at 500,000 per stack. One oversized stack would be rejected
        // by the client, so the payout is split before it is handed over.
        var maxStack = itemHelper.GetItem(tpl).Value?.Properties?.StackMaxSize ?? amount;
        var output = new ItemEventRouterResponse();
        var remaining = amount;
        var stacksMade = 0;

        log.Detail($"credit {amount:N0} {wallet} (max stack {maxStack:N0}), balance {before:N0}");

        while (remaining > 0)
        {
            var size = (int)Math.Min(remaining, maxStack);
            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { StackObjectsCount = size },
            };

            try
            {
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
            }
            catch (Exception ex)
            {
                // Losing a payout is the worst outcome in the mod, so this is loud and
                // says exactly how much never made it.
                log.Error($"AddItemToStash threw paying {size:N0} {wallet}. {remaining:N0} unpaid.", ex);
                return;
            }

            remaining -= size;
            stacksMade++;
        }

        var after = GetBalance(sessionId, wallet);
        log.Detail($"credit done: {wallet} {before:N0} -> {after:N0} in {stacksMade} stack(s)");

        if (after != before + amount)
        {
            // A full stash is the likely cause -- AddItemToStash can decline to place
            // an item without throwing.
            log.Error(
                $"credit mismatch: {wallet} is {after:N0} but should be {before + amount:N0}. "
                + "A full stash would explain this.");
        }
    }

    private static IEnumerable<Item> StacksOf(PmcData pmcData, MongoId tpl) =>
        pmcData.Inventory?.Items?.Where(item => item.Template == tpl) ?? [];
}
