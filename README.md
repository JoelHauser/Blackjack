# Blackjack

A blackjack table for the SPT hideout. Wager roubles, dollars or euros against a
server-dealt shoe.

**Status:** engine complete and tested; server transport and client UI in progress.

---

## Architecture

The server is authoritative for everything that matters. The client renders a
state it is handed and sends intents back -- it never sees the hole card, never
draws a card, and never decides an outcome.

```
client (BepInEx plugin)                 server mod (.NET 10)
  hideout interaction   ──POST──►  /blackjack/deal    { wallet, wager }
                        ◄────────  RoundView + ItemEventRouterResponse
  hit / stand / double  ──POST──►  /blackjack/action  { action }
  / split               ◄────────  RoundView, settlement, money delta
```

This is not anti-cheat theatre. Money mutation has to happen server-side because
that is where the profile lives and saves, so the deck belongs next to it. The
consequence worth knowing: **the client cannot animate a deal it has not been
told about**, so the UI must be built around awaiting a response, not around
locally dealing and reconciling.

## Projects

| Project | Target | Purpose |
| --- | --- | --- |
| `src/Blackjack.Game` | net10.0 | Rules engine. No SPT reference, no I/O, no randomness it does not own. |
| `src/Blackjack.Server` | net10.0 | SPT server mod: routes, DI registration, currency. |
| `tests/Blackjack.Game.Tests` | net10.0 | 50 tests over the engine. |
| `tests/Blackjack.Server.Tests` | net10.0 | 19 tests over the money flow, using fakes. |
| `tools/Blackjack.Console` | net10.0 | Terminal table -- plays the engine with no SPT install. |
| `src/Blackjack.Client` | netstandard2.1 | *(not yet)* BepInEx plugin: UI and input. |

`Blackjack.Game` is currency-agnostic on purpose -- it deals in `int` wagers and
knows nothing about roubles. `Bank` in the server project is the only code that
maps a `Wallet` to an item template.

The server project is split so the interesting half is testable:

- `BlackjackService` holds the whole game flow and depends only on `IBank`,
  `IProfileGateway` and `TableStore`.
- `BlackjackCallbacks` is a thin HTTP adapter -- serialise, log, nothing else.
- `Bank` and `ProfileGateway` are the only classes that touch SPT services.

## How the player reaches the table

**Rest Space.** A blackjack table is added as an interactable object inside the
existing Rest Space area -- walk up, interact, the panel opens.

It is *not* its own hideout station. `HideoutAreas` ends at `CircleOfCultists = 27`
server-side, and the client carries a matching enum plus a Unity prefab baked into
the hideout scene for every area. A new enum value has no model and no icon, and
the client does not know it exists.

A configurable hotkey opens the same panel from anywhere. That exists so the whole
stack can be tested before the hideout interaction is wired up -- it is not the
intended way in. Bind it through the BepInEx config menu rather than hardcoding it,
and note F12 is unavailable: that is the config menu itself.

## Testing without an SPT install

SPT's `InventoryHelper`, `ProfileHelper` and `SaveServer` are concrete classes
with non-virtual methods, so anything depending on them directly cannot be tested
without a running server. `IBank` and `IProfileGateway` exist to break that: SPT's
DI registers a class against every interface it implements, so the real
implementations resolve with no extra wiring, and the tests substitute fakes.

What that buys: every path that moves currency is covered without SPT present --
stake collection, double and split top-ups, settlement, refusals, and per-currency
isolation. `MoneyInvariantTests` plays 400 random rounds and asserts, after each
one, that the money the service moved equals the profit the engine reported.

The suite was mutation-checked: collecting the full stake instead of the increase,
and paying out on losing hands, each fail 7 tests.

What remains unverified without a server: `Bank`'s own `InventoryHelper` calls, and
whether `scripts/smoke.ps1` resolves the session correctly.

## Engine

`BlackjackTable` is the whole game. Construct it, call `Deal`, then `Hit` /
`Stand` / `Double` / `Split`; every one returns the `RoundView` the client
renders. Illegal actions throw rather than being silently ignored.

Rules are configurable via `Rules`: deck count, dealer hits soft 17, blackjack
payout, double-after-split, split limit, one-card-after-ace-split, shoe
penetration, table limits.

### Rules the tests pin down

These are the ones implementations usually get wrong:

- A 21 assembled after a split is **not** a natural and pays even money, not 3:2.
- The dealer peeks, so a player never doubles or splits into a hand already lost.
- A player who busts loses immediately -- the dealer does not draw, even if it
  would also have busted. This is where the house edge actually comes from.
- Only one ace can count as 11, so scoring is a single conditional promotion.
- Split aces take exactly one card each and are then forced to stand.

Run them with `dotnet test`.

## Routes

| Route | Body | Purpose |
| --- | --- | --- |
| `POST /blackjack/deal` | `{ wallet, wager }` | Takes the stake, deals a round. |
| `POST /blackjack/action` | `{ action }` | Hit, Stand, Double or Split. |
| `POST /blackjack/state` | -- | Current round, for reconnecting a UI. |

All three return `{ ok, error, round, balance, wallet }`.

**Known limitation:** custom static routes do not flow through the ItemEventRouter,
so the client's own inventory model is stale until it refreshes. That is why every
response carries `balance` -- the UI must trust that number over anything it
computes locally. Moving the actions onto an ItemEventRouter action would fix it
properly and is the right call if the staleness turns out to be visible.

## Build and verify

```
dotnet build                              # everything
dotnet test                               # 69 tests, no SPT needed
dotnet run --project tools/Blackjack.Console   # play a hand in the terminal
```

The server mod output goes to `bin/<Config>/Blackjack.Server/`; copy that folder
into `<game>/SPT/user/mods/`. Then, with the server running:

```
scripts\smoke.ps1 -SessionId <your-profile-id>
```

That plays a hand over HTTP with no game client attached.

## SPT version sensitivity

Targets **SPT 4.1.3**. The `SPTarkov.*` NuGet packages lag the game: 4.1.2 is the
newest published, which is what this references. If a 4.1.3-specific API is
missing, reference the DLLs from the server install directly instead.

Note that SPT 4.0 moved server mods from TypeScript to C# -- 3.x guides do not
apply.

Things to re-check when the SPT version moves:

- `IModMetadata.SptVersion` is a hard gate; the mod will not load outside its
  range. It is `~4.1.3`, meaning `>=4.1.3 <4.2.0`.
- `InventoryHelper.RemoveItemByCount` / `AddItemToStash` signatures.
- `Money` template ids (stable so far, but they live in the server enum).
