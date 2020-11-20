
param(
    [string] $SubscriptionName = "Compliance_Tools_Eng",
    [string] $VaultName = "sace-kv",
    [string] $SpName = "sace-keyvault-reader"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function GetUserHomeFolder() {
    $isUnix = $PSVersionTable.Contains("Platform") -and ($PSVersionTable.Platform -eq "Unix")
    $isMac = $PSVersionTable.Contains("Platform") -and ($PSVersionTable.OS.Contains("Darwin"))
    if ($isMac -or $isUnix) {
        return $env:HOME
    }
    else {
        return $env:USERPROFILE
    }
}

function LoginToAzure() {
    param([string] $SubscriptionName)

    $azAccount = az account show | ConvertFrom-Json
    if ($null -eq $azAccount -or $azAccount.name -ine $SubscriptionName) {
        az login | Out-Null
        az account set --subscription $SubscriptionName | Out-Null
    }

    $currentAccount = az account show | ConvertFrom-Json
    return $currentAccount
}

function EnsureCertificateInKeyVault {
    param(
        [string] $VaultName,
        [string] $CertName
    )

    $existingCert = az keyvault certificate list --vault-name $VaultName --query "[?contains(id, '$CertName')]" | ConvertFrom-Json
    if (!$existingCert) {
        $credentialFolder = Join-Path $(GetUserHomeFolder) ".secrets"
        if (-not (Test-Path $credentialFolder)) {
            New-Item -Path $credentialFolder -ItemType Directory -Force | Out-Null
        }
        $defaultPolicyFile = Join-Path $credentialFolder "default_policy.json"
        az keyvault certificate get-default-policy -o json | Out-File $defaultPolicyFile -Encoding utf8
        az keyvault certificate create -n $CertName --vault-name $VaultName -p @$defaultPolicyFile | Out-Null
    }
}

function DownloadCertFromKeyVault {
    param(
        [string] $VaultName,
        [string] $CertName
    )

    $credentialFolder = Join-Path $(GetUserHomeFolder) ".secrets"
    if (-not (Test-Path $credentialFolder)) {
        New-Item -Path $credentialFolder -ItemType Directory -Force | Out-Null
    }
    $pfxCertFile = Join-Path $credentialFolder "$certName.pfx"
    $pemCertFile = Join-Path $credentialFolder "$certName.pem"
    $keyCertFile = Join-Path $credentialFolder "$certName.key"

    az keyvault secret download --vault-name $VaultName -n $CertName -e base64 -f $pfxCertFile
    openssl pkcs12 -in $pfxCertFile -clcerts -nodes -out $keyCertFile -passin pass:
    openssl rsa -in $keyCertFile -out $pemCertFile | Out-Null
}

function EnsureServicePrincipal() {
    param(
        [string] $SpName,
        [string] $VaultName
    )

    [array]$spArray = az ad sp list --display-name $SpName | ConvertFrom-Json
    if ($null -eq $spArray -or $spArray.Count -eq 0) {
        $certName = "$SpName-cert"
        EnsureCertificateInKeyVault -VaultName $VaultName -CertName $certName

        az ad sp create-for-rbac -n $SpName --role contributor --keyvault $VaultName --cert $certName | Out-Null
        $sp = az ad sp list --display-name $SpName | ConvertFrom-Json

        az keyvault set-policy `
            --name $VaultName `
            --object-id $sp.objectId `
            --spn $sp.displayName `
            --certificate-permissions get list `
            --secret-permissions get list | Out-Null
    }

    $sp = az ad sp list --display-name $SpName | ConvertFrom-Json
    return $sp
}

function InstallCert() {
    param(
        [string] $VaultName,
        [string] $CertName
    )

    $credentialFolder = Join-Path $(GetUserHomeFolder) ".secrets"
    if (-not (Test-Path $credentialFolder)) {
        New-Item -Path $credentialFolder -ItemType Directory -Force | Out-Null
    }
    $pfxCertFile = Join-Path $credentialFolder "$certName.pfx"
    if (Test-Path $pfxCertFile) {
        Remove-Item $pfxCertFile -Force | Out-Null
    }

    az keyvault secret download --vault-name $VaultName -n $CertName -e base64 -f $pfxCertFile
    $pfxBytes = [System.IO.File]::ReadAllBytes($pfxCertFile)
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet -bxor [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
    $pfx = new-object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $pfxCertFile

    return $pfx.Thumbprint
}

LoginToAzure -SubscriptionName $SubscriptionName | Out-Null
$sp = EnsureServicePrincipal -SpName $SpName -VaultName $VaultName
$thumbprint = InstallCert -VaultName $VaultName -CertName "$SpName-cert"

Write-Host "Copy thumbprint to appsettings.json file: `n" -ForegroundColor Yellow
$certFile = Join-Path (Join-Path $(GetUserHomeFolder) ".secrets") "$SpName-cert.pfx"
$settings = @{
    clientId             = $sp.appId
    clientCertThumbprint = $thumbprint
    clientCertFile       = $certFile
} | ConvertTo-Json

Write-Host $settings -ForegroundColor Green
Write-Host "`n"
