#Requires -Version 5.1
<#
.SYNOPSIS
    Demo/screenshot driver for a locally running OmnisRouter.

.DESCRIPTION
    Fires a spread of prompts (trivial -> hard) at the router and prints a colour-coded table of the
    routing decisions: which model each prompt was routed to, ROUTED vs ESCALATED, confidence,
    estimated cost, and estimated saving versus always using the strongest model. It then drives the
    same prompts through /v1/chat/completions so the built-in dashboard at /ui has real data to show.

    The Anthropic API key is read from an environment variable at runtime (default ANTHROPIC_API_KEY)
    and registered with the router as a BYOK provider key. The key value is never printed.

    Two screenshots come out of one run:
      1. The terminal decisions table this script prints.
      2. The web dashboard at http://localhost:8080/ui (paste the token, click Load).

.PARAMETER BaseUrl
    Router base URL. Default http://localhost:8080

.PARAMETER Token
    Router bearer token. Default 'test-token-local' (the bootstrap token seeded on the dev container).

.PARAMETER KeyEnvVar
    Name of the environment variable holding the Anthropic key. Default ANTHROPIC_API_KEY.

.PARAMETER DecisionsOnly
    Only call /v1/route (no cost, no upstream call, no dashboard data). Skips the real completions.

.PARAMETER RemoveKeyAfter
    Delete the provider key this script registered when it finishes (only if this run created it).

.EXAMPLE
    pwsh ./scripts/demo-router.ps1
.EXAMPLE
    pwsh ./scripts/demo-router.ps1 -DecisionsOnly     # zero-cost, dashboard not populated
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$Token = 'test-token-local',
    [string]$KeyEnvVar = 'ANTHROPIC_API_KEY',
    [switch]$DecisionsOnly,
    [switch]$RemoveKeyAfter
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}

$auth = @{ Authorization = "Bearer $Token" }

function Read-EnvKey([string]$name) {
    foreach ($scope in 'Process', 'User', 'Machine') {
        $v = [Environment]::GetEnvironmentVariable($name, $scope)
        if (-not [string]::IsNullOrWhiteSpace($v)) { return $v }
    }
    return $null
}

function Money($n) { '$' + ([double]$n).ToString('0.000000') }

function Write-Cell([string]$text, [int]$width, [string]$colour, [switch]$Right) {
    if ($text.Length -gt $width) { $text = $text.Substring(0, [Math]::Max(0, $width - 1)) + [char]0x2026 }
    $pad = if ($Right) { $text.PadLeft($width) } else { $text.PadRight($width) }
    Write-Host $pad -NoNewline -ForegroundColor $colour
    Write-Host '  ' -NoNewline
}

# --- prompts: trivial -> hard, so the router shows a spread of models/decisions ------------------
$prompts = @(
    'Say hello.',
    "What's 2 + 2?",
    'Translate "good morning" into French.',
    'Write a haiku about routing.',
    'Explain how HTTP caching works, briefly.',
    'Refactor a recursive Fibonacci into an iterative version in Rust and note the tradeoffs.',
    'Prove that the square root of 2 is irrational.',
    'Design a fault-tolerant distributed rate limiter and analyse its failure modes.'
)

Write-Host ''
Write-Host '  OmnisRouter ' -NoNewline -ForegroundColor White
Write-Host "- live routing demo  ($BaseUrl)" -ForegroundColor DarkGray
Write-Host ''

# --- health -------------------------------------------------------------------------------------
try {
    $h = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 5
    Write-Host '  health  ' -NoNewline -ForegroundColor DarkGray
    Write-Host $h.status -ForegroundColor Green
} catch {
    Write-Host "  health  unreachable - is the router running on $BaseUrl ?" -ForegroundColor Red
    exit 1
}

# --- ensure an Anthropic BYOK key is registered -------------------------------------------------
$createdKeyId = $null
$existing = Invoke-RestMethod -Uri "$BaseUrl/v1/keys" -Headers $auth
if (-not ($existing | Where-Object { $_.provider -eq 'anthropic' })) {
    $apiKey = Read-EnvKey $KeyEnvVar
    if (-not $apiKey) {
        Write-Host "  no Anthropic key found in `$env:$KeyEnvVar (checked Process/User/Machine)." -ForegroundColor Red
        Write-Host '  set it, or pass -KeyEnvVar <name>, then re-run.' -ForegroundColor DarkGray
        exit 1
    }
    # The key value is only ever held in $apiKey / the request body - never written to output.
    $body = @{ provider = 'anthropic'; label = 'demo-env-key'; api_key = $apiKey } | ConvertTo-Json
    $created = Invoke-RestMethod -Uri "$BaseUrl/v1/keys" -Method Post -Headers $auth -ContentType 'application/json' -Body $body
    $createdKeyId = $created.id
    Remove-Variable apiKey, body
    Write-Host '  byok    ' -NoNewline -ForegroundColor DarkGray
    Write-Host "registered anthropic key from `$env:$KeyEnvVar (stored encrypted)" -ForegroundColor Green
} else {
    Write-Host '  byok    ' -NoNewline -ForegroundColor DarkGray
    Write-Host 'anthropic key already configured' -ForegroundColor Green
}

