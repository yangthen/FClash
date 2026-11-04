param(
    [Parameter(Mandatory = $true)][string]$Scenario,
    [int]$Samples = 30,
    [int]$IntervalSeconds = 1,
    [int]$WarmupSeconds = 5,
    [int[]]$ProcessIds = @(),
    [string]$OutputPath = (Join-Path $env:TEMP 'FClash-memory.csv')
)
$ErrorActionPreference = 'Stop'
if ($WarmupSeconds -gt 0) { Start-Sleep -Seconds $WarmupSeconds }
$rows = for ($sample = 0; $sample -lt $Samples; $sample++) {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -in @('FClash', 'FlClash', 'FlClashCore', 'FlClashHelperService') } |
        Where-Object { $ProcessIds.Count -eq 0 -or $_.Id -in $ProcessIds } |
        ForEach-Object {
            [pscustomobject]@{
                Timestamp = (Get-Date).ToUniversalTime().ToString('o')
                Scenario = $Scenario
                ProcessId = $_.Id
                ProcessName = $_.ProcessName
                WorkingSetBytes = $_.WorkingSet64
                PrivateBytes = $_.PrivateMemorySize64
                CpuSeconds = $_.CPU
            }
        }
    if ($sample + 1 -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
}
$rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding utf8 -Append
Write-Output $OutputPath
