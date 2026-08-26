using Blackjack.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server;

/// <summary>
/// The whole server-side game flow: validate what the player asked for, let the
/// table decide, then move money to match.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and is tested -- with no SPT server
/// present. HTTP and logging live in <see cref="BlackjackCallbacks"/>.
/// </summary>
[Injectable]
public class BlackjackService(IBank bank, IProfileGateway profiles, TableStore tables, IStatsStore stats)
{
    public async Task<BlackjackResponse> DealAsync(DealRequest request, MongoId sessionId)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet))
        {
            return BlackjackResponse.Failed($"Unknown currency '{request.Wallet}'.");
        }

        var session = tables.For(sessionId);
        var rules = session.Table.Rules;

        if (session.Table.Phase == RoundPhase.PlayerTurn)
        {
            return BlackjackResponse.Failed("A round is already in progress.");
        }

        // Validate the stake before taking it. Letting Deal throw after the debit
        // would pocket the money and leave no hand to win it back with.
        if (request.Wager < rules.MinBet || request.Wager > rules.MaxBet)
        {
            return BlackjackResponse.Failed($"Wager must be between {rules.MinBet} and {rules.MaxBet}.");
        }

        if (!bank.TryDebit(sessionId, wallet, request.Wager))
        {
            return BlackjackResponse.Failed(
                $"Not enough {wallet} -- you have {bank.GetBalance(sessionId, wallet)}.");
        }

        session.Wallet = wallet;
        var view = session.Table.Deal(request.Wager);
        session.Staked = view.TotalWagered;

        Settle(session, view, sessionId);
        await profiles.SaveAsync(sessionId);

        return Success(view, sessionId, session);
    }

    public async Task<BlackjackResponse> ActAsync(ActionRequest request, MongoId sessionId)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        if (!Enum.TryParse<PlayerAction>(request.Action, ignoreCase: true, out var action))
        {
            return BlackjackResponse.Failed($"Unknown action '{request.Action}'.");
        }

        var session = tables.For(sessionId);
        if (session.Table.Phase != RoundPhase.PlayerTurn)
        {
            return BlackjackResponse.Failed("No round is in progress.");
        }

        var before = session.Table.View();

        // Doubling and splitting raise the stake. Check affordability *before* the
        // engine acts -- once the hand has changed there is no way to un-split it
        // if the debit then fails.
        if (action is PlayerAction.Double or PlayerAction.Split)
        {
            var extra = before.PlayerHands[before.ActiveHandIndex].Wager;
            if (bank.GetBalance(sessionId, session.Wallet) < extra)
            {
                return Refused($"Not enough {session.Wallet} to {action}.", before, sessionId, session);
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
            // The engine is the authority on legality. An illegal request means the
            // client's view drifted, so hand it the real one back.
            return Refused(ex.Message, before, sessionId, session);
        }

        string? warning = null;
        var owed = view.TotalWagered - session.Staked;
        if (owed > 0 && !bank.TryDebit(sessionId, session.Wallet, owed))
        {
            // Pre-checked above, so reaching here means the profile changed
            // underneath us. The adapter logs it: the player is now playing a
            // stake they did not pay.
            warning = $"Failed to collect {owed} {session.Wallet} after {action}.";
        }

        session.Staked = view.TotalWagered;

        Settle(session, view, sessionId);
        await profiles.SaveAsync(sessionId);

        return Success(view, sessionId, session) with { Warning = warning };
    }

    public PlayerStats Stats(MongoId sessionId) => stats.Get(sessionId);

    public BlackjackResponse State(MongoId sessionId)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        var session = tables.For(sessionId);
        return Success(session.Table.View(), sessionId, session);
    }

    private void Settle(PlayerSession session, RoundView view, MongoId sessionId)
    {
        if (view.Phase != RoundPhase.Settled)
        {
            return;
        }

        if (view.TotalReturned > 0)
        {
            bank.Credit(sessionId, session.Wallet, view.TotalReturned);
        }

        var record = stats.Get(sessionId);
        record.Record(view, session.Wallet, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        stats.Save(sessionId, record);

        session.Staked = 0;
    }

    private BlackjackResponse Success(RoundView view, MongoId sessionId, PlayerSession session) => new()
    {
        Ok = true,
        Round = view,
        Balance = bank.GetBalance(sessionId, session.Wallet),
        Wallet = session.Wallet.ToString(),
    };

    private BlackjackResponse Refused(
        string error,
        RoundView view,
        MongoId sessionId,
        PlayerSession session) => new()
    {
        Ok = false,
        Error = error,
        Round = view,
        Balance = bank.GetBalance(sessionId, session.Wallet),
        Wallet = session.Wallet.ToString(),
    };
}
