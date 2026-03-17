param(
    [string]$OutputDir = "$PSScriptRoot\..\certs"
)

$environments = @(
    @{ Name = "Dev";     Password = "REPLACE-ME" }
    @{ Name = "Staging"; Password = "REPLACE-ME" }
    @{ Name = "Prod";    Password = "REPLACE-ME" }
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

foreach ($env in $environments) {
    $name = $env.Name
    $password = $env.Password
    $pfxFile = Join-Path $OutputDir "dp-$($name.ToLower()).pfx"
    $keyFile = Join-Path $OutputDir "dp-key.pem"
    $certFile = Join-Path $OutputDir "dp-cert.pem"

    Write-Host "Creating $name certificate..." -ForegroundColor Cyan

    openssl req -x509 -newkey rsa:2048 -keyout $keyFile -out $certFile -days 10950 -nodes -subj "/CN=FamilyHQ-DataProtection-$name" 2>$null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: openssl not found or failed. Ensure OpenSSL is installed and on PATH." -ForegroundColor Red
        exit 1
    }

    openssl pkcs12 -export -out $pfxFile -inkey $keyFile -in $certFile -password "pass:$password"

    Remove-Item $keyFile, $certFile -Force

    Write-Host "  Created: $pfxFile" -ForegroundColor Green
    Write-Host "  Password: $password" -ForegroundColor Yellow
}

Write-Host "`nAll certificates created in: $OutputDir" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor White
Write-Host "  1. Upload each .pfx to Jenkins as a Secret file credential" -ForegroundColor White
Write-Host "  2. Update DataProtection__CertificatePassword in each .env file" -ForegroundColor White
Write-Host "  3. Do NOT commit the certs or certs/ directory to git" -ForegroundColor White
