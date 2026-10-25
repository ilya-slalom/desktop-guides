param(
    # A new, empty folder for the PFX, its password and the public certificate.
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [string] $Subject = 'CN=359962D5-3801-484E-BE5B-39D0D498BEA4',
    [ValidateRange(1, 10)] [int] $Years = 5
)
# T17.1: creates the self-signed release certificate once. The PFX and its
# password go to the GitHub secrets and the owner's password manager; the
# certificate never stays in this machine's store. Prints no secret.
$ErrorActionPreference = 'Stop'
if ((Test-Path -LiteralPath $OutputDirectory) -and
    @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
    throw "Use a new, empty folder: $OutputDirectory"
}
$OutputDirectory = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$base = Join-Path $OutputDirectory 'desktop-guides-release'
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years) -CertStoreLocation Cert:\CurrentUser\My
try {
    $bytes = [byte[]]::new(32)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $password = [Convert]::ToBase64String($bytes)
    $secure = ConvertTo-SecureString -String $password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath "$base.pfx" -Password $secure `
        -CryptoAlgorithmOption AES256_SHA256 | Out-Null
    Export-Certificate -Cert $cert -FilePath "$base.cer" | Out-Null
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText("$base.password.txt", $password, $utf8)
    [IO.File]::WriteAllText("$base.pfx.base64.txt",
        [Convert]::ToBase64String([IO.File]::ReadAllBytes("$base.pfx")), $utf8)
}
finally {
    # -DeleteKey also removes the exportable private key from the user's key store.
    $storePath = "Cert:\CurrentUser\My\$($cert.Thumbprint)"
    Remove-Item -LiteralPath $storePath -DeleteKey -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $storePath) {
    throw "The certificate is still in the store: $storePath"
}
$quoted = $base.Replace("'", "''")
[ordered]@{
    subject = $cert.Subject
    thumbprint = $cert.Thumbprint
    notBefore = $cert.NotBefore.ToUniversalTime().ToString('o')
    notAfter = $cert.NotAfter.ToUniversalTime().ToString('o')
} | ConvertTo-Json -Compress
"Get-Content -Raw '$quoted.pfx.base64.txt' | gh secret set RELEASE_SIGNING_PFX --repo ilyalissoboi/desktop-guides"
"Get-Content -Raw '$quoted.password.txt' | gh secret set RELEASE_SIGNING_PASSWORD --repo ilyalissoboi/desktop-guides"
