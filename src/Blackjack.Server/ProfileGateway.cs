using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace Blackjack.Server;

/// <summary>
/// Thin wrapper over the two SPT services the game flow needs. Its only job is to
/// be an interface, so <see cref="BlackjackService"/> can be tested without one.
/// </summary>
[Injectable]
public class ProfileGateway(ProfileHelper profileHelper, SaveServer saveServer) : IProfileGateway
{
    public bool HasProfile(MongoId sessionId) => profileHelper.GetPmcProfile(sessionId) is not null;

    public async Task SaveAsync(MongoId sessionId) => await saveServer.SaveProfileAsync(sessionId);
}
