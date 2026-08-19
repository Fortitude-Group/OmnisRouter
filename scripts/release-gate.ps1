<#
.SYNOPSIS
    OmnisRouter release gate (T068). Implements FR-018 / SC-009: "no release is tagged unless
    its published benchmark run passes the gate alongside a clean build and green tests."

.DESCRIPTION
    Blocks a release tag unless ALL of the following pass, in order:

      1. Build      dotnet build OmnisRouter.slnx -c Release
                     Requires 0 Error(s) AND 0 Warning(s). Directory.Build.props sets
                     <TreatWarningsAsErrors>true</TreatWarningsAsErrors>, so a genuine compiler
                     warning already fails the build with a non-zero exit code — but NuGet/restore
                     warnings (e.g. NU1901 advisories) are NOT compiler errors and can leave the
                     build "succeeded" with a non-zero Warning(s) count, so we parse the dotnet
                     build summary explicitly rather than trusting the exit code alone.

      2. Tests      dotnet test OmnisRouter.slnx -c Release
                     Requires every test project to pass (non-zero exit code on any failure).

      3. Benchmark  Invokes OmnisBench (github.com/Fortitude-Group/OmnisBench), the companion
                     Python benchmark program that produces the routing policy inputs AND serves
                     as this release gate (see specs/001-omnisrouter/spec.md Dependencies, and
                     routing/BUILD.md). OmnisBench lives in a SEPARATE repo, so this step shells
                     out to a caller-supplied command via -OmnisBenchCommand rather than hard-coding
                     a path to a sibling checkout. A typical real invocation looks like:

                         omnisbench verify --run runs/<release-date>

                     which re-grades a published run bundle (results.json + report.html) offline,
                     with zero live API calls (see the OmnisBench CLI: `run` produces a bundle,
                     `verify` re-grades it from stored responses). FR-018 requires that EACH
                     release PUBLISH the OmnisBench frontier (the run bundle) it gates against —
                     that publishing step is a release-process responsibility, not something this
                     script can do for a repo it doesn't own; the script only *gates* on it.

                     -OmnisBenchCommand defaults to an intentionally-unwired PLACEHOLDER that
                     prints a loud warning and FAILS the gate (never silently passes) so nobody can
                     tag a release believing the benchmark ran when it did not. Wire a real release
                     by passing the actual command, e.g.:

                         -OmnisBenchCommand "omnisbench verify --run runs/2026-08-19"

    On full success, prints (but does NOT run) the `git tag` command for -Tag, per T068 — this
    script never creates the tag itself; that remains a deliberate, separate human action.

.PARAMETER Tag
    The version to release, e.g. "v0.4.0". Only used to print the suggested `git tag` command
    once all gates pass. Optional — omit to just run the gate (e.g. in CI) without a tag prompt.

.PARAMETER OmnisBenchCommand
    Shell command that invokes OmnisBench's own verification/benchmark check for this release and
    returns a non-zero exit code on failure. See DESCRIPTION for the default placeholder behavior
    and a realistic example.

.PARAMETER Solution
    Path to the solution file. Defaults to OmnisRouter.slnx at the repo root (resolved relative to
    this script's own location, so it works regardless of the caller's working directory).

.PARAMETER Configuration
    Build/test configuration. Defaults to "Release" (release gates should never run against Debug).

.PARAMETER Help
    Show this help and exit without doing anything.

.EXAMPLE
    pwsh -NoProfile -File scripts/release-gate.ps1 -Help

.EXAMPLE
    # CI usage: run the gate, benchmark step deliberately left unwired (will FAIL, on purpose).
    pwsh -NoProfile -File scripts/release-gate.ps1

.EXAMPLE
    # Real release: benchmark step wired to a published OmnisBench run bundle.
    pwsh -NoProfile -File scripts/release-gate.ps1 `
        -Tag v0.4.0 `
        -OmnisBenchCommand "omnisbench verify --run runs/2026-08-19"
