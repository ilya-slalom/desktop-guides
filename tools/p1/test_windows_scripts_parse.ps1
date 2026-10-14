$ErrorActionPreference = 'Stop'
# Every tools/p1 script parses, and no if-chain branch sits after its else:
# PowerShell reads such an elseif as a command named 'elseif', so a mode in
# it silently runs the else branch instead.
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref] $tokens, [ref] $errors)
    foreach ($parseError in $errors) {
        $failures.Add("$($file.Name):$($parseError.Extent.StartLineNumber): $($parseError.Message)")
    }
    $stray = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -in @('elseif', 'else')
    }, $true)
    # Names are case-insensitive: assigning $lane to a validated -Lane
    # parameter fails at run time with "cannot be validated".
    $validated = @()
    if ($ast.ParamBlock) {
        $validated = @($ast.ParamBlock.Parameters | Where-Object {
            $_.Attributes | Where-Object { $_.TypeName.Name -like 'Validate*' }
        } | ForEach-Object { $_.Name.VariablePath.UserPath })
    }
    if ($validated.Count -gt 0) {
        $assignments = $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst]
        }, $true)
        foreach ($assignment in $assignments) {
            $name = $assignment.Left.VariablePath.UserPath
            if ($validated | Where-Object { [string]::Equals($_, $name, [StringComparison]::OrdinalIgnoreCase) }) {
                $failures.Add("$($file.Name):$($assignment.Extent.StartLineNumber): assigns `$$name, a validated parameter")
            }
        }
    }
    foreach ($command in $stray) {
        $failures.Add("$($file.Name):$($command.Extent.StartLineNumber): '$($command.GetCommandName())' after the end of its if-chain")
    }
}
if ($failures.Count -gt 0) { throw ($failures -join "`n") }
Write-Output 'windows_scripts_parse: all scripts passed.'
