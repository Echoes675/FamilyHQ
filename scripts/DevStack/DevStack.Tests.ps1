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
        $cfg.ConnectionStrings.WebApi    | Should Match 'Port=5433;'
        $cfg.Postgres.HostPort           | Should Be 5433
    }

    It 'lets a .env override the Postgres host port' {
        $tmp = (New-TemporaryFile).FullName
        try {
            Set-Content -Path $tmp -Value @('POSTGRES_HOST_PORT=5544')
            $cfg = Resolve-DevStackConfig -RepoRoot 'C:\repo' -EnvFile $tmp
            $cfg.Postgres.HostPort        | Should Be 5544
            $cfg.ConnectionStrings.WebApi | Should Match 'Port=5544;'
        } finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
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

    It 'accepts the compiled service exe launched under the repo (the dotnet run child process)' {
        # dotnet run spawns the real listener as a child .exe under bin/, not dotnet.exe.
        # Get-NetTCPConnection returns that child PID, so the guard must recognise it.
        $exe = "$repo\tools\FamilyHQ.Simulator\bin\Debug\net10.0\FamilyHQ.Simulator.exe"
        $p = [pscustomobject]@{ Path = $exe; CommandLine = $exe }
        Test-IsFamilyHqProcess -Process $p -RepoRoot $repo | Should Be $true
    }

    It 'rejects a compiled exe living under a sibling repo that shares our prefix' {
        $exe = 'D:\Git\Echoes675\FamilyHQExtra\bin\Debug\net10.0\SomeApp.exe'
        $p = [pscustomobject]@{ Path = $exe; CommandLine = $exe }
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

Describe 'Invoke-DevStackPhase' {
    It 'returns completed with exit code 0 for a fast success' {
        $r = Invoke-DevStackPhase -Name 'ok' -FilePath 'pwsh' -Arguments @('-NoProfile','-Command','exit 0') -TimeoutSeconds 30
        $r.Outcome  | Should Be 'completed'
        $r.ExitCode | Should Be 0
    }
    It 'returns failed with the child exit code for a non-zero exit' {
        $r = Invoke-DevStackPhase -Name 'bad' -FilePath 'pwsh' -Arguments @('-NoProfile','-Command','exit 3') -TimeoutSeconds 30
        $r.Outcome  | Should Be 'failed'
        $r.ExitCode | Should Be 3
    }
    It 'returns timeout and kills a child that overruns' {
        $r = Invoke-DevStackPhase -Name 'slow' -FilePath 'pwsh' -Arguments @('-NoProfile','-Command','Start-Sleep 30') -TimeoutSeconds 2
        $r.Outcome | Should Be 'timeout'
        ($r.DurationSeconds -lt 10) | Should Be $true
    }
}

Describe 'Test-PlaywrightChromiumInstalled' {
    It 'returns false when the browsers dir is absent' {
        Test-PlaywrightChromiumInstalled -BrowsersPath (Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())) | Should Be $false
    }
    It 'returns false when no chromium build is present' {
        $dir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid()); New-Item -ItemType Directory -Path $dir | Out-Null
        try { Test-PlaywrightChromiumInstalled -BrowsersPath $dir | Should Be $false }
        finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'returns true when a chromium build folder exists' {
        $dir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
        New-Item -ItemType Directory -Path (Join-Path $dir 'chromium-1234') | Out-Null
        try { Test-PlaywrightChromiumInstalled -BrowsersPath $dir | Should Be $true }
        finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Describe 'Install-DevStackPlaywright' {
    BeforeEach {
        $script:prev = $env:PLAYWRIGHT_BROWSERS_PATH
        $script:browsers = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
        $env:PLAYWRIGHT_BROWSERS_PATH = $script:browsers
        $script:repo = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
        $pwDir = Join-Path $script:repo 'tests-e2e/FamilyHQ.E2E.Features/bin/Debug/net10.0'
        New-Item -ItemType Directory -Path $pwDir -Force | Out-Null
        $script:marker = Join-Path $script:repo 'installed.marker'
        Set-Content -Path (Join-Path $pwDir 'playwright.ps1') -Value "Set-Content -Path '$($script:marker)' -Value 'ran'; exit 0"
        $script:cfg = [pscustomobject]@{ RepoRoot = $script:repo }
    }
    AfterEach {
        $env:PLAYWRIGHT_BROWSERS_PATH = $script:prev
        Remove-Item $script:browsers,$script:repo -Recurse -Force -ErrorAction SilentlyContinue
    }
    It 'skips install when chromium is already present' {
        New-Item -ItemType Directory -Path (Join-Path $script:browsers 'chromium-1234') -Force | Out-Null
        $r = Install-DevStackPlaywright -Config $script:cfg
        $r.Action | Should Be 'skipped'
        (Test-Path $script:marker) | Should Be $false
    }
    It 'installs (runs playwright.ps1) when chromium is missing' {
        $r = Install-DevStackPlaywright -Config $script:cfg -TimeoutSeconds 60
        $r.Action | Should Be 'installed'
        (Test-Path $script:marker) | Should Be $true
    }
    It 'installs even when present if -Force is given' {
        New-Item -ItemType Directory -Path (Join-Path $script:browsers 'chromium-1234') -Force | Out-Null
        $r = Install-DevStackPlaywright -Config $script:cfg -TimeoutSeconds 60 -Force
        $r.Action | Should Be 'installed'
        (Test-Path $script:marker) | Should Be $true
    }
    It 'reports unavailable when playwright.ps1 is not built yet' {
        Remove-Item (Join-Path $script:repo 'tests-e2e/FamilyHQ.E2E.Features/bin/Debug/net10.0/playwright.ps1') -Force
        $r = Install-DevStackPlaywright -Config $script:cfg
        $r.Action | Should Be 'unavailable'
    }
}

Describe 'Test-IsPlaywrightOrphan' {
    $since = (Get-Date).AddMinutes(-5)
    It 'accepts a headless chromium with a playwright profile started after the run began' {
        $p = [pscustomobject]@{ Name='chrome'; CommandLine='chrome --headless --user-data-dir=C:\Temp\playwright_chromiumdev_profile'; StartTime=(Get-Date) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $true
    }
    It 'rejects a non-headless browser (a real user browser)' {
        $p = [pscustomobject]@{ Name='chrome'; CommandLine='chrome --profile-directory=Default https://example.com'; StartTime=(Get-Date) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $false
    }
    It 'rejects a non-browser process' {
        $p = [pscustomobject]@{ Name='dotnet'; CommandLine='dotnet test'; StartTime=(Get-Date) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $false
    }
    It 'rejects a matching browser that started before the run (not ours)' {
        $p = [pscustomobject]@{ Name='chrome'; CommandLine='chrome --headless --user-data-dir=C:\Temp\ms-playwright'; StartTime=$since.AddMinutes(-1) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $false
    }
    It 'returns false when command line is missing' {
        $p = [pscustomobject]@{ Name='chrome'; CommandLine=$null; StartTime=(Get-Date) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $false
    }
    It 'accepts headless_shell (no --headless flag) with a playwright profile' {
        $p = [pscustomobject]@{ Name='headless_shell'; CommandLine='C:\Users\x\AppData\Local\ms-playwright\chromium_headless_shell-1234\headless_shell.exe --user-data-dir=C:\Temp\playwright_chromiumdev_profile'; StartTime=(Get-Date) }
        Test-IsPlaywrightOrphan -Process $p -Since $since | Should Be $true
    }
}

Describe 'Set-XunitMaxParallelThreadsContent' {
    It 'rewrites the maxParallelThreads value and preserves the rest' {
        $c = "{`n  `"`$schema`": `"https://xunit.net/schema/current/xunit.runner.schema.json`",`n  `"maxParallelThreads`": 6`n}"
        $out = Set-XunitMaxParallelThreadsContent -Content $c -Value 1
        ($out -match '"maxParallelThreads":\s*1') | Should Be $true
        ($out -match '"maxParallelThreads":\s*6') | Should Be $false
        ($out -match 'xunit\.runner\.schema\.json') | Should Be $true
    }
    It 'handles compact spacing' {
        Set-XunitMaxParallelThreadsContent -Content '{"maxParallelThreads":8}' -Value 2 | Should Be '{"maxParallelThreads":2}'
    }
}
