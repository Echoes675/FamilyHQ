Set-StrictMode -Version Latest

function Read-DotEnv {
    param([string]$Path)
    $result = @{}
    if (-not $Path -or -not (Test-Path $Path)) { return $result }
    foreach ($line in Get-Content -Path $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trimmed.Substring(0, $idx).Trim()
        $rawVal = $trimmed.Substring($idx + 1).Trim()
        $wasQuoted = $rawVal.StartsWith('"') -or $rawVal.StartsWith("'")
        $val = $rawVal
        if (($val.StartsWith('"') -and $val.EndsWith('"')) -or
            ($val.StartsWith("'") -and $val.EndsWith("'"))) {
            $val = $val.Substring(1, $val.Length - 2)
        } elseif (-not $wasQuoted) {
            $commentIdx = $val.IndexOf(' #')
            if ($commentIdx -ge 0) { $val = $val.Substring(0, $commentIdx).TrimEnd() }
        }
        $result[$key] = $val
    }
    return $result
}

function Resolve-DevStackConfig {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$EnvFile = (Join-Path $RepoRoot 'scripts/DevStack/.env')
    )

    $envVars = Read-DotEnv -Path $EnvFile
    $pgUser     = if ($envVars.ContainsKey('POSTGRES_USER'))      { $envVars['POSTGRES_USER'] }      else { 'postgres' }
    $pgPass     = if ($envVars.ContainsKey('POSTGRES_PASSWORD'))  { $envVars['POSTGRES_PASSWORD'] }  else { 'postgres' }
    $pgHostPort = if ($envVars.ContainsKey('POSTGRES_HOST_PORT')) { [int]$envVars['POSTGRES_HOST_PORT'] } else { 5433 }

    $baseConn = "Host=localhost;Port=$pgHostPort;Username=$pgUser;Password=$pgPass"

    return [pscustomobject]@{
        RepoRoot       = $RepoRoot
        PostgresImage  = 'postgres:17.4'
        ContainerName  = 'familyhq-dev-db'
        VolumeName     = 'familyhq-dev-db-data'
        StateDir       = Join-Path $RepoRoot 'scripts/.dev-stack'
        LogDir         = Join-Path $RepoRoot 'scripts/.dev-stack/logs'
        Ports          = [pscustomobject]@{ WebUi = 7154; WebApi = 7196; Simulator = 7199 }
        Postgres       = [pscustomobject]@{ User = $pgUser; Password = $pgPass; Db = 'familyhq'; SimDb = 'familyhq_sim'; HostPort = $pgHostPort }
        ConnectionStrings = [pscustomobject]@{
            WebApi    = "$baseConn;Database=familyhq;"
            Simulator = "$baseConn;Database=familyhq_sim;"
        }
        Services = @(
            [pscustomobject]@{ Name = 'simulator'; Project = 'tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj'; Profile = 'FamilyHQ.Simulator'; Port = 7199; HealthPath = '/health';     ConnKey = 'Simulator' }
            [pscustomobject]@{ Name = 'webapi';    Project = 'src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj';         Profile = 'https';              Port = 7196; HealthPath = '/api/health'; ConnKey = 'WebApi' }
            [pscustomobject]@{ Name = 'webui';     Project = 'src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj';           Profile = 'FamilyHQ.WebUi';     Port = 7154; HealthPath = '/';           ConnKey = $null }
        )
    }
}

function Test-IsFamilyHqProcess {
    param(
        [Parameter(Mandatory)]$Process,
        [Parameter(Mandatory)][string]$RepoRoot
    )
    if (-not $Process) { return $false }
    $path = [string]$Process.Path
    $cmd  = [string]$Process.CommandLine
    if ([string]::IsNullOrWhiteSpace($cmd)) { return $false }
    $isDotnet = $path -match '(?i)dotnet(\.exe)?$'
    if (-not $isDotnet) { return $false }
    # Anchor on a trailing separator so a sibling repo whose name merely shares our prefix
    # (e.g. FamilyHQExtra) is NOT treated as ours. Known limitation: a separate dotnet
    # process given a sub-path of this repo as an explicit argument could still match;
    # acceptable risk for a local dev tool — the reconciler refuses unidentified holders.
    $anchor = $RepoRoot.ToLowerInvariant().TrimEnd('\') + '\'
    return $cmd.ToLowerInvariant().Contains($anchor)
}

function Get-DevStackListenerProcess {
    # Returns $null, or an object { Pid; Path; CommandLine } for the process listening on $Port.
    param([Parameter(Mandatory)][int]$Port)
    # Take the first entry; Kestrel binds IPv4+IPv6 under the same PID so all entries agree.
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -First 1
    if (-not $conn) { return $null }
    $procId = [int]$conn.OwningProcess
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    $cim  = Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        Pid         = $procId
        Path        = if ($proc) { $proc.Path } else { $null }
        CommandLine = if ($cim) { $cim.CommandLine } else { $null }
    }
}

