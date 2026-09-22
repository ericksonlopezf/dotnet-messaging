# Copyright © Erickson Lopez. MIT License.
param(
    [string]$TargetDir = "coverage-report-full"
)

$ErrorActionPreference = "Stop"

if (Test-Path $TargetDir) {
    Remove-Item -Path $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
}

$testConfigurations = @(
    @{ Proj = "tests/EricksonLopez.Messaging.Tests/EricksonLopez.Messaging.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging($|\.OpenTelemetry|\.Abstractions)' },
    @{ Proj = "tests/EricksonLopez.Messaging.Generators.Tests/EricksonLopez.Messaging.Generators.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.Generators' },
    @{ Proj = "tests/EricksonLopez.Messaging.Analyzers.Tests/EricksonLopez.Messaging.Analyzers.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.Analyzers' },
    @{ Proj = "tests/EricksonLopez.Messaging.AzureServiceBus.Tests/EricksonLopez.Messaging.AzureServiceBus.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.AzureServiceBus' },
    @{ Proj = "tests/EricksonLopez.Messaging.RabbitMQ.Tests/EricksonLopez.Messaging.RabbitMQ.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.RabbitMQ' },
    @{ Proj = "tests/EricksonLopez.Messaging.AwsSqs.Tests/EricksonLopez.Messaging.AwsSqs.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.AwsSqs' },
    @{ Proj = "tests/EricksonLopez.Messaging.Kafka.Tests/EricksonLopez.Messaging.Kafka.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.Kafka' },
    @{ Proj = "tests/EricksonLopez.Messaging.Events.Tests/EricksonLopez.Messaging.Events.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.Events' },
    @{ Proj = "tests/EricksonLopez.Messaging.Testing.Tests/EricksonLopez.Messaging.Testing.Tests.csproj"; Pattern = '^EricksonLopez\.Messaging\.Testing' }
)

$coverageFiles = @()

foreach ($cfg in $testConfigurations) {
    $proj = $cfg.Proj
    $pattern = $cfg.Pattern
    $testDir = Split-Path -Parent $proj
    $testResultsDir = Join-Path $testDir "TestResults"
    if (Test-Path $testResultsDir) {
        Remove-Item -Path $testResultsDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "Running tests with coverage for $proj (Pattern: $pattern)..."
    dotnet test $proj --collect:"XPlat Code Coverage" --results-directory $testResultsDir
    
    $covItem = Get-ChildItem -Path $testResultsDir -Recurse -Filter "coverage.cobertura.xml" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($covItem) {
        $cov = $covItem.FullName
        [xml]$xml = Get-Content $cov
        if ($xml.coverage -and $xml.coverage.packages -and $xml.coverage.packages.package) {
            $packages = @($xml.coverage.packages.package)
            foreach ($pkg in $packages) {
                if ($pkg.name -notmatch $pattern) {
                    $xml.coverage.packages.RemoveChild($pkg) | Out-Null
                }
            }
            $xml.Save($cov)
        }
        $coverageFiles += $cov
    }
}

$reportsArg = $coverageFiles -join ";"
Write-Host "Generating full ReportGenerator summary for all coverage files..."
reportgenerator "-reports:$reportsArg" "-targetdir:$TargetDir" "-assemblyfilters:+EricksonLopez.Messaging*;-*Tests;-EricksonLopez.Events*;-EricksonLopez.Result*" "-classfilters:-*JsonSourceGenerator*;-*MessagingJsonContext*" "-reporttypes:TextSummary;Html"

if (Test-Path "$TargetDir/Summary.txt") {
    Get-Content "$TargetDir/Summary.txt"
}
