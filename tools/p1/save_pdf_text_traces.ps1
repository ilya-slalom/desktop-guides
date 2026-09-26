param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..'))
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$project = 'tools\p1\PdfTextSpike\PdfTextSpike.csproj'
$fixtures = @(
    @{ Id = 'pdf-access'; Path = 'tests\fixtures\p0\pdf-access.pdf' }
    @{ Id = 'pdf-scan'; Path = 'tests\fixtures\p0\pdf-scan.pdf' }
    @{ Id = 'pdf-locked'; Path = 'tests\fixtures\p0\pdf-locked.pdf'; Password = 'guide' }
    @{ Id = 'pdf-long'; Path = 'tests\fixtures\p0\generated\pdf-long.pdf' }
)
foreach ($fixture in $fixtures) {
    $inputArgs = @($fixture.Path)
    if ($fixture.ContainsKey('Password')) {
        $inputArgs += $fixture.Password
    }
    $output = & dotnet run --project $project -c Release --no-restore -- @inputArgs
    if ($LASTEXITCODE -ne 0) {
        throw "PdfTextSpike failed for $($fixture.Id)."
    }
    $json = ($output | Out-String) | ConvertFrom-Json
    $result = Join-Path $OutputDirectory ($fixture.Id + '-text.json')
    $json | ConvertTo-Json -Depth 6 | Set-Content $result -Encoding UTF8
}
