<#
.SYNOPSIS
    Plays a hand of blackjack against a running SPT server, with no game client.

.DESCRIPTION
    Exercises /blackjack/deal and /blackjack/action directly so the server mod can
    be verified before any client work exists. Watch the server console alongside
    this -- errors from the mod surface there, not in the HTTP response.

    UNVERIFIED: written on a machine with no SPT install. The session id is passed
    as a PHPSESSID cookie, which is how SPT identifies the profile, but if the
    server returns null or ignores the session, check how your build's HTTP
    listener resolves it and adjust $headers.

.PARAMETER SessionId
    The profile id. Find it in the filename under SPT\user\profiles\.

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "http://127.0.0.1:6969",
    [ValidateSet("Roubles", "Dollars", "Euros")][string]$Wallet = "Roubles",
    [int]$Wager = 10000
)

$ErrorActionPreference = "Stop"
$headers = @{ "Cookie" = "PHPSESSID=$SessionId"; "Content-Type" = "application/json" }

function Invoke-Blackjack {
    param([string]$Route, [hashtable]$Body = @{})

    $json = $Body | ConvertTo-Json -Compress
    $response = Invoke-RestMethod -Uri "$Server$Route" -Method Post -Headers $headers -Body $json
    return $response
}

function Show-Round {
    param($Response)

    if (-not $Response.ok) {
        Write-Host "  refused: $($Response.error)" -ForegroundColor Yellow
        return
    }

    $round = $Response.round
    $dealer = ($round.dealer.cards -join " ")
    Write-Host "  dealer  $dealer ($($round.dealer.value))"

    foreach ($hand in $round.playerHands) {
        $cards = ($hand.cards -join " ")
        $outcome = if ($hand.outcome -eq "Pending") { "" } else { "  $($hand.outcome)" }
        Write-Host "  player  $cards ($($hand.value))  staked $($hand.wager)$outcome"
    }

    Write-Host "  phase $($round.phase)   balance $($Response.balance) $($Response.wallet)"
    if ($round.availableActions) {
        Write-Host "  legal: $($round.availableActions -join ', ')" -ForegroundColor DarkGray
    }
}

Write-Host "Dealing $Wager $Wallet against $Server" -ForegroundColor Cyan
$state = Invoke-Blackjack -Route "/blackjack/deal" -Body @{ wallet = $Wallet; wager = $Wager }
Show-Round $state

# Play it out by standing immediately -- the point is to prove the round settles
# and the money moves, not to play well.
while ($state.ok -and $state.round.phase -eq "PlayerTurn") {
    Write-Host "Standing..." -ForegroundColor Cyan
    $state = Invoke-Blackjack -Route "/blackjack/action" -Body @{ action = "Stand" }
    Show-Round $state
}

Write-Host ""
Write-Host "Now check the stash in-game: the balance above is authoritative, but the" -ForegroundColor DarkGray
Write-Host "client's own inventory view will not refresh until it reloads." -ForegroundColor DarkGray
