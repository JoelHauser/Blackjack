<#
.SYNOPSIS
    Plays a hand of blackjack against a running SPT server, with no game client.

.DESCRIPTION
    Pings first, then plays a round. The ping is the important half on a first run:
    it proves the mod loaded, the route is reachable, the session resolved to a real
    profile, and its money can be read -- all before a rouble is at stake. If the
    ping fails there is no point running the rest.

    Watch the server console alongside this. Every line the mod writes is prefixed
    "[Blackjack]", so it can be filtered out of the noise.

    UNVERIFIED: written on a machine with no SPT install. The session id is passed as
    a PHPSESSID cookie, which is how SPT identifies the profile, but if the ping
    comes back with a blank sessionId, that assumption is wrong -- check how your
    build's HTTP listener resolves it and adjust $headers.

.PARAMETER SessionId
    The profile id. Find it in the filename under SPT\user\profiles\.

.PARAMETER PingOnly
    Stop after the health check without betting anything.

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -PingOnly

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -Wallet Dollars -Wager 500
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "http://127.0.0.1:6969",
    [ValidateSet("Roubles", "Dollars", "Euros")][string]$Wallet = "Roubles",
    [int]$Wager = 10000,
    [switch]$PingOnly
)

$ErrorActionPreference = "Stop"
$headers = @{ "Cookie" = "PHPSESSID=$SessionId"; "Content-Type" = "application/json" }

function Invoke-Blackjack {
    param([string]$Route, [hashtable]$Body = @{})

    $json = $Body | ConvertTo-Json -Compress
    try {
        return Invoke-RestMethod -Uri "$Server$Route" -Method Post -Headers $headers -Body $json
    }
    catch {
        Write-Host "  request to $Route failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  if this is a connection error the server is not running; if it is a 404," -ForegroundColor DarkGray
        Write-Host "  the mod did not load -- check the server console for a [Blackjack] banner." -ForegroundColor DarkGray
        exit 1
    }
}

function Show-Round {
    param($Response)

    if (-not $Response.ok) {
        Write-Host "  refused: $($Response.error)" -ForegroundColor Yellow
    }

    $round = $Response.round
    if (-not $round) { return }

    Write-Host "  dealer  $($round.dealer.cards -join ' ') ($($round.dealer.value))"
    foreach ($hand in $round.playerHands) {
        $outcome = if ($hand.outcome -eq "Pending") { "" } else { "  $($hand.outcome)" }
        Write-Host "  player  $($hand.cards -join ' ') ($($hand.value))  staked $($hand.wager)$outcome"
    }

    Write-Host "  phase $($round.phase)   balance $($Response.balance) $($Response.wallet)"
    if ($round.availableActions) {
        Write-Host "  legal: $($round.availableActions -join ', ')" -ForegroundColor DarkGray
    }
}

# ---- health check -----------------------------------------------------------

Write-Host "Pinging $Server" -ForegroundColor Cyan
$ping = Invoke-Blackjack -Route "/blackjack/ping"

Write-Host "  mod version   $($ping.modVersion)"
Write-Host "  session       '$($ping.sessionId)'"
Write-Host "  profile       $(if ($ping.hasProfile) { 'found' } else { 'NOT FOUND' })"

if (-not $ping.sessionId) {
    Write-Host ""
    Write-Host "The server did not resolve a session. The PHPSESSID cookie assumption is wrong." -ForegroundColor Red
    exit 1
}

if (-not $ping.hasProfile) {
    Write-Host ""
    Write-Host "Session resolved but no profile matched it. Check the id against the" -ForegroundColor Red
    Write-Host "filename in SPT\user\profiles\." -ForegroundColor Red
    exit 1
}

foreach ($k in $ping.balances.PSObject.Properties) {
    Write-Host "  $($k.Name.PadRight(13))$($k.Value)"
}

if ($PingOnly) {
    Write-Host ""
    Write-Host "Ping OK. The mod is loaded, reachable, and can read the profile." -ForegroundColor Green
    exit 0
}

# ---- play a round -----------------------------------------------------------

Write-Host ""
Write-Host "Dealing $Wager $Wallet" -ForegroundColor Cyan
$state = Invoke-Blackjack -Route "/blackjack/deal" -Body @{ wallet = $Wallet; wager = $Wager }
Show-Round $state

# Stand immediately -- the point is proving the round settles and the money moves,
# not playing well.
while ($state.ok -and $state.round.phase -eq "PlayerTurn") {
    Write-Host "Standing..." -ForegroundColor Cyan
    $state = Invoke-Blackjack -Route "/blackjack/action" -Body @{ action = "Stand" }
    Show-Round $state
}

$after = Invoke-Blackjack -Route "/blackjack/ping"
$before = $ping.balances.$Wallet
$now = $after.balances.$Wallet

Write-Host ""
Write-Host "$Wallet $before -> $now (moved $($now - $before))" -ForegroundColor Cyan

if ($before -eq $now -and $state.round.net -ne 0) {
    Write-Host "Balance did not move but the round was not a push -- money is not reaching" -ForegroundColor Red
    Write-Host "the stash. Check the console for a [Blackjack] mismatch line." -ForegroundColor Red
}
else {
    Write-Host "Money moved. Open the stash in-game to confirm it is really there." -ForegroundColor Green
}

$stats = Invoke-Blackjack -Route "/blackjack/stats"
Write-Host "Record: $($stats.roundsPlayed) rounds, $($stats.wins)W/$($stats.pushes)P/$($stats.losses)L"
