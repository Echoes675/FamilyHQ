$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/DevStack.psm1" -Force

Describe 'Resolve-DevStackConfig' {
    It 'returns the fixed ports and container name' {
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo'
        $cfg.Ports.WebUi    | Should Be 7154
        $cfg.Ports.WebApi   | Should Be 7196
        $cfg.Ports.Simulator| Should Be 7199
        $cfg.ContainerName  | Should Be 'familyhq-dev-db'
        $cfg.PostgresImage  | Should Be 'postgres:17.4'
    }

    It 'defaults Postgres credentials when no .env is present' {
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo' -EnvFile 'C:\does\not\exist.env'
        $cfg.Postgres.User     | Should Be 'postgres'
        $cfg.Postgres.Password | Should Be 'postgres'
    }

    It 'lets a .env override the Postgres credentials' {
        $tmp = New-TemporaryFile
        Set-Content -Path $tmp -Value @('POSTGRES_USER=devuser','POSTGRES_PASSWORD=devpass')
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo' -EnvFile $tmp
        $cfg.Postgres.User     | Should Be 'devuser'
        $cfg.Postgres.Password | Should Be 'devpass'
        Remove-Item $tmp
    }

    It 'builds per-app connection strings against the two databases' {
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo'
        $cfg.ConnectionStrings.WebApi    | Should Match 'Database=familyhq;'
        $cfg.ConnectionStrings.Simulator | Should Match 'Database=familyhq_sim;'
        $cfg.ConnectionStrings.WebApi    | Should Match 'Password=postgres'
    }
}
