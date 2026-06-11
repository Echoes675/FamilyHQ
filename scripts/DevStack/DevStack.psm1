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
    $pgUser = if ($envVars.ContainsKey('POSTGRES_USER')) { $envVars['POSTGRES_USER'] } else { 'postgres' }
    $pgPass = if ($envVars.ContainsKey('POSTGRES_PASSWORD')) { $envVars['POSTGRES_PASSWORD'] } else { 'postgres' }

    $baseConn = "Host=localhost;Port=5432;Username=$pgUser;Password=$pgPass"

    return [pscustomobject]@{
        RepoRoot       = $RepoRoot
        PostgresImage  = 'postgres:17.4'
        ContainerName  = 'familyhq-dev-db'
        VolumeName     = 'familyhq-dev-db-data'
        StateDir       = Join-Path $RepoRoot 'scripts/.dev-stack'
        LogDir         = Join-Path $RepoRoot 'scripts/.dev-stack/logs'
        Ports          = [pscustomobject]@{ WebUi = 7154; WebApi = 7196; Simulator = 7199 }
        Postgres       = [pscustomobject]@{ User = $pgUser; Password = $pgPass; Db = 'familyhq'; SimDb = 'familyhq_sim' }
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

Export-ModuleMember -Function Resolve-DevStackConfig, Test-IsFamilyHqProcess, Get-DevStackListenerProcess
