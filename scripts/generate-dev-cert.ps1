# Create a directory for certs (outside the repo)
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\.familyhq\certs"

# Generate a self-signed certificate with a 10-year expiry
$cert = New-SelfSignedCertificate `
    -Subject "CN=FamilyHQ-DataProtection-Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeySpec KeyExchange `
    -KeyLength 2048 `
    -NotAfter (Get-Date).AddYears(10)

# Export to PFX with a password
$password = ConvertTo-SecureString -String "YourDevPasswordHere" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$env:USERPROFILE\.familyhq\certs\dp-dev.pfx" -Password $password

# Clean up from certificate store (the PFX file is self-contained)
Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)"

Write-Host "Certificate created at: $env:USERPROFILE\.familyhq\certs\dp-dev.pfx"