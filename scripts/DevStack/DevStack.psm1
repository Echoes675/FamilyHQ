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

Export-ModuleMember -Function Resolve-DevStackConfig
