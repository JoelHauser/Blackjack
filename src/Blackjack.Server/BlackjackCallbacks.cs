using Blackjack.Game;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>
/// The transport layer. Owns no game rules -- it validates that the player can
/// afford what they are asking for, hands the intent to <see cref="BlackjackTable"/>,
/// and moves money to match whatever the table decided.
/// </summary>
[Injectable]
public class BlackjackCallbacks(
    ISptLogger<BlackjackCallbacks> logger,
    HttpResponseUtil httpResponseUtil,
    ProfileHelper profileHelper,
    SaveServer saveServer,
    TableStore tables,
    Bank bank)
{
    public async ValueTask<string> Deal(DealRequest info, MongoId sessionId)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            return Respond(BlackjackResponse.Failed("No PMC profile for this session."));
        }

        if (!Enum.TryParse<Wallet>(info.Wallet, ignoreCase: true, out var wallet))
        {
            return Respond(BlackjackResponse.Failed($"Unknown currency '{info.Wallet}'."));
        }

        var session = tables.For(sessionId);
        var rules = session.Table.Rules;

        if (session.Table.Phase == RoundPhase.PlayerTurn)
        {
            return Respond(BlackjackResponse.Failed("A round is already in progress."));
        }

        // Validate the stake before taking it. Letting Deal throw after the debit
        // would pocket the money and leave no hand to win it back with.
        if (info.Wager < rules.MinBet || info.Wager > rules.MaxBet)
        {
            return Respond(
                BlackjackResponse.Failed($"Wager must be between {rules.MinBet} and {rules.MaxBet}."));
        }

        var output = new ItemEventRouterResponse();

        if (!bank.TryDebit(sessionId, pmcData, wallet, info.Wager, output))
        {
            return Respond(
                BlackjackResponse.Failed($"Not enough {wallet} -- you have {bank.GetBalance(pmcData, wallet)}."));
        }

        session.Wallet = wallet;
        var view = session.Table.Deal(info.Wager);
        session.Staked = view.TotalWagered;

        SettleIfFinished(session, view, sessionId, pmcData, output);
        await saveServer.SaveProfileAsync(sessionId);

        return Respond(Success(view, pmcData, session));
    }

    public async ValueTask<string> Act(ActionRequest info, MongoId sessionId)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            return Respond(BlackjackResponse.Failed("No PMC profile for this session."));
        }

        if (!Enum.TryParse<PlayerAction>(info.Action, ignoreCase: true, out var action))
        {
            return Respond(BlackjackResponse.Failed($"Unknown action '{info.Action}'."));
        }

        var session = tables.For(sessionId);
        if (session.Table.Phase != RoundPhase.PlayerTurn)
        {
            return Respond(BlackjackResponse.Failed("No round is in progress."));
        }

        var before = session.Table.View();
        var output = new ItemEventRouterResponse();

        // Doubling and splitting raise the stake. Check affordability *before* the
        // engine acts -- once the hand has changed there is no way to un-split it
        // if the debit then fails.
        if (action is PlayerAction.Double or PlayerAction.Split)
        {
            var extra = before.PlayerHands[before.ActiveHandIndex].Wager;
            if (bank.GetBalance(pmcData, session.Wallet) < extra)
            {
                return Respond(BlackjackResponse.Failed($"Not enough {session.Wallet} to {action}."));
            }
        }

        RoundView view;
        try
        {
            view = action switch
            {
                PlayerAction.Hit => session.Table.Hit(),
                PlayerAction.Stand => session.Table.Stand(),
                PlayerAction.Double => session.Table.Double(),
                PlayerAction.Split => session.Table.Split(),
                _ => throw new InvalidOperationException($"Unhandled action {action}."),
            };
        }
        catch (InvalidOperationException ex)
        {
            // The engine is the authority on legality; an illegal request means the
            // client's view drifted, so hand it the real one back.
            return Respond(
                new BlackjackResponse
                {
                    Ok = false,
                    Error = ex.Message,
                    Round = before,
                    Balance = bank.GetBalance(pmcData, session.Wallet),
                    Wallet = session.Wallet.ToString(),
                });
        }

        var owed = view.TotalWagered - session.Staked;
        if (owed > 0 && !bank.TryDebit(sessionId, pmcData, session.Wallet, owed, output))
        {
            // Pre-checked above, so reaching here means the profile changed underneath
            // us. Log loudly: the player is now playing a stake they did not pay.
            logger.Error($"Blackjack: failed to collect {owed} {session.Wallet} from {sessionId} after {action}.");
        }

        session.Staked = view.TotalWagered;

        SettleIfFinished(session, view, sessionId, pmcData, output);
        await saveServer.SaveProfileAsync(sessionId);

        return Respond(Success(view, pmcData, session));
    }

    public ValueTask<string> State(StateRequest info, MongoId sessionId)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            return new ValueTask<string>(Respond(BlackjackResponse.Failed("No PMC profile for this session.")));
        }

        var session = tables.For(sessionId);
        return new ValueTask<string>(Respond(Success(session.Table.View(), pmcData, session)));
    }

    private void SettleIfFinished(
        PlayerSession session,
        RoundView view,
        MongoId sessionId,
        PmcData pmcData,
        ItemEventRouterResponse output)
    {
        if (view.Phase != RoundPhase.Settled)
        {
            return;
        }

        if (view.TotalReturned > 0)
        {
            bank.Credit(sessionId, pmcData, session.Wallet, view.TotalReturned, output);
        }

        session.Staked = 0;
    }

    private BlackjackResponse Success(RoundView view, PmcData pmcData, PlayerSession session) => new()
    {
        Ok = true,
        Round = view,
        Balance = bank.GetBalance(pmcData, session.Wallet),
        Wallet = session.Wallet.ToString(),
    };

    private string Respond(BlackjackResponse response) => httpResponseUtil.NoBody(response);
}
