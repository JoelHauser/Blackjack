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

## Whether there is an SPT install depends on the machine

This repo is worked on from more than one machine. Check before assuming:

| Machine | Installs |
| --- | --- |
| The one this file was written on | none |
| Joel's Windows box | `H:\SPT4.1.X` (4.1.3) and `H:\SPT2026` (4.0.13) |

**With an install present**, three things the rest of this file calls unverifiable
become checkable, and all three have now been done -- see "What the install
settled" below. Item templates live at
`SPT_Runtime/SPT_Data/database/templates/items.json`, the server assemblies at
`SPT_Runtime/SPTarkov.*.dll`, and `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`
is what the client plugin needs.

**Reflecting over the installed assemblies beats reflecting over the NuGet
package**, because the package lags: NuGet tops out at 4.1.2 and the install is
4.1.3. Mono.Cecil ships with the game at `BepInEx/core/Mono.Cecil.dll` and reads
them without loading them.

**Without an install**, .NET 10 file-based apps make the NuGet package a
one-liner:

```csharp
// probe.cs, run with: dotnet run probe.cs
#:package SPTarkov.Server.Core@4.1.2
var asm = typeof(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile).Assembly;
var t = asm.GetTypes().First(x => x.Name == "MailSendService");
foreach (var m in t.GetMethods()) Console.WriteLine(m);
```

Source lives at `github.com/sp-tarkov/server-csharp` under
`Libraries/SPTarkov.Server.Core/`.

## What the install settled

Done on Joel's box against 4.1.3, by reading the shipped assemblies and database.
None of it required running the server.

- **Building against NuGet 4.1.2 is safe on a 4.1.3 install.** Every SPT symbol the
  compiled mod names -- 36 types and 63 members -- resolves against the installed
  `SPTarkov.*` assemblies. Nothing the mod touches moved between the two.
- **The 4.1.3 namespaces**, which are not what the older docs say:
  `Helpers.Profile.InventoryHelper`, `Helpers.Profile.ProfileHelper`,
  `Helpers.Items.ItemHelper`, `Services.Commerce.MailSendService`,
  `Servers.SaveServer`, `Common.Models.Logging.ISptLogger<T>`.
- **Signatures on the money path are as assumed**:
  `AddItemToStash(MongoId, AddItemDirectRequest, PmcData, ItemEventRouterResponse)`
  returning void, and `GetPmcProfile(MongoId)` returning `PmcData`.
- **All six wallet templates exist**, with these real stack limits:

  | Wallet | Template | StackMaxSize |
  | --- | --- | --- |
  | Roubles | `5449016a4bdc2d6f028b456f` | 1,000,000 |
  | Dollars | `5696686a4bdc2da3298b456a` | 50,000 |
  | Euros | `569668774bdc2da2298b4568` | 50,000 |
  | GP coins | `5d235b4d86f7742e017bc88a` | 100 |
  | Bitcoin | `59faff1d86f7746c51718c9c` | **1** |
  | Lega medal | `6656560053eaaa7a23349c86` | **1** |

  **Bitcoin and Lega medals do not stack.** A maximum bitcoin win is 20 separate
  items needing 20 free grid cells, which is the likeliest way a payout runs out of
  room -- and the reason `Bank.Credit`'s shortfall-to-mail path matters more than it
  looked. `Bank` reads these from the database rather than assuming them, so an item
  mod that changes a limit is handled.

  A comment in `Bank.cs` claimed roubles cap at 500,000. They cap at 1,000,000.

## Things that will bite you

Each of these cost real time. None are hypothetical.

- **`new ItemEventRouterResponse()` is not a usable response.** Its constructor
  initialises nothing, and `RemoveItemByCount` reaches into
  `output.ProfileChanges[sessionId]`, so a hand-built one throws
  NullReferenceException -- *after* the items are already gone. That failure reported
  itself as "not enough roubles" while the stake had left the stash. Get one from
  `EventOutputHolder.GetOutput(sessionId)`. The static routes cannot return it to the
  client, so the stash view stays stale; being unread is fine, being uninitialised is
  not.
- **A mod can change any item's stack limit.** Roubles cap at 1,000,000 in the base
  database and at 20,000,000 on a server running BarterItemsStacks. `Bank` reads
  `StackMaxSize` live for this reason; assuming the database value would be wrong on
  a real install.
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

## Talking to the server without a game client

Three things about SPT's HTTP layer, each of which cost a round trip to discover
because the error named none of them. All read out of 4.1.3 and then confirmed
against a running server.

