# Blackjack -- working notes for Claude

A card table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin still to be written. Players stake roubles, dollars,
euros, GP coins, bitcoin or Lega medals.

This file is loaded automatically at the start of every session. Keep it to things
a fresh session would otherwise rediscover the hard way -- not a chronological
diary, which would grow without bound. **Update "Current state" when you finish a
piece of work.**

---

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing.

## Layout

| Project | Owns |
| --- | --- |
| `src/Blackjack.Game` | Rules engine. No SPT reference, no I/O, no clock. |
| `src/Blackjack.Server` | The mod: routes, DI, currency, stats, escrow, logging. |
| `tests/Blackjack.Game.Tests` | 52 tests over the engine. |
| `tests/Blackjack.Server.Tests` | 51 tests over the money flow, on fakes. |
| `tools/Blackjack.Console` | Terminal table. Plays the engine with no SPT install. |
| `scripts/smoke.ps1` | Drives a real server over HTTP. Needs SPT; untested. |

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a `Wallet` to an item template lives in `Wallets.cs` and
`Bank.cs`. Keep it that way; it is what makes the rules testable.

## There is no SPT install on this machine

Nothing in this repo has ever run against a real server. Every claim about SPT
behaviour is either read from the source or unverified -- say which.

**To inspect SPT's API, reflect over the real assembly.** .NET 10 file-based apps
make this a one-liner, and it beats guessing at namespaces:

```csharp
// probe.cs, run with: dotnet run probe.cs
#:package SPTarkov.Server.Core@4.1.2
var asm = typeof(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile).Assembly;
var t = asm.GetTypes().First(x => x.Name == "MailSendService");
foreach (var m in t.GetMethods()) Console.WriteLine(m);
```

Source lives at `github.com/sp-tarkov/server-csharp` under
`Libraries/SPTarkov.Server.Core/`. Item templates are **not** there -- they ship
with the install -- so stack sizes and item properties cannot be checked here.

NuGet tops out at **4.1.2**; there is no 4.1.3 package. The libraries lag the game.

## Things that will bite you

Each of these cost real time. None are hypothetical.

- **`PaymentService` cannot settle a bet.** Both entry points derive currency from
  a trader -- `GiveProfileMoney` reads `trader.Currency`, and `PayMoney`'s
  no-trader path is hardcoded to roubles. `Bank` walks item stacks directly.
- **`AddItemToStash` can decline an item without throwing.** A full stash silently
  swallows a payout. `Bank.Credit` compares the balance either side of every move
  against what it intended and posts the shortfall as mail rather than losing it.
- **An item-event reply carries `ProfileChanges` and nothing else.** The round
  rides in the response's `ExtensionData` under `blackjack`, or the client would
  need a second request for it.
- **A custom static route does not update the client's inventory.** Money lands in
  the profile but the stash view stays stale until reload, which reads to a player
  as the mod eating their winnings. That is why the client uses item-event actions.
- **The table is in memory and the stake is not.** A stake is debited and saved the
  moment a hand is dealt, so a crash mid-round used to take the money and leave no
  hand. `EscrowStore` records every stake until settlement and refunds orphans.
- **`/blackjack/state` is called before any hand exists.** `DealerView` used to
  read `_dealer.Cards[0]` unconditionally and threw on a fresh table -- every visit
  to the panel would have failed. An empty dealer hand must describe itself.
- **Naming a property `Path` shadows `System.IO.Path`** inside the same class and
  breaks every `Path.Combine`. `StatsStore.FilePath` is named that for this reason.
- **`OnLoadOrder` has no `PostDBModLoader`.** The values are `Watermark`, `Preload`,
  `GameCallbacks`, `TraderRegistration`, `Routers`, `HandbookCallbacks`,
  `SaveCallbacks`, `TraderCallbacks`, `PresetCallbacks`, `RagfairCallbacks`,
  `PostLoad`.
- **SPT's DI registers a class against every non-System interface it implements**
  (`DependencyInjectionHandler.InjectAll`), so `Bank : IBank` resolves for free.
  That is what makes the interface seams cost nothing.
- **Bash heredocs mangle backslashes.** Writing C# with `'\\'` through
  `cat <<'EOF'` produces broken escapes. Use the Write tool for those files.
- **`Compress-Archive` writes backslash zip entries**, which extract as one literal
  filename on Linux. Pack releases with `System.IO.Compression` instead.

## Architecture

