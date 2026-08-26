using Blackjack.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>
/// HTTP adapter. Serialises what <see cref="BlackjackService"/> decided and
/// surfaces warnings to the server console. Deliberately holds no game logic --
/// everything worth testing lives one layer down, where it is reachable without
/// a running server.
/// </summary>
[Injectable]
public class BlackjackCallbacks(
    HttpResponseUtil httpResponseUtil,
    BlackjackService service,
    BlackjackLog log)
{
    public async ValueTask<string> Deal(DealRequest info, MongoId sessionId)
    {
        Received("deal", sessionId, $"{info.Wager} {info.Wallet}");
        return Respond(await service.DealAsync(info, sessionId));
    }

    public async ValueTask<string> Act(ActionRequest info, MongoId sessionId)
    {
        Received("action", sessionId, info.Action);
        return Respond(await service.ActAsync(info, sessionId));
    }

    public ValueTask<string> State(StateRequest info, MongoId sessionId)
    {
        Received("state", sessionId, null);
        return new ValueTask<string>(Respond(service.State(sessionId)));
    }

    public ValueTask<string> Stats(StatsRequest info, MongoId sessionId)
    {
        Received("stats", sessionId, null);
        return new ValueTask<string>(httpResponseUtil.NoBody(service.Stats(sessionId)));
    }

    public ValueTask<string> Ping(PingRequest info, MongoId sessionId)
    {
        var response = service.Ping(sessionId);

        // Always logged, never gated on verbose: this is the line that tells you
        // whether the mod is reachable and whether the session resolved at all.
        log.Info(
            $"ping from session '{response.SessionId}' -- profile {(response.HasProfile ? "found" : "NOT FOUND")}"
            + (response.HasProfile ? $", {string.Join(", ", response.Balances.Select(b => $"{b.Key} {b.Value:N0}"))}" : ""));

        if (!response.HasProfile)
        {
            log.Error("no profile for that session. If the id above is blank, the session cookie did not resolve.");
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    private void Received(string route, MongoId sessionId, string? detail) =>
        log.Detail($"-> {route} [{sessionId}]{(detail is null ? "" : $" {detail}")}");

    private string Respond(BlackjackResponse response)
    {
        // A warning means the round went through but money did not, so it is an error
        // regardless of how quiet the log is set to be.
        if (response.Warning is not null)
        {
            log.Error(response.Warning);
        }

        // Always written, not gated on verbose: a stake reappearing needs a reason
        // beside it or it reads as a payout bug.
        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        if (!response.Ok)
        {
            log.Detail($"<- refused: {response.Error}");
        }
        else if (response.Round is not null)
        {
            var round = response.Round;
            var hands = string.Join(
                " | ",
                round.PlayerHands.Select(h => $"{string.Join(" ", h.Cards)} ({h.Value}){(h.Outcome == HandOutcome.Pending ? "" : $" {h.Outcome}")}"));

            log.Detail(
                $"<- {round.Phase} dealer [{string.Join(" ", round.Dealer.Cards)}] ({round.Dealer.Value}) "
                + $"you [{hands}] staked {round.TotalWagered:N0} returned {round.TotalReturned:N0} "
                + $"balance {response.Balance:N0} {response.Wallet}");
        }

        return httpResponseUtil.NoBody(response);
    }
}