# --- header -------------------------------------------------------------------------------------
Write-Host ''
Write-Host '  ' -NoNewline
Write-Cell 'PROMPT'        30 DarkGray
Write-Cell 'ROUTED TO'     22 DarkGray
Write-Cell 'DECISION'      10 DarkGray
Write-Cell 'CONF'          6  DarkGray -Right
Write-Cell 'EST COST'      11 DarkGray -Right
Write-Cell 'SAVED VS BIG'  13 DarkGray -Right
Write-Host ''
Write-Host ('  ' + ('-' * 96)) -ForegroundColor DarkGray

$totalCost = 0.0; $totalSaved = 0.0; $escalations = 0
$replies = @()

foreach ($p in $prompts) {
    $payload = @{ model = 'auto'; messages = @(@{ role = 'user'; content = $p }) } | ConvertTo-Json -Depth 5

    if ($DecisionsOnly) {
        $d = Invoke-RestMethod -Uri "$BaseUrl/v1/route" -Method Post -Headers $auth -ContentType 'application/json' -Body $payload
        $model = "$($d.chosen.provider)/$($d.chosen.model_id)"
        $decision = $d.decision; $conf = $d.confidence
        $cost = [double]$d.est_cost_usd; $delta = [double]$d.est_cost_delta_vs_big_usd
    } else {
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/v1/chat/completions" -Method Post -Headers $auth -ContentType 'application/json' -Body $payload
            $hdr = $resp.Headers
            $model = ([string]($hdr['X-Omnis-Model'])).Trim()
            $decision = ([string]($hdr['X-Omnis-Decision'])).Trim()
            $conf = [double]([string]($hdr['X-Omnis-Confidence']))
            $cost = [double]([string]($hdr['X-Omnis-Cost-Usd']))
            $delta = [double]([string]($hdr['X-Omnis-Cost-Delta-Vs-Big']))
            $reply = (($resp.Content | ConvertFrom-Json).choices[0].message.content -replace '\s+', ' ').Trim()
            $replies += [pscustomobject]@{ Prompt = $p; Model = $model; Reply = $reply }
        } catch {
            $model = 'ERROR'; $decision = 'ERROR'; $conf = 0; $cost = 0; $delta = 0
        }
    }

    $saved = if ($delta -lt 0) { [Math]::Abs($delta) } else { 0.0 }
    $totalCost += $cost; $totalSaved += $saved
    $dcolour = switch ($decision) { 'ESCALATED' { 'Yellow' } 'ROUTED' { 'Cyan' } 'ERROR' { 'Red' } default { 'Gray' } }
    if ($decision -eq 'ESCALATED') { $escalations++ }

    Write-Host '  ' -NoNewline
    Write-Cell $p                       30 Gray
    Write-Cell $model                   22 White
    Write-Cell $decision                10 $dcolour
    Write-Cell ($conf.ToString('0.00')) 6  Gray -Right
    Write-Cell (Money $cost)            11 Gray -Right
    Write-Cell $(if ($saved -gt 0) { Money $saved } else { '-' }) 13 Green -Right
    Write-Host ''
}

Write-Host ('  ' + ('-' * 96)) -ForegroundColor DarkGray
Write-Host '  ' -NoNewline
Write-Host ("{0} prompts   " -f $prompts.Count) -NoNewline -ForegroundColor White
Write-Host ("est spend {0}   " -f (Money $totalCost)) -NoNewline -ForegroundColor Gray
Write-Host ("saved {0}   " -f (Money $totalSaved)) -NoNewline -ForegroundColor Green
Write-Host ("{0} escalations" -f $escalations) -ForegroundColor Yellow
Write-Host ''

# --- a couple of real replies, to show it actually answered -------------------------------------
if (-not $DecisionsOnly -and $replies.Count -gt 0) {
    Write-Host '  live replies' -ForegroundColor DarkGray
    foreach ($r in ($replies | Select-Object -First 3)) {
        Write-Host ('  ' + $r.Prompt) -ForegroundColor Gray
        $snippet = if ($r.Reply.Length -gt 88) { $r.Reply.Substring(0, 88) + [char]0x2026 } else { $r.Reply }
        Write-Host ('    ' + $r.Model + ': ') -NoNewline -ForegroundColor DarkCyan
        Write-Host $snippet -ForegroundColor White
    }
    Write-Host ''
    Write-Host '  dashboard  ' -NoNewline -ForegroundColor DarkGray
    Write-Host "$BaseUrl/ui" -NoNewline -ForegroundColor Cyan
    Write-Host "  (paste the token, click Load)" -ForegroundColor DarkGray
    Write-Host ''
}

# --- optional cleanup of the key this run registered --------------------------------------------
if ($RemoveKeyAfter -and $createdKeyId) {
    Invoke-RestMethod -Uri "$BaseUrl/v1/keys/$createdKeyId" -Method Delete -Headers $auth | Out-Null
    Write-Host "  cleanup  removed the demo BYOK key ($createdKeyId)" -ForegroundColor DarkGray
} elseif ($createdKeyId) {
    Write-Host "  note     BYOK key left registered (id $createdKeyId). Remove with:" -ForegroundColor DarkGray
    Write-Host "           curl -X DELETE $BaseUrl/v1/keys/$createdKeyId -H 'Authorization: Bearer $Token'" -ForegroundColor DarkGray
}
