# Data Protection Key Encryption

ASP.NET Core Data Protection keys are stored in the `DataProtectionKeys` table in PostgreSQL. These keys are encrypted at rest using an X.509 certificate.

## How It Works

- The app loads a `.pfx` certificate at startup using two configuration values:
  - `DataProtection:CertificatePath` — absolute path to the `.pfx` file
  - `DataProtection:CertificatePassword` — password for the `.pfx` file
- In **Development**, the certificate is optional (keys are stored unencrypted if not configured)
- In **all other environments**, both values are **required** — the app will fail to start without them

## Configuration by Environment

| Environment | CertificatePath source | CertificatePassword source |
|---|---|---|
| Local dev (Windows) | `dotnet user-secrets` | `dotnet user-secrets` |
| Docker (test/staging/prod) | Environment variable | Environment variable |
| Jenkins CI/CD | Jenkins credentials → env var | Jenkins credentials → env var |

## Generating a Certificate

### For Local Development (Windows)

Open PowerShell and run:

```powershell
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
```

Then configure user secrets:

```powershell
cd src/FamilyHQ.WebApi

dotnet user-secrets set "DataProtection:CertificatePath" "C:\Users\<YourUsername>\.familyhq\certs\dp-dev.pfx"
dotnet user-secrets set "DataProtection:CertificatePassword" "YourDevPasswordHere"
```

### For Linux / Docker Environments

On the host machine (or in a CI step), generate the certificate using OpenSSL:

```bash
# Create directory
mkdir -p /opt/familyhq/certs

# Generate a self-signed certificate (10-year expiry) and export as PFX
openssl req -x509 -newkey rsa:2048 -keyout /tmp/dp-key.pem -out /tmp/dp-cert.pem \
    -days 3650 -nodes -subj "/CN=FamilyHQ-DataProtection"

openssl pkcs12 -export -out /opt/familyhq/certs/dp.pfx \
    -inkey /tmp/dp-key.pem -in /tmp/dp-cert.pem \
    -passout pass:YourStrongPasswordHere

# Clean up temp files
rm /tmp/dp-key.pem /tmp/dp-cert.pem

# Restrict permissions
chmod 600 /opt/familyhq/certs/dp.pfx
```

### Docker Compose Configuration

Mount the certificate into the container and pass the password via environment variable:

```yaml
services:
  webapi:
    image: familyhq-webapi
    volumes:
      - /opt/familyhq/certs:/certs:ro
    environment:
      - DataProtection__CertificatePath=/certs/dp.pfx
      - DataProtection__CertificatePassword=YourStrongPasswordHere
```

**Note**: Use `__` (double underscore) in environment variable names — ASP.NET Core translates this to `:` for hierarchical configuration.

### Jenkins Pipeline

Store the certificate password in Jenkins Credentials (type: Secret text), then inject it:

```groovy
pipeline {
    environment {
        DP_CERT_PASSWORD = credentials('familyhq-dp-cert-password')
    }
    stages {
        stage('Deploy') {
            steps {
                sh """
                    docker run -d \
                        -v /opt/familyhq/certs:/certs:ro \
                        -e DataProtection__CertificatePath=/certs/dp.pfx \
                        -e DataProtection__CertificatePassword=${DP_CERT_PASSWORD} \
                        familyhq-webapi
                """
            }
        }
    }
}
```

## After Enabling Encryption

Once you configure the certificate and restart the app:

1. **Delete existing rows** from the `DataProtectionKeys` table — these contain unencrypted keys
2. **Delete existing rows** from the `UserTokens` table — these were encrypted with the old (unencrypted) keys and cannot be decrypted with the new key ring
3. Restart the application — new encrypted keys will be generated automatically
4. Users will need to re-authenticate to get new OAuth tokens

```sql
-- Run against your FamilyHQ database
TRUNCATE TABLE "DataProtectionKeys";
TRUNCATE TABLE "UserTokens";
```

## Certificate Rotation

When a certificate is approaching expiry:

1. Generate a new certificate using the steps above
2. Update the configuration to point to the new certificate
3. The existing keys encrypted with the old certificate will still need to be readable — you have two options:
   - **Option A**: Delete old keys and let them regenerate (users re-authenticate)
   - **Option B**: Add `.UnprotectKeysWithAnyCertificate(oldCert, ...)` to `Program.cs` temporarily to decrypt old keys, then remove after the old keys expire (90 days by default)

## Each Environment Gets Its Own Certificate

Each environment (dev, test/staging, production) should have its own independently generated certificate. This ensures:

- A compromised dev certificate cannot decrypt production data
- Certificate rotation can be done per environment without affecting others
- Keys encrypted in one environment cannot be used in another