#>
[CmdletBinding()]
param(
    [string]$Tag,

    [string]$OmnisBenchCommand = (
        'Write-Warning "[OmnisBench PLACEHOLDER] -OmnisBenchCommand was not supplied — the ' +
        'benchmark gate is NOT wired to a real OmnisBench run and cannot pass. This release gate ' +
        'treats an unwired benchmark step as a FAILURE, never a silent pass. Wire it with e.g.: ' +
        '-OmnisBenchCommand ''omnisbench verify --run runs/<release-date>'' (see routing/BUILD.md ' +
        'and the OmnisBench repo). Remember FR-018: each release must PUBLISH the OmnisBench ' +
        'frontier it gates against, not just pass it locally."; ' +
        'throw "OmnisBench benchmark gate not wired (see warning above)"'
    ),

    [string]$Solution = (Join-Path (Split-Path -Parent $PSScriptRoot) 'OmnisRouter.slnx'),

    [string]$Configuration = 'Release',

    [switch]$Help
)

$ErrorActionPreference = 'Stop'

function Show-Usage {
    Write-Host @'
OmnisRouter release gate (scripts/release-gate.ps1)

Blocks a release tag unless build + tests + OmnisBench benchmark all pass (FR-018 / SC-009).

USAGE
    pwsh -NoProfile -File scripts/release-gate.ps1 [-Tag <version>] [-OmnisBenchCommand <cmd>]
                                                    [-Solution <path>] [-Configuration <cfg>] [-Help]

PARAMETERS
    -Tag <version>            Version to print the git tag command for on success, e.g. v0.4.0.
                              Never creates the tag itself.
    -OmnisBenchCommand <cmd>  Command that runs OmnisBench's release verification and returns a
                              non-zero exit code on failure. Defaults to an unwired placeholder
                              that WARNS and FAILS the gate — it never silently passes.
                              Example: -OmnisBenchCommand "omnisbench verify --run runs/2026-08-19"
    -Solution <path>          Path to OmnisRouter.slnx. Defaults to the repo root.
    -Configuration <cfg>      Build/test configuration. Defaults to "Release".
    -Help                     Show this help and exit.

EXAMPLES
    pwsh -NoProfile -File scripts/release-gate.ps1 -Help
    pwsh -NoProfile -File scripts/release-gate.ps1
    pwsh -NoProfile -File scripts/release-gate.ps1 -Tag v0.4.0 `
        -OmnisBenchCommand "omnisbench verify --run runs/2026-08-19"
'@
}

if ($Help) {
    Show-Usage
    exit 0
}

# ---------------------------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------------------------

# Tracks one gate's outcome for the final PASS/FAIL summary table.
$script:GateResults = New-Object System.Collections.Generic.List[pscustomobject]

function Add-GateResult {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $script:GateResults.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}

function Write-Section {
    param([string]$Title)
    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------------------------
# Gate 1: Build (Release, 0 errors, 0 warnings)
# ---------------------------------------------------------------------------------------------

Write-Section "Gate 1/3: dotnet build $Solution -c $Configuration"

$buildOutput = & dotnet build $Solution -c $Configuration 2>&1 | Tee-Object -Variable buildOutputRaw
$buildExitCode = $LASTEXITCODE
$buildText = ($buildOutput | Out-String)

# dotnet's final summary lines look like:
#     0 Warning(s)
#     0 Error(s)
# Take the LAST match of each (the aggregate solution-wide total), not per-project subtotals.
$warningMatches = [regex]::Matches($buildText, '(?m)^\s*(\d+)\s+Warning\(s\)\s*$')
$errorMatches   = [regex]::Matches($buildText, '(?m)^\s*(\d+)\s+Error\(s\)\s*$')
$warningCount = if ($warningMatches.Count -gt 0) { [int]$warningMatches[$warningMatches.Count - 1].Groups[1].Value } else { -1 }
$errorCount   = if ($errorMatches.Count   -gt 0) { [int]$errorMatches[$errorMatches.Count - 1].Groups[1].Value }   else { -1 }

$buildPassed = ($buildExitCode -eq 0) -and ($errorCount -eq 0) -and ($warningCount -eq 0)

if ($buildPassed) {
    Write-Host "PASS  build succeeded: 0 Warning(s), 0 Error(s)" -ForegroundColor Green
    Add-GateResult -Name 'Build (Release)' -Passed $true -Detail '0 warnings, 0 errors'
}
else {
    Write-Host $buildText
    $detail = "exit=$buildExitCode warnings=$warningCount errors=$errorCount"
    Write-Host "FAIL  build gate failed ($detail)" -ForegroundColor Red
    Add-GateResult -Name 'Build (Release)' -Passed $false -Detail $detail
}

# ---------------------------------------------------------------------------------------------
# Gate 2: Tests (all green)
# ---------------------------------------------------------------------------------------------

Write-Section "Gate 2/3: dotnet test $Solution -c $Configuration"

$testOutput = & dotnet test $Solution -c $Configuration 2>&1 | Tee-Object -Variable testOutputRaw
$testExitCode = $LASTEXITCODE
$testText = ($testOutput | Out-String)

$testPassed = ($testExitCode -eq 0)

if ($testPassed) {
    Write-Host "PASS  all tests green" -ForegroundColor Green
    Add-GateResult -Name 'Tests (Release)' -Passed $true -Detail 'exit=0'
}
else {
    Write-Host $testText
    Write-Host "FAIL  one or more tests failed (exit=$testExitCode)" -ForegroundColor Red
    Add-GateResult -Name 'Tests (Release)' -Passed $false -Detail "exit=$testExitCode"
}

# ---------------------------------------------------------------------------------------------
# Gate 3: OmnisBench benchmark
# ---------------------------------------------------------------------------------------------

Write-Section "Gate 3/3: OmnisBench benchmark"
Write-Host "Command: $OmnisBenchCommand"

$LASTEXITCODE = $null
$benchPassed = $false
$benchDetail = ''
try {
    Invoke-Expression $OmnisBenchCommand
    # Native commands set $LASTEXITCODE; pure-PowerShell commands leave it untouched. Treat
    # "never set, no exception thrown" as success (e.g. a caller-supplied PS function that
    # returns normally), and "explicitly non-zero" as failure — but never treat a stale exit
    # code from an EARLIER dotnet call as this gate's result, since we reset it to $null above.
    if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -eq 0) {
        $benchPassed = $true
        $benchDetail = 'command completed successfully'
    }
    else {
        $benchDetail = "command exited with code $LASTEXITCODE"
    }
}
catch {
    $benchDetail = $_.Exception.Message
}

if ($benchPassed) {
    Write-Host "PASS  OmnisBench gate: $benchDetail" -ForegroundColor Green
    Add-GateResult -Name 'OmnisBench benchmark' -Passed $true -Detail $benchDetail
}
else {
    Write-Host "FAIL  OmnisBench gate: $benchDetail" -ForegroundColor Red
    Add-GateResult -Name 'OmnisBench benchmark' -Passed $false -Detail $benchDetail
}

# ---------------------------------------------------------------------------------------------
# Summary + tag command
# ---------------------------------------------------------------------------------------------

Write-Section 'Release gate summary'

$allPassed = $true
foreach ($result in $script:GateResults) {
    $status = if ($result.Passed) { 'PASS' } else { 'FAIL' }
    $color = if ($result.Passed) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1,-24} {2}" -f $status, $result.Name, $result.Detail) -ForegroundColor $color
    if (-not $result.Passed) { $allPassed = $false }
}

Write-Host ''

if ($allPassed) {
    Write-Host 'RELEASE GATE: PASS — all checks green.' -ForegroundColor Green
    if ($Tag) {
        Write-Host ''
        Write-Host 'Gate passed. This script does NOT create the tag — run it yourself:'
        Write-Host "    git tag -a $Tag -m `"Release $Tag`"" -ForegroundColor Yellow
        Write-Host "    git push origin $Tag" -ForegroundColor Yellow
        Write-Host ''
        Write-Host 'Remember (FR-018): publish the OmnisBench frontier (run bundle) that backed' -ForegroundColor Yellow
        Write-Host "this gate's benchmark step alongside the $Tag release." -ForegroundColor Yellow
    }
    else {
        Write-Host ''
        Write-Host 'No -Tag supplied — pass -Tag <version> to see the suggested git tag command.'
    }
    exit 0
}
else {
    Write-Host 'RELEASE GATE: FAIL — do not tag a release.' -ForegroundColor Red
    exit 1
}
