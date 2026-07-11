#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'status', 'reset', 'e2e')]
    [string]$Command = 'status',

    [string]$Filter,
    [switch]$KeepData,
    [switch]$Reuse,
    [switch]$Force,
    [switch]$Headed,
    [switch]$ForceBrowserInstall,
    [int]$InstallTimeoutMinutes = 5,
    [int]$TestTimeoutMinutes = 30,
    [int]$MaxParallelThreads = 1,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
Import-Module "$PSScriptRoot/DevStack/DevStack.psm1" -Force
$cfg = Resolve-DevStackConfig -RepoRoot $RepoRoot

function Assert-DevCerts {
    & dotnet dev-certs https --check *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "No trusted ASP.NET dev HTTPS certificate found. Run: dotnet dev-certs https --trust"
    }
}

function Show-DevStackStatus {
    Write-Host "FamilyHQ local stack:"
    foreach ($svc in $cfg.Services) {
        $healthy = Test-DevStackServiceHealthy -Service $svc
        $state = if ($healthy) { 'UP  ' } else { 'down' }
        Write-Host ("  [{0}] {1,-10} https://localhost:{2}{3}" -f $state, $svc.Name, $svc.Port, $svc.HealthPath)
    }
    $db = docker ps --filter "name=$($cfg.ContainerName)" --format '{{.Names}}' 2>$null |
          Where-Object { $_ -eq $cfg.ContainerName }
    $dbState = if ($db) { 'UP  ' } else { 'down' }
    Write-Host ("  [{0}] postgres   {1} (host port {2})" -f $dbState, $cfg.PostgresImage, $cfg.Postgres.HostPort)
}

function Invoke-Up {
    Assert-DevCerts
    Initialize-DevStackState -Config $cfg

    if ($Reuse -and ($cfg.Services | ForEach-Object { Test-DevStackServiceHealthy -Service $_ }) -notcontains $false) {
        Write-Host "All services already healthy; attaching (--reuse)."
        Show-DevStackStatus
        return
    }

    Invoke-DevStackReconcile -Config $cfg -Force:$Force
    Start-DevStackPostgres -Config $cfg -KeepData:$KeepData

    $pids = @{}
    foreach ($svc in $cfg.Services) {
        Write-Host "Starting $($svc.Name)..."
        $pids[$svc.Name] = Start-DevStackService -Config $cfg -Service $svc
        if (-not (Wait-DevStackHealthy -Service $svc)) {
            Write-Host "---- $($svc.Name) failed to become healthy; last log lines: ----"
            Get-Content (Join-Path $cfg.LogDir "$($svc.Name).out.log") -Tail 20 -ErrorAction SilentlyContinue
            throw "$($svc.Name) did not become healthy"
        }
        Write-Host "$($svc.Name) healthy."
    }
    Save-DevStackState -Config $cfg -Pids $pids
    Show-DevStackStatus
}

function Invoke-Down {
    Invoke-DevStackReconcile -Config $cfg -Force:$Force
    Stop-DevStackPostgres -Config $cfg -KeepData:$KeepData
    $statePath = Join-Path $cfg.StateDir 'state.json'
    Remove-Item $statePath -ErrorAction SilentlyContinue
    Write-Host "Stack down."
}

function Invoke-E2E {
    $runStart = Get-Date
    if (($cfg.Services | ForEach-Object { Test-DevStackServiceHealthy -Service $_ }) -contains $false) {
        Write-Host "PHASE start: boot $((Get-Date).ToString('HH:mm:ss'))"
        Invoke-Up
        Write-Host "PHASE ok: boot"
    } else {
        Write-Host "PHASE ok: boot (skipped - stack already healthy)"
    }
    if ($Headed) { $env:TestConfiguration__Headless = 'false' } else { $env:TestConfiguration__Headless = 'true' }

    $install = Install-DevStackPlaywright -Config $cfg -TimeoutSeconds ($InstallTimeoutMinutes * 60) -Force:$ForceBrowserInstall
    if ($install.Action -eq 'installed' -and $install.Phase -and $install.Phase.Outcome -ne 'completed') {
        Stop-DevStackPlaywrightOrphans -Since $runStart | Out-Null
        Write-Warning "playwright-install did not complete ($($install.Phase.Outcome)); aborting. Stack left up; run 'down' to reset."
        exit 2
    }

    $resultsDir = Join-Path $cfg.RepoRoot 'TestResults'
    $trxPath = Join-Path $resultsDir 'e2e.trx'
    $testArgs = ConvertTo-DotnetTestArgs -Filter $Filter -TrxName 'e2e.trx' -ExtraArgs (@('--results-directory', $resultsDir) + $ExtraArgs)
    $featuresProj = Join-Path $cfg.RepoRoot 'tests-e2e/FamilyHQ.E2E.Features'

    Write-Host "Running E2E: dotnet test $($testArgs -join ' ')"
    $code = 0
    $runnerJson = Join-Path $cfg.RepoRoot 'tests-e2e/FamilyHQ.E2E.Features/xunit.runner.json'
    $origRunnerJson = $null
    try {
        # FHQ-150: high xUnit parallelism crashes Playwright's transport on local Windows/.NET 10
        # (CI on Linux is unaffected). Cap parallelism for this run only; the committed value that
        # CI uses is restored in the finally below.
        if (Test-Path $runnerJson) {
            $origRunnerJson = Get-Content -Raw -Path $runnerJson
            Set-Content -Path $runnerJson -NoNewline -Encoding utf8 `
                -Value (Set-XunitMaxParallelThreadsContent -Content $origRunnerJson -Value $MaxParallelThreads)
            Write-Host "Local run: xUnit maxParallelThreads capped to $MaxParallelThreads (FHQ-150; committed value restored after)."
        }
        $phase = Invoke-DevStackPhase -Name 'dotnet-test' -FilePath 'dotnet' `
            -Arguments (@('test', $featuresProj) + $testArgs) `
            -TimeoutSeconds ($TestTimeoutMinutes * 60) -WorkingDirectory $cfg.RepoRoot
        $code = if ($phase.Outcome -eq 'timeout') { 2 } else { $phase.ExitCode }
    } finally {
        if ($null -ne $origRunnerJson) { Set-Content -Path $runnerJson -Value $origRunnerJson -NoNewline -Encoding utf8 }
        $orphans = Stop-DevStackPlaywrightOrphans -Since $runStart
        if ($orphans -gt 0) { Write-Host "Cleaned up $orphans orphaned Playwright browser process(es)." }
    }
    Write-Host "E2E exit code: $code (TRX: $trxPath). Stack left up; run 'down' to reset."
    exit $code
}

switch ($Command) {
    'up'     { Invoke-Up }
    'down'   { Invoke-Down }
    'status' { Show-DevStackStatus }
    'reset'  { Invoke-Down; Invoke-Up }
    'e2e'    { Invoke-E2E }
}