- **It serves HTTPS, not HTTP**, on the same port, with a self-signed certificate
  it generates into `user\certs\`. .NET rejects that by default and reports "the
  underlying connection was closed", which reads as the server being down.
- **Every request body is zlib-inflated and every response deflated**, because that
  is what the EFT client speaks. Two headers opt out, and
  `SptHttpListener.HandleAsync` / `IsDebugRequest` are where they are read:

  | Header | Effect |
  | --- | --- |
  | `requestcompressed: 0` | read my body as plain UTF-8 |
  | `responsecompressed: 0` | reply in plain JSON |

  Without them a plain-JSON body dies inside `Inflater` with "the archive entry was
  compressed using an unsupported compression method".
- **Request bodies are matched case-sensitively.** `{"wager": 10000}` binds nothing
  against `public int Wager`; every property silently takes its default. That made a
  10,000 bet arrive as 0 and come back "bets run from 1,000 to 500,000", while
  `Wallet` -- which has a default of Roubles -- looked like it had bound correctly.
  Send PascalCase.
- **Enums go over the wire as integers, not names.** `phase` is `1`, not
  `"PlayerTurn"`. Comparing against the name never matches and never errors, which
  left a dealt hand sitting in PlayerTurn with the stake in escrow while the caller
  reported success. `RoundPhase` is AwaitingBet/PlayerTurn/DealerTurn/Settled,
  `HandOutcome` is Pending/Win/Lose/Push/Blackjack/Bust, `PlayerAction` is
  Hit/Stand/Double/Split, all zero-based. Worth making these strings before the
  client plugin is written, so it does not hardcode magic numbers.
- **The session id is a `PHPSESSID` cookie**, read with
  `Request.Cookies.TryGetValue` in `HttpServer.HandleRequestAsync`. In PowerShell it
  cannot be passed through `-Headers`: `Cookie` is a restricted header and it is
  dropped **silently**, so the request arrives with no session and the server says
  "session id provided was empty, did you restart the server while the game was
  running?". Use a `WebRequestSession`. `scripts\smoke.ps1` does.

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

- Working branch **`test`**; `main` is behind by three commits, a clean
  fast-forward, and needs a merge before release.
- Server mod is feature-complete: rules, six wallets, money, stats, escrow, logging,
  both transports. 103 tests green, re-run on Joel's box.
- **Statically verified against a real 4.1.3 install** -- every API the mod calls
  exists with the signature it expects, and every wallet template exists with the
  stack limit the code reads. See "What the install settled".
- **It loads and answers.** On a real 4.1.3 server the mod appears in the mod list,
  writes its data file, registers its routes, and `smoke.ps1 -PingOnly` resolves the
  session and reads all six balances back. That is the first time any of this has
  run outside a test double.
- **Money moves correctly.** Hands have been dealt, played and settled against a real
  profile in both directions. A win credited 20,000 against a 10,000 stake and a loss
  took 25,000, each landing on the exact expected balance, with escrow empty and stats
  written afterwards. `Bank.Debit`, `Bank.Credit`, escrow and settlement have all now
  run for real.
- **Untested still:** valuables (bitcoin and Lega are at zero in the test profile),
  the full-stash shortfall-to-mail path, a restart mid-round, split and double, and
  the item-event transport -- everything so far went through the static routes.
- `releases/Blackjack-0.1.0.zip` is built and committed.

### Testing on Joel's box

Profile `6a8cd3a7e0b8272790f41285` ("test", level 69) is the sandbox -- roughly
499M roubles, 500M dollars, 500M euros, 5,000 GP coins. The other profile,
`6a7501c247d2e12a3892aaee` ("SCOOP", level 16), is the real one; leave it alone.

**Bitcoin and Lega medals are both at zero there**, so the two wallets with a
`StackMaxSize` of 1 -- the riskiest payout path, one item per coin -- cannot be
exercised by betting until some are added.

### Open items

- **The client plugin does not exist.** No longer blocked: on Joel's box
  `H:\SPT4.1.X` has `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` and the SPT
  client DLLs under `BepInEx/plugins/spt/`. This is the largest remaining piece and
  the only thing standing between the mod and a first real test.
  Note 4.1.3's `PluginValidator` reads a plugin's references to `spt-*` and requires
  a major.minor match, so the plugin must be built against this install, not an
  older one.
- **`smoke.ps1` works** against a real server, as of the first run on Joel's box.
  The PHPSESSID assumption was right; three things around it were wrong. See
  "Talking to the server without a game client".
- **Make the wire enums strings** before the client plugin is written. See the
  integers note above.
- **Mail attachments are unverified** -- SPT may expect `ParentId`/`SlotId` set on
  them in ways not checked here.
- **`ExtensionData` serialisation is unverified**, as is whether the client accepts
  an unfamiliar action name on the item-event endpoint.
- **The panel mockup is stale**: it still shows an odd-stake warning from a payout
  rule that was abandoned.
- Undecided: whether a settled round reads in the strip above the buttons or as an
  overlay across the felt.
