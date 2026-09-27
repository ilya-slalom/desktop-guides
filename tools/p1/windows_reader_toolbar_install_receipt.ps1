function Test-ToolbarInstallReceipt(
    [string] $ReceiptPath,
    [string] $InvocationId,
    [string] $PackageFullName
) {
    if (-not (Test-Path -LiteralPath $ReceiptPath)) {
        return $false
    }
    try {
        $receipt = Get-Content -LiteralPath $ReceiptPath -Raw |
            ConvertFrom-Json
        return $receipt.invocationId -ceq $InvocationId -and
            $receipt.packageFullName -ceq $PackageFullName
    }
    catch {
        return $false
    }
}