Server-authoritative. The client renders what it is handed and sends intents; it
never sees the hole card, never draws, never decides an outcome.

```
BlackjackService          the whole game flow, on IBank / IProfileGateway /
                          IStatsStore / IEscrowStore. No SPT types but MongoId,
                          so it is testable without a server.
BlackjackCallbacks        static routes  -- curl testing, throwaway change record
BlackjackItemEventCallbacks  item events -- the game client, real change record
Bank / ProfileGateway     the only classes that touch SPT services
```

The interface seams exist because `InventoryHelper`, `ProfileHelper` and
`SaveServer` are concrete classes with non-virtual methods. Depending on them
directly makes the calling code untestable, server or not.

Two transports, one service. Do not put game logic in either adapter.

## Decisions already made

- **Rest Space interactable, not a new hideout area.** `HideoutAreas` ends at
  `CircleOfCultists = 27` and the client has a matching enum plus a baked prefab
  per area. A new value has no model and the client does not know it exists.
- **The panel floats over a dimmed hideout**, not a fullscreen takeover. This makes
  freeing the cursor and swallowing player input a hard requirement. Fallback if
  that proves impractical: takeover, which is how EFT presents its own area screens.
- **No hotkey.** The table is the only way in, so the game cannot be reached in a
  raid -- the Rest Space does not exist on a raid map.
- **Valuables are staked through EFT's own grid component**, dragged into a
  container. One item type per bet: a mixed stake has no coherent payout.
- **Per-hand settlement, straight to the stash.** No session, no chips, no buy-in.
  Mail only when the stash cannot take the winnings.
- **Naturals pay 3:2 in currency, even money in valuables.** One bitcoin at 3:2
  settles on half a coin, which cannot exist. The rate is a per-round argument to
  `Deal`, because one shoe serves every currency.
- Design mockups: [panel states](https://claude.ai/code/artifact/99573205-77e3-4c7e-860d-d4a10e713fb3),
  [opening the table](https://claude.ai/code/artifact/f5f210b0-1748-4b56-a766-da4f4fcf0ad6).

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code
  prevents. The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable that a player might argue about lives in `Rules` or `WalletInfo`.

## Verifying

There is no game here, so:

```
dotnet test                                    # 103 tests, no SPT needed
dotnet run --project tools/Blackjack.Console   # play a hand in the terminal
```

**Distrust a suite that passes first time.** The money tests were mutation-checked:
collecting the full stake instead of the increase, and paying out on losing hands,
each fail 7 tests. Do that again after changing anything that moves currency.

`MoneyInvariantTests` plays 400 random rounds and checks, after each, that the money
moved equals the profit the engine reported. An end-of-session balance check would
miss errors that cancel.

On a machine with SPT, `scripts\smoke.ps1 -SessionId <id> -PingOnly` first. It
touches no money and proves the mod loaded, the route is reachable, the session
resolved and the profile can be read.

## Releasing

`releases/Blackjack-<ver>.zip`, laid out as `user/mods/Blackjack/` so it extracts
into an SPT install. The version lives in **two** places and they must agree:
`Blackjack.Server.csproj` `<Version>` and `ModMetadata.Version`.

SPT's own assemblies are not bundled -- the server provides them. Symbols ship for
now, deliberately, because nothing has run for real yet.

---

## Current state

**Update this section as work completes.**

- Working branch **`test`**; `main` is behind and needs a merge before release.
- Server mod is feature-complete: rules, six wallets, money, stats, escrow, logging,
  both transports. 103 tests green.
- **Nothing has run against a real SPT server.** `Bank`'s `InventoryHelper` calls in
  particular have never touched a profile.
- `releases/Blackjack-0.1.0.zip` is built and committed.

### Open items

- **The client plugin does not exist.** It needs `Assembly-CSharp.dll` and the SPT
  client DLLs from a game install, so it cannot be started here.
- **`smoke.ps1` is unverified**, including whether the PHPSESSID cookie resolves a
  session. A blank `sessionId` from `/blackjack/ping` is the direct answer.
- **Mail attachments are unverified** -- SPT may expect `ParentId`/`SlotId` set on
  them in ways not checked here.
- **`ExtensionData` serialisation is unverified**, as is whether the client accepts
  an unfamiliar action name on the item-event endpoint.
- **The panel mockup is stale**: it still shows an odd-stake warning from a payout
  rule that was abandoned.
- Undecided: whether a settled round reads in the strip above the buttons or as an
  overlay across the felt.
