# Enforces a minimum line-coverage threshold for a single package/module inside a
# Cobertura report produced by coverlet.collector's "XPlat Code Coverage" data collector.
#
# coverlet.collector does not support the <Threshold> runsettings option (that's a
# coverlet.msbuild-only feature), so the gate is implemented here instead of relying
# on the collector to fail the run itself.
param(
    [Parameter(Mandatory)] [string]$CoverageDirectory,
    [Parameter(Mandatory)] [string]$PackageName,
    [Parameter(Mandatory)] [double]$Threshold
)

$ErrorActionPreference = "Stop"

$reportFile = Get-ChildItem -Path $CoverageDirectory -Filter "coverage.cobertura.xml" -Recurse |
    Select-Object -First 1

if (-not $reportFile) {
    Write-Error "No coverage.cobertura.xml found under '$CoverageDirectory'."
    exit 1
}

[xml]$xml = Get-Content -Path $reportFile.FullName
$package = $xml.coverage.packages.package | Where-Object { $_.name -eq $PackageName }

if (-not $package) {
    Write-Error "Package '$PackageName' was not found in '$($reportFile.FullName)'."
    exit 1
}

$lineRate = [double]$package.'line-rate' * 100
$rounded = [math]::Round($lineRate, 2)

Write-Host "Coverage for '$PackageName': $rounded% (threshold: $Threshold%)"

if ($lineRate -lt $Threshold) {
    Write-Error "Coverage $rounded% for '$PackageName' is below the required $Threshold% threshold."
    exit 1
}
