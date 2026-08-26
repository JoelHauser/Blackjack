using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server;

/// <summary>
/// Adapts the item-event transport onto the same <see cref="BlackjackService"/> the
/// static routes use, so there is one implementation of the game and two ways in.
/// </summary>
[Injectable]
public class BlackjackItemEventCallbacks(BlackjackService service, BlackjackLog log)
{
    /// <summary>
    /// Key the round is attached under. An item-event reply carries ProfileChanges and
    /// nothing else, so the round rides along in the response's extension data rather
    /// than costing the client a second request for it.
    /// </summary>
    private const string RoundKey = "blackjack";

    public async ValueTask<ItemEventRouterResponse> Deal(
        BlackjackDealAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        log.Detail($"-> event deal [{sessionId}] {body.Wager} {body.Wallet}");

        var result = await service.DealAsync(
            new DealRequest { Wallet = body.Wallet, Wager = body.Wager },
            sessionId,
            output);

        return Attach(output, result);
    }

    public async ValueTask<ItemEventRouterResponse> Play(
        BlackjackPlayAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        log.Detail($"-> event play [{sessionId}] {body.Move}");

        var result = await service.ActAsync(
            new ActionRequest { Action = body.Move },
            sessionId,
            output);

        return Attach(output, result);
    }

    private ItemEventRouterResponse Attach(ItemEventRouterResponse output, BlackjackResponse result)
    {
        if (result.Warning is not null)
        {
            log.Error(result.Warning);
        }

        if (result.Note is not null)
        {
            log.Info(result.Note);
        }

        if (!result.Ok)
        {
            log.Detail($"<- refused: {result.Error}");
        }

        output.ExtensionData ??= [];
        output.ExtensionData[RoundKey] = result;

        return output;
    }
}
