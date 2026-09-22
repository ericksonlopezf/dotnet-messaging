# Copyright © Erickson Lopez. MIT License.
param(
    [string]$ProjectPath = "tests/EricksonLopez.Messaging.Tests/EricksonLopez.Messaging.Tests.csproj",
    [string]$TargetDir = "coverage-report",
    [string]$AssemblyFilter = "+EricksonLopez.Messaging",
    [string]$ClassFilter = "-*JsonSourceGenerator*"
)

$ErrorActionPreference = "Stop"

# Clean old TestResults in the test project directory
$testDir = Split-Path -Parent $ProjectPath
$testResultsDir = Join-Path $testDir "TestResults"
if (Test-Path $testResultsDir) {
    Remove-Item -Path $testResultsDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Running tests with coverage for $ProjectPath..."
dotnet test $ProjectPath --collect:"XPlat Code Coverage" --results-directory $testResultsDir

$coverageFile = Get-ChildItem -Path $testResultsDir -Recurse -Filter "coverage.cobertura.xml" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $coverageFile) {
    Write-Error "No coverage.cobertura.xml found in $testResultsDir"
    exit 1
}

Write-Host "Found coverage file: $($coverageFile.FullName)"
Write-Host "Generating ReportGenerator summary..."
reportgenerator "-reports:$($coverageFile.FullName)" "-targetdir:$TargetDir" "-assemblyfilters:$AssemblyFilter" "-classfilters:$ClassFilter" "-reporttypes:TextSummary;Html"

if (Test-Path "$TargetDir/Summary.txt") {
    Get-Content "$TargetDir/Summary.txt"
}
