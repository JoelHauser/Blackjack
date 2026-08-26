using SPTarkov.Common.Models.Logging;
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
    ISptLogger<BlackjackCallbacks> logger,
    HttpResponseUtil httpResponseUtil,
    BlackjackService service)
{
    public async ValueTask<string> Deal(DealRequest info, MongoId sessionId) =>
        Respond(await service.DealAsync(info, sessionId));

    public async ValueTask<string> Act(ActionRequest info, MongoId sessionId) =>
        Respond(await service.ActAsync(info, sessionId));

    public ValueTask<string> State(StateRequest info, MongoId sessionId) =>
        new(Respond(service.State(sessionId)));

    private string Respond(BlackjackResponse response)
    {
        if (response.Warning is not null)
        {
            logger.Error($"Blackjack: {response.Warning}");
        }

        return httpResponseUtil.NoBody(response);
    }
}