function ConvertTo-DotnetTestArgs {
    param(
        [string]$Filter,
        [string]$TrxName = 'e2e.trx',
        [string[]]$ExtraArgs = @()
    )
    $ignore = 'Category!=ignore'
    if ([string]::IsNullOrWhiteSpace($Filter)) {
        $effective = $ignore
    } else {
        # dotnet test filter '&' binds tighter than '|', so a bare OR filter would leave
        # the ignore-exclusion applied to only the last branch. Wrap when an OR is present
        # and the filter is not already fully parenthesised.
        $safeFilter = if ($Filter -match '\|' -and $Filter -notmatch '^\(.*\)$') { "($Filter)" } else { $Filter }
        $effective = "$safeFilter&$ignore"
    }
    $testArgs = @('--filter', $effective, '--logger', "trx;LogFileName=$TrxName", '--logger', 'console;verbosity=normal')
    if ($ExtraArgs -and $ExtraArgs.Count -gt 0) { $testArgs += $ExtraArgs }
    return $testArgs
}

function Start-DevStackPostgres {
    param(
        [Parameter(Mandatory)]$Config,
        [switch]$KeepData
    )
    $name = $Config.ContainerName
    $existing = docker ps -a --filter "name=$name" --format '{{.Names}}' 2>$null |
                Where-Object { $_ -eq $name }
    if ($existing) {
        Write-Host "Removing existing container $name"
        docker rm -f $name | Out-Null
    }
    if (-not $KeepData) {
        docker volume rm $Config.VolumeName 2>$null | Out-Null
    }

    Write-Host "Starting Postgres ($($Config.PostgresImage))"
    docker run -d --name $name `
        -e "POSTGRES_USER=$($Config.Postgres.User)" `
        -e "POSTGRES_PASSWORD=$($Config.Postgres.Password)" `
        -e "POSTGRES_DB=$($Config.Postgres.Db)" `
        -p "$($Config.Postgres.HostPort):5432" `
        -v "$($Config.VolumeName):/var/lib/postgresql/data" `
        $Config.PostgresImage | Out-Null

    # Wait for readiness.
    $deadline = (Get-Date).AddSeconds(60)
    $pgReady = $false
    do {
        Start-Sleep -Seconds 1
        docker exec $name pg_isready -U $Config.Postgres.User 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $pgReady = $true }
    } while (-not $pgReady -and (Get-Date) -lt $deadline)
    if (-not $pgReady) { throw "Postgres did not become ready within 60s" }

    # Create the second database idempotently.
    $simDb = $Config.Postgres.SimDb
    $exists = docker exec $name psql -U $Config.Postgres.User -tAc "SELECT 1 FROM pg_database WHERE datname='$simDb'"
    if ("$exists".Trim() -ne '1') {
        docker exec $name psql -U $Config.Postgres.User -c "CREATE DATABASE ""$simDb""" | Out-Null
    }
    Write-Host "Postgres ready: $($Config.Postgres.Db) + $simDb"
}

function Stop-DevStackPostgres {
    param([Parameter(Mandatory)]$Config, [switch]$KeepData)
    docker rm -f $Config.ContainerName 2>$null | Out-Null
    if (-not $KeepData) { docker volume rm $Config.VolumeName 2>$null | Out-Null }
    Write-Host "Postgres container $($Config.ContainerName) stopped."
}

function Initialize-DevStackState {
    param([Parameter(Mandatory)]$Config)
    New-Item -ItemType Directory -Force -Path $Config.LogDir | Out-Null
}

function Start-DevStackService {
    # Launches one service via dotnet run with its launch profile, redirecting logs and
    # injecting the connection string (when the service needs one). Returns the PID.
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)]$Service
    )
    $project = Join-Path $Config.RepoRoot $Service.Project
    $outLog  = Join-Path $Config.LogDir "$($Service.Name).out.log"
    $errLog  = Join-Path $Config.LogDir "$($Service.Name).err.log"
    Set-Content -Path $outLog -Value '' ; Set-Content -Path $errLog -Value ''

    # Child inherits the parent env snapshot at spawn time; set the per-service connection
    # string immediately before launch so webapi/simulator get their own database.
    if ($Service.ConnKey) {
        $env:ConnectionStrings__DefaultConnection = $Config.ConnectionStrings.$($Service.ConnKey)
    } else {
        Remove-Item Env:\ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    }

    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', $project, '--launch-profile', $Service.Profile) `
        -WorkingDirectory $Config.RepoRoot `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog `
        -PassThru
    return $proc.Id
}

function Save-DevStackState {
    param([Parameter(Mandatory)]$Config, [Parameter(Mandatory)][hashtable]$Pids)
    $statePath = Join-Path $Config.StateDir 'state.json'
    $Pids | ConvertTo-Json | Set-Content -Path $statePath
}

Export-ModuleMember -Function Resolve-DevStackConfig, Test-IsFamilyHqProcess, Get-DevStackListenerProcess, ConvertTo-DotnetTestArgs, Start-DevStackPostgres, Stop-DevStackPostgres, Initialize-DevStackState, Start-DevStackService, Save-DevStackState
