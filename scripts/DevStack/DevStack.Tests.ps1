$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
        $tmp = (New-TemporaryFile).FullName
        try {
            Set-Content -Path $tmp -Value @('POSTGRES_USER=devuser','POSTGRES_PASSWORD=devpass')
            $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo' -EnvFile $tmp
            $cfg.Postgres.User     | Should Be 'devuser'
            $cfg.Postgres.Password | Should Be 'devpass'
        } finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
    }

    It 'builds per-app connection strings against the two databases' {
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo'
        $cfg.ConnectionStrings.WebApi    | Should Match 'Database=familyhq;'
        $cfg.ConnectionStrings.Simulator | Should Match 'Database=familyhq_sim;'
        $cfg.ConnectionStrings.WebApi    | Should Match 'Password=postgres'
    }

    It 'returns three services in the expected order' {
        $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo'
        $cfg.Services.Count             | Should Be 3
        $cfg.Services[0].Name           | Should Be 'simulator'
        $cfg.Services[1].Name           | Should Be 'webapi'
        $cfg.Services[2].Name           | Should Be 'webui'
        $cfg.Services[1].ConnKey        | Should Be 'WebApi'
        ($cfg.Services[2].ConnKey -eq $null) | Should Be $true
    }

    It 'strips surrounding quotes and inline comments from .env values' {
        $tmp = (New-TemporaryFile).FullName
        try {
            Set-Content -Path $tmp -Value @('POSTGRES_USER=plainuser # the user','POSTGRES_PASSWORD="pa ss"')
            $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo' -EnvFile $tmp
            $cfg.Postgres.User     | Should Be 'plainuser'
            $cfg.Postgres.Password | Should Be 'pa ss'
        } finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Test-IsFamilyHqProcess' {
    $repo = 'D:\Git\Echoes675\FamilyHQ'

    It 'accepts a dotnet process whose command line references the repo' {
        $p = [pscustomobject]@{ Path = 'C:\Program Files\dotnet\dotnet.exe'; CommandLine = "dotnet run --project $repo\src\FamilyHQ.WebApi" }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $true
    }

    It 'rejects a non-dotnet process even on our port' {
        $p = [pscustomobject]@{ Path = 'C:\Windows\System32\svchost.exe'; CommandLine = 'svchost -k netsvcs' }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $false
    }

    It 'rejects a dotnet process for an unrelated repo' {
        $p = [pscustomobject]@{ Path = 'C:\Program Files\dotnet\dotnet.exe'; CommandLine = 'dotnet run --project C:\Other\App.csproj' }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $false
    }

    It 'returns false when command line is missing (cannot prove ownership)' {
        $p = [pscustomobject]@{ Path = 'C:\Program Files\dotnet\dotnet.exe'; CommandLine = $null }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $false
    }

    It 'rejects a dotnet process for a repo whose name shares our prefix (FamilyHQExtra)' {
        $p = [pscustomobject]@{
            Path        = 'C:\Program Files\dotnet\dotnet.exe'
            CommandLine = "dotnet run --project D:\Git\Echoes675\FamilyHQExtra\src\SomeApp.csproj"
        }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $false
    }
}

Describe 'ConvertTo-DotnetTestArgs' {
    It 'returns the exact arg array for a null filter' {
        $result = ConvertTo-DotnetTestArgs -Filter $null -TrxName 'e2e.trx'
        $result | Should Be @('--filter', 'Category!=ignore', '--logger', 'trx;LogFileName=e2e.trx', '--logger', 'console;verbosity=normal')
    }

    It 'combines a user filter with the ignore exclusion' {
        $result = ConvertTo-DotnetTestArgs -Filter 'Category=dashboard' -TrxName 'e2e.trx'
        ($result -join ' ') | Should Match 'Category=dashboard&Category!=ignore'
    }

    It 'parenthesises a bare OR filter before applying the ignore exclusion' {
        $result = ConvertTo-DotnetTestArgs -Filter 'Category=smoke|Category=dashboard' -TrxName 'e2e.trx'
        $result[1] | Should Be '(Category=smoke|Category=dashboard)&Category!=ignore'
    }

    It 'does not double-wrap an already-parenthesised OR filter' {
        $result = ConvertTo-DotnetTestArgs -Filter '(Category=smoke|Category=dashboard)' -TrxName 'e2e.trx'
        $result[1] | Should Be '(Category=smoke|Category=dashboard)&Category!=ignore'
    }

    It 'passes raw extra args through verbatim' {
        $result = ConvertTo-DotnetTestArgs -Filter $null -TrxName 'e2e.trx' -ExtraArgs @('--no-build')
        ($result -contains '--no-build') | Should Be $true
    }
}
