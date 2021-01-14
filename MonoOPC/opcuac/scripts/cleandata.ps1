$ErrorActionPreference = "Stop"
$sleep_delay = 60

# $root_path = Split-Path $PSScriptRoot
# $data_path = Join-Path -Path $root_path  -ChildPath "data"
$data_path = "C:\gitprj\mopcua\MonoOPC\opcuac\bin\Debug\data"
Write-Host $data_path
while($true)
{
    $logs = @(Get-Item  (Join-Path -Path $data_path -ChildPath "*.txt") | Sort-Object)
    if ($logs.Length -gt 10) 
    {
        Write-Host Deleting : $logs[0].FullName
        Remove-Item $logs[0].FullName
    } else {
        Write-Host  [$(Get-Date)] - Sleeping for $sleep_delay seconds
        Start-Sleep -Seconds $sleep_delay
    }
}