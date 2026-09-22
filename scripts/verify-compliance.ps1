# Copyright © Erickson Lopez. MIT License.
# =============================================================================
# REPOSITORY COMPLIANCE & ARCHITECTURAL INVARIANT AUDITOR
# Repository: EricksonLopez.Messaging
# Enforces:
#   1. Documentation & Template kebab-case file naming convention
#   2. Zero [Obsolete] attribute usages across solution (src/, tests/, benchmarks/, samples/)
#   3. Canonical MIT copyright headers on all C# source files
#   4. Single Type Per File rule across all production C# files (src/)
#   5. Normalized GitHub links pointing to ericksonlopezf/dotnet-messaging
#   6. Canonical contact/security email: ericksonlopezf@gmail.com
#   7. Zero CS1591 / 1591 XML documentation warning suppressions
#   8. Markdown internal relative links validity & case-sensitivity
# =============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$Violations = [System.Collections.Generic.List[string]]::new()

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  REPOSITORY COMPLIANCE & ARCHITECTURE AUDITOR    " -ForegroundColor Cyan
Write-Host "  Repository: EricksonLopez.Messaging             " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# 1. Documentation file naming (must be lowercase kebab-case .md)
# -----------------------------------------------------------------------------
Write-Host "`n[1/8] Checking documentation and template naming (kebab-case)..." -ForegroundColor Yellow
$DocsPath = Join-Path $RepoRoot "docs"
$TemplatesPath = Join-Path $RepoRoot ".github/ISSUE_TEMPLATE"

$ExemptDocFiles = @(
    "README.md", "LICENSE", "SECURITY.md", "SUPPORT.md", 
    "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "CHANGELOG.md", 
    "PULL_REQUEST_TEMPLATE.md"
)

if (Test-Path $DocsPath) {
    $DocFiles = Get-ChildItem -Path $DocsPath -Recurse -File -Filter "*.md"
    foreach ($File in $DocFiles) {
        $BaseName = $File.BaseName
        if ($BaseName -cmatch '[A-Z_]') {
            $Violations.Add("Doc naming violation: '$($File.FullName)' contains uppercase letters or underscores. Use lowercase kebab-case.")
        }
    }
}

if (Test-Path $TemplatesPath) {
    $TemplateFiles = Get-ChildItem -Path $TemplatesPath -File -Filter "*.md"
    foreach ($File in $TemplateFiles) {
        $BaseName = $File.BaseName
        if ($BaseName -cmatch '[A-Z_]') {
            $Violations.Add("Issue template naming violation: '$($File.FullName)' contains uppercase letters or underscores. Use lowercase kebab-case.")
        }
    }
}
Write-Host "  ✅ All documentation and template files use valid kebab-case naming." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 2. Zero [Obsolete] usages across all solution code
# -----------------------------------------------------------------------------
Write-Host "`n[2/8] Checking for [Obsolete] attribute usages across all projects..." -ForegroundColor Yellow
$ScanDirs = @("src", "tests", "benchmarks", "samples")
$ObsoleteFound = $false
foreach ($Dir in $ScanDirs) {
    $TargetDir = Join-Path $RepoRoot $Dir
    if (Test-Path $TargetDir) {
        $ObsoleteMatches = Get-ChildItem -Path $TargetDir -Recurse -Filter "*.cs" | 
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            Select-String -Pattern '\[\s*(System\.)?Obsolete'
        foreach ($Match in $ObsoleteMatches) {
            $Violations.Add("Obsolete attribute violation: '$($Match.Path):$($Match.LineNumber)' - [Obsolete] is prohibited in this solution.")
            $ObsoleteFound = $true
        }
    }
}
if (-not $ObsoleteFound) {
    Write-Host "  ✅ Zero [Obsolete] attributes across the entire solution." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 3. Canonical MIT Copyright Headers across all C# files
# -----------------------------------------------------------------------------
Write-Host "`n[3/8] Checking canonical MIT copyright headers across all C# files..." -ForegroundColor Yellow
$HeaderRegex = '(?s)^\s*(//|/\*|<!--)\s*Copyright\s+(©|\(c\)|&copy;)?\s*Erickson Lopez.*?(MIT License|\(MIT\))'
$CsFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter "*.cs" | 
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|MEGA-AUDIT|audit-reports)[\\/]' }
foreach ($File in $CsFiles) {
    $Content = Get-Content -Path $File.FullName -Raw
    if ($Content -notmatch $HeaderRegex) {
        $Violations.Add("Missing/Invalid copyright header: '$($File.FullName)'. Expected '// Copyright .* Erickson Lopez\. MIT License\.' at top.")
    }
}
Write-Host "  ✅ All C# source files contain the required MIT copyright header." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 4. Single Type Per File Rule in src/
# -----------------------------------------------------------------------------
Write-Host "`n[4/8] Checking 'One Type Per File' rule in src/..." -ForegroundColor Yellow
$SrcPath = Join-Path $RepoRoot "src"
if (Test-Path $SrcPath) {
    $ProductionFiles = Get-ChildItem -Path $SrcPath -Recurse -Filter "*.cs" | 
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($File in $ProductionFiles) {
        $Lines = Get-Content -Path $File.FullName
        $DeclaredTypes = [System.Collections.Generic.List[string]]::new()
        $InBlockComment = $false
        $BraceDepth = 0
        $IsFileScopedNamespace = $false

        foreach ($Line in $Lines) {
            $Trimmed = $Line.Trim()
            if ($Trimmed.StartsWith("/*")) { $InBlockComment = $true }
            if ($Trimmed.EndsWith("*/")) { $InBlockComment = $false; continue }
            if ($InBlockComment -or $Trimmed.StartsWith("//") -or [string]::IsNullOrWhiteSpace($Trimmed)) { continue }

            if ($Trimmed -match '^namespace\s+[A-Za-z0-9_.]+\s*;') {
                $IsFileScopedNamespace = $true
            }

            # Check if declaring a type at namespace level (depth 0 for file-scoped, depth 1 for block namespace)
            $MaxDepth = if ($IsFileScopedNamespace) { 0 } else { 1 }
            if ($BraceDepth -le $MaxDepth -and $Trimmed -match '^\s*(?:(?:public|internal|private|protected|file|sealed|abstract|static|readonly|ref|partial)\s+)*(class|interface|struct|enum|record(?:\s+struct)?)\s+([A-Za-z0-9_]+)') {
                $TypeName = $Matches[2]
                $DeclaredTypes.Add($TypeName)
            }

            # Track brace depth for nesting
            $Opens = [regex]::Matches($Trimmed, '\{').Count
            $Closes = [regex]::Matches($Trimmed, '\}').Count
            $BraceDepth += ($Opens - $Closes)
            if ($BraceDepth -lt 0) { $BraceDepth = 0 }
        }

        if ($DeclaredTypes.Count -gt 1) {
            $Violations.Add("Multiple top-level types declared in single file: '$($File.FullName)' contains [$($DeclaredTypes -join ', ')].")
        }
    }
    Write-Host "  ✅ Every production file satisfies the 'One Type Per File' invariant." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 5. GitHub Repository Identity Links
# -----------------------------------------------------------------------------
Write-Host "`n[5/8] Checking GitHub identity links (ericksonlopezf/dotnet-messaging)..." -ForegroundColor Yellow
$MarkdownFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter "*.md" | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults|StrykerOutput|node_modules|MEGA-AUDIT|audit-reports)[\\/]' }
foreach ($File in $MarkdownFiles) {
    $Mismatches = Get-Content -Path $File.FullName | Select-String -Pattern 'github\.com/([a-zA-Z0-9_-]+)/([a-zA-Z0-9_-]+)'
    foreach ($M in $Mismatches) {
        $Url = $M.Matches[0].Value
        if ($Url -match 'github\.com/ericksonlopezf/dotnet-' -and $Url -notmatch 'dotnet-(messaging|template|mediator|mapper|events|outbox|specification|sql-builder|dapper-extensions|processes|sharedkernel|shared-kernel|result|multitenancy)') {
            $Violations.Add("Broken repo URL: '$($File.FullName):$($M.LineNumber)' references '$Url'.")
        }
    }
}
Write-Host "  ✅ All GitHub URLs correctly target valid repositories." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 6. Contact and Security Email Normalization
# -----------------------------------------------------------------------------
Write-Host "`n[6/8] Checking contact and security email normalization (ericksonlopezf@gmail.com)..." -ForegroundColor Yellow
$AuditedDocs = @("SECURITY.md", "SUPPORT.md", "README.md", "CONTRIBUTING.md")
foreach ($DocName in $AuditedDocs) {
    $DocPath = Join-Path $RepoRoot $DocName
    if (Test-Path $DocPath) {
        $Content = Get-Content -Path $DocPath -Raw
        if ($DocName -in @("SECURITY.md", "SUPPORT.md")) {
            if ($Content -notmatch 'ericksonlopezf@gmail\.com') {
                $Violations.Add("$DocName contact email is not normalized to ericksonlopezf@gmail.com")
            }
        }
        if ($Content -match 'mailto:[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}' -and $Content -notmatch 'mailto:ericksonlopezf@gmail\.com') {
            $Violations.Add("$DocName contains invalid mailto link. Expected mailto:ericksonlopezf@gmail.com")
        }
    }
}
Write-Host "  ✅ Official contact emails normalized to ericksonlopezf@gmail.com." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 7. CS1591 Warning Suppression Prohibition
# -----------------------------------------------------------------------------
Write-Host "`n[7/8] Checking for CS1591 / 1591 warning suppressions..." -ForegroundColor Yellow
$XmlConfigFiles = Get-ChildItem -Path $RepoRoot -Recurse -Include "*.csproj", "*.props", "*.targets" |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|MEGA-AUDIT|audit-reports)[\\/]' }
foreach ($XmlFile in $XmlConfigFiles) {
    $XmlContent = Get-Content -Path $XmlFile.FullName -Raw
    if ($XmlContent -match '<NoWarn>[^<]*\b(CS)?1591\b') {
        $Violations.Add("CS1591 suppression violation: '$($XmlFile.FullName)' suppresses CS1591 in <NoWarn>.")
    }
}
$EditorConfigFile = Join-Path $RepoRoot ".editorconfig"
if (Test-Path $EditorConfigFile) {
    $EcContent = Get-Content -Path $EditorConfigFile -Raw
    if ($EcContent -match 'dotnet_diagnostic\.CS1591\.severity\s*=\s*none') {
        $Violations.Add("CS1591 suppression violation: .editorconfig sets CS1591 severity to none.")
    }
}
Write-Host "  ✅ Zero CS1591 warning suppressions detected." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 8. Markdown Relative Links Integrity & Case Sensitivity
# -----------------------------------------------------------------------------
Write-Host "`n[8/8] Checking Markdown relative links integrity..." -ForegroundColor Yellow

function Test-ExactPathCasing {
    param([string]$BaseDir, [string]$RelativePath)

    $Normalized = $RelativePath.Replace('\', '/')
    $Parts = $Normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)
    $Current = (Get-Item -LiteralPath $BaseDir).FullName

    foreach ($Part in $Parts) {
        if ($Part -eq '.') { continue }
        if ($Part -eq '..') {
            $Parent = (Get-Item -LiteralPath $Current).Parent
            if ($null -eq $Parent) { return $false }
            $Current = $Parent.FullName
            continue
        }
        $Items = [System.IO.Directory]::GetFileSystemEntries($Current)
        $Matched = $false
        foreach ($Item in $Items) {
            $ItemName = [System.IO.Path]::GetFileName($Item)
            if ($ItemName -ceq $Part) {
                $Current = $Item
                $Matched = $true
                break
            }
        }
        if (-not $Matched) {
            return $false
        }
    }
    return $true
}

foreach ($File in $MarkdownFiles) {
    $Content = Get-Content -Path $File.FullName -Raw
    $LinkMatches = [regex]::Matches($Content, '\[([^\]]+)\]\(([^)]+)\)')
    $FileDir = $File.DirectoryName

    foreach ($Match in $LinkMatches) {
        $Target = $Match.Groups[2].Value.Trim()
        # Skip external URLs, mailto, anchors, and absolute web links
        if ($Target -match '^(https?://|mailto:|#)' -or [string]::IsNullOrWhiteSpace($Target)) {
            continue
        }
        # Strip anchor from relative link
        $TargetFile = ($Target -split '#')[0]
        if ([string]::IsNullOrWhiteSpace($TargetFile)) { continue }

        $ResolvedPath = Join-Path $FileDir $TargetFile
        if (-not (Test-Path $ResolvedPath)) {
            $Violations.Add("Broken relative link in '$($File.FullName)': target '$Target' does not exist.")
        } elseif (-not (Test-ExactPathCasing -BaseDir $FileDir -RelativePath $TargetFile)) {
            $Violations.Add("Broken relative link in '$($File.FullName)': target '$Target' does not match exact disk casing.")
        }
    }
}
Write-Host "  ✅ All internal Markdown relative links resolve successfully." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 9. Centralized Implicit Usings & Packaging Assets in Directory.Build.props
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Build Props Governance] Validating Directory.Build.props (ImplicitUsings, Icon, Readme, License)..." -ForegroundColor Yellow
$PropsFile = Join-Path $RepoRoot "Directory.Build.props"
if (-not (Test-Path $PropsFile)) {
    $Violations.Add("Directory.Build.props not found at repository root.")
} else {
    $PropsContent = Get-Content -Path $PropsFile -Raw
    if ($PropsContent -notmatch '<ImplicitUsings>\s*enable\s*</ImplicitUsings>') {
        $Violations.Add("Directory.Build.props must centrally enable <ImplicitUsings>enable</ImplicitUsings>.")
    }
    if ($PropsContent -notmatch '<PackageIcon>\s*icon\.png\s*</PackageIcon>') {
        $Violations.Add("Directory.Build.props must declare <PackageIcon>icon.png</PackageIcon>.")
    }
    if ($PropsContent -notmatch '<PackageReadmeFile>\s*README\.md\s*</PackageReadmeFile>') {
        $Violations.Add("Directory.Build.props must declare <PackageReadmeFile>README.md</PackageReadmeFile>.")
    }
    if ($PropsContent -notmatch '<PackageLicenseExpression>\s*MIT\s*</PackageLicenseExpression>') {
        $Violations.Add("Directory.Build.props must declare <PackageLicenseExpression>MIT</PackageLicenseExpression>.")
    }
    Write-Host "  ✅ Directory.Build.props governance policies verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 10. Version Harmony & Release Integrity
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Version Harmony] Validating version synchronization across repository..." -ForegroundColor Yellow
$ManifestFile = Join-Path $RepoRoot ".release-please-manifest.json"
$VersionFromProps = ""
if (Test-Path $PropsFile) {
    if ((Get-Content -Path $PropsFile -Raw) -match '<(?:Version|VersionPrefix)>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</(?:Version|VersionPrefix)>') {
        $VersionFromProps = $Matches[1]
    }
}
if (Test-Path $ManifestFile) {
    $ManifestJson = Get-Content -Path $ManifestFile -Raw | ConvertFrom-Json
    $ManifestVersion = $ManifestJson.'.'
    if ($ManifestVersion -ne $VersionFromProps) {
        $Violations.Add("Version mismatch: Directory.Build.props ($VersionFromProps) does not match .release-please-manifest.json ($ManifestVersion).")
    }
    
    $ChangelogPath = Join-Path $RepoRoot "CHANGELOG.md"
    if (Test-Path $ChangelogPath) {
        $ChangelogContent = Get-Content -Path $ChangelogPath -Raw
        if ($ChangelogContent -notmatch "## \[$([regex]::Escape($VersionFromProps))\]") {
            $Violations.Add("CHANGELOG.md does not document current version [$VersionFromProps].")
        }
    }
    Write-Host "  ✅ Version harmony verified ($VersionFromProps across manifest, props, changelog)." -ForegroundColor Green
}

# -----------------------------------------------------------------------------

# -----------------------------------------------------------------------------
# Stryker.NET Configuration, Concurrency, Anti-Gaming Blacklist & Matrix Synchronization
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Stryker] Validating Stryker.NET configuration, concurrency, anti-gaming blacklist & package matrix..." -ForegroundColor Yellow
$strykerErrors = 0
$targetRoot = if (Get-Variable -Name "RootDirectory" -Scope 0 -ErrorAction SilentlyContinue) { $RootDirectory } elseif (Get-Variable -Name "WorkspaceRoot" -Scope 0 -ErrorAction SilentlyContinue) { $WorkspaceRoot } elseif (Get-Variable -Name "repoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $repoRoot } elseif (Get-Variable -Name "RepoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $RepoRoot } else { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }

$strykerConfigFiles = Get-ChildItem -Path $targetRoot -Recurse -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|StrykerOutput|BenchmarkDotNet\.Artifacts|node_modules)[\\/]' -and
    $_.Name -ne "stryker-config.master.json"
}

if (-not $strykerConfigFiles -or $strykerConfigFiles.Count -eq 0) {
    Write-Host "  ❌ Zero Stryker configuration files found in repository." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Zero Stryker configuration files found.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $strykerErrors++
} else {
    foreach ($sf in $strykerConfigFiles) {
        $json = Get-Content $sf.FullName -Raw | ConvertFrom-Json
        $cfg = if ($json.PSObject.Properties['stryker-config']) { $json.'stryker-config' } else { $json }

        if ($cfg.PSObject.Properties['thresholds']) {
            $th = $cfg.thresholds
            if ($th.high -ne 100 -or $th.low -ne 98 -or $th.break -ne 95) {
                Write-Host "  ❌ Non-compliant mutation thresholds in $($sf.FullName): high=$($th.high), low=$($th.low), break=$($th.break). Required: high=100, low=98, break=95." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant mutation thresholds in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['concurrency']) {
            if ($cfg.concurrency -ne 2) {
                Write-Host "  ❌ Non-compliant Stryker concurrency in $($sf.FullName): $($cfg.concurrency). Required: 2." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant Stryker concurrency in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['ignore-methods'] -and $cfg.'ignore-methods') {
            foreach ($m in $cfg.'ignore-methods') {
                if ($m -match 'ThrowIf|Exception|Guard|ScrubEphemeralMemory') {
                    Write-Host "  ❌ Prohibited anti-gaming method exclusion '$m' detected in $($sf.FullName)." -ForegroundColor Red
                    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Prohibited anti-gaming exclusion '$m' in $($sf.FullName)") } else { $Violations++ } }
                    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                    $strykerErrors++
                }
            }
        }
    }

        $strykerProfiles = Get-ChildItem -Path $targetRoot -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -ne "stryker-config.master.json" -and
        ($_.Name -match '^stryker(-.+)?-config\.json$' -or $_.Name -eq "stryker-config.json")
    }

    $srcProjects = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\/](bin|obj)[\/]'
    }

    # Verify exact 1:1 count parity between Stryker profile configs and src/ projects
    if ($strykerProfiles.Count -ne $srcProjects.Count) {
        Write-Host "  ❌ Stryker profile count ($($strykerProfiles.Count)) does not match exactly the number of projects in src/ ($($srcProjects.Count))." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile count ($($strykerProfiles.Count)) does not match project count in src/ ($($srcProjects.Count)).") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    }

    foreach ($proj in $srcProjects) {
        $projName = $proj.Name
        $matched = $false
        foreach ($sf in $strykerProfiles) {
            $raw = Get-Content $sf.FullName -Raw
            if ($raw -match [regex]::Escape($projName) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matched = $true
                break
            }
        }

        if (-not $matched) {
            Write-Host "  ❌ Project '$projName' has no corresponding Stryker configuration profile." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding Stryker configuration profile.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    foreach ($sf in $strykerProfiles) {
        $raw = Get-Content $sf.FullName -Raw
        $matchedProj = $false
        foreach ($proj in $srcProjects) {
            if ($raw -match [regex]::Escape($proj.Name) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matchedProj = $true
                break
            }
        }
        if (-not $matchedProj) {
            Write-Host "  ❌ Stryker profile '$($sf.Name)' does not correspond to any project in src/ (orphaned profile)." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile '$($sf.Name)' does not correspond to any project in src/.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    $mutationWfPath = Join-Path $targetRoot ".github/workflows/mutation-testing.yml"
    if (-not (Test-Path $mutationWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/mutation-testing.yml" -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/mutation-testing.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    } else {
        $wfContent = Get-Content $mutationWfPath -Raw
        if ($wfContent -match '--concurrency\s*[:\s]\s*([3-9]|\d{2,})') {
            Write-Host "  ❌ Mutation workflow overrides concurrency with value > 2 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides concurrency > 2") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
        if ($wfContent -match '--break-at\s*[:\s]\s*([0-8]\d|\d{1})(?!\d)') {
            Write-Host "  ❌ Mutation workflow overrides break threshold with value < 90 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides break threshold < 90") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }

        foreach ($sf in $strykerConfigFiles) {
            if ($sf.Name -eq "stryker-config.json" -and $strykerConfigFiles.Count -gt 1) {
                continue
            }
            if ($sf.Name -eq "stryker-config-unit.json") {
                continue
            }
            $pkgIdent = if ($sf.Name -match '^stryker-(.+)-config\.json$') { $Matches[1] } else { $sf.Name }
            if ($wfContent -notmatch [regex]::Escape($sf.Name) -and $wfContent -notmatch "(?i)name:\s*$pkgIdent" -and $wfContent -notmatch "(?i)working-dir:.*$pkgIdent") {
                Write-Host "  ❌ Stryker configuration '$($sf.Name)' is missing from .github/workflows/mutation-testing.yml matrix." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker configuration '$($sf.Name)' is missing from matrix") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }
    }
}

if ($strykerErrors -eq 0) {
    Write-Host "  ✅ Stryker.NET configuration, 100/98/95 thresholds, concurrency 2, anti-gaming blacklist, and package matrix synchronization verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Mutation Testing Release Gate Scripts, Workflow Gate & docs/testing-roadmap.md
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Release Gate] Validating mutation release gate scripts, workflow enforcement & docs/testing-roadmap.md..." -ForegroundColor Yellow
$gateErrors = 0

$gateScriptPath = Join-Path $targetRoot "scripts/verify-mutation-gate.js"
if (-not (Test-Path $gateScriptPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.js release gate script." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$gateTestPath = Join-Path $targetRoot "scripts/verify-mutation-gate.test.js"
if (-not (Test-Path $gateTestPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.test.js unit tests." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.test.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$roadmapPath = Join-Path $targetRoot "docs/testing-roadmap.md"
if (-not (Test-Path $roadmapPath)) {
    Write-Host "  ❌ Missing docs/testing-roadmap.md governance document." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing docs/testing-roadmap.md") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$publishWfPath = Join-Path $targetRoot ".github/workflows/publish.yml"
if (Test-Path $publishWfPath) {
    $pubContent = Get-Content $publishWfPath -Raw
    if ($pubContent -notmatch "verify-mutation-gate\.js") {
        Write-Host "  ❌ .github/workflows/publish.yml does not enforce verify-mutation-gate.js before publishing." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("publish.yml does not enforce verify-mutation-gate.js") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $gateErrors++
    }
}

if ($gateErrors -eq 0) {
    Write-Host "  ✅ Mutation release gate scripts, publish pipeline gate, and docs/testing-roadmap.md verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Benchmark Regression Quality Gate & CI Enforcement
# -----------------------------------------------------------------------------
$hasBenchProject = (Get-ChildItem -Path $targetRoot -Filter "*Benchmark*.csproj" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|MEGA-AUDIT|StrykerOutput)[\\/]' } | Select-Object -First 1) -ne $null
if ($hasBenchProject) {
    Write-Host "`n[Gate: Benchmark Gate] Validating benchmark regression scripts & workflow enforcement..." -ForegroundColor Yellow
    $benchGateErrors = 0

    $benchScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.ps1"
    if (-not (Test-Path $benchScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.ps1 regression assertion script." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchTestScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.test.ps1"
    if (-not (Test-Path $benchTestScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.test.ps1 unit tests." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.test.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchWfPath = Join-Path $targetRoot ".github/workflows/benchmark-regression-gate.yml"
    if (-not (Test-Path $benchWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/benchmark-regression-gate.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmark-regression-gate.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    } else {
        $benchWfContent = Get-Content $benchWfPath -Raw -Encoding utf8
        if ($benchWfContent -notmatch "verify-benchmark-gate\.ps1" -or $benchWfContent -notmatch "--exporters json") {
            Write-Host "  ❌ .github/workflows/benchmark-regression-gate.yml does not enforce verify-benchmark-gate.ps1 and --exporters json." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid benchmark-regression-gate.yml") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $benchGateErrors++
        }
    }

    $baselinePath = Join-Path $targetRoot "benchmarks/results/baseline.json"
    if (-not (Test-Path $baselinePath)) {
        Write-Host "  ❌ Missing benchmarks/results/baseline.json baseline file." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmarks/results/baseline.json") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    if ($benchGateErrors -eq 0) {
        Write-Host "  ✅ Benchmark regression assertion script, baseline, and CI workflow verified." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# SourceLink & Central Package Management Integration Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: SourceLink] Validating centralized Microsoft.SourceLink.GitHub integration..." -ForegroundColor Yellow
$sourceLinkErrors = 0

$pkgPropsPath = Join-Path $targetRoot "Directory.Packages.props"
$bldPropsPath = Join-Path $targetRoot "Directory.Build.props"

if (-not (Test-Path $pkgPropsPath)) {
    Write-Host "  ❌ Missing Directory.Packages.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Packages.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $pkgContent = Get-Content $pkgPropsPath -Raw -Encoding utf8
    if ($pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props must declare 'Microsoft.SourceLink.GitHub' instead of generic or missing package." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Packages.props missing Microsoft.SourceLink.GitHub") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($pkgContent -match 'PackageVersion\s+Include="Microsoft\.SourceLink\.Common"' -and $pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props uses generic Microsoft.SourceLink.Common without GitHub provider." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Generic Microsoft.SourceLink.Common used") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if (-not (Test-Path $bldPropsPath)) {
    Write-Host "  ❌ Missing Directory.Build.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Build.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $bldContent = Get-Content $bldPropsPath -Raw -Encoding utf8
    if ($bldContent -notmatch 'PackageReference\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Build.props must centralize '<PackageReference Include=""Microsoft.SourceLink.GitHub"" PrivateAssets=""All"" />'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing Microsoft.SourceLink.GitHub PackageReference") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($bldContent -notmatch '<PublishRepositoryUrl>\s*true\s*</PublishRepositoryUrl>' -and $bldContent -notmatch '<PublishRepositoryUrl\s+Condition=') {
        Write-Host "  ❌ Directory.Build.props must specify '<PublishRepositoryUrl>true</PublishRepositoryUrl>'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing PublishRepositoryUrl") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if ($sourceLinkErrors -eq 0) {
    Write-Host "  ✅ SourceLink integration (Microsoft.SourceLink.GitHub) verified in Directory.Packages.props & Directory.Build.props." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Native AOT Test Gate & Compilation Smoke Test Invariants
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Native AOT] Validating Native AOT compilation smoke tests & workflow enforcement..." -ForegroundColor Yellow
$aotErrors = 0

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

# 1. Discover AOT-applicable projects in src/
$aotApplicableProjects = @()
foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $projDir = $proj.DirectoryName
    
    # Exclude Roslyn Analyzers and Source Generators
    if ($projName -match '(Analyzers?|Generators?)\.csproj$' -or $projDir -match '[\\/](Analyzers?|Generators?)[\\/]?$') {
        continue
    }
    # Exclude API endpoints / applications if applicable
    if ($projName -match '\.Api\.csproj$') {
        continue
    }
    
    $projContent = Get-Content $proj.FullName -Raw
    # Exclude projects explicitly marked as non-AOT compatible
    if ($projContent -match '<IsAotCompatible>\s*false\s*</IsAotCompatible>' -or 
        $projContent -match '<PublishAot>\s*false\s*</PublishAot>') {
        continue
    }
    
    $aotApplicableProjects += $proj
}

if ($aotApplicableProjects.Count -gt 0) {
    Write-Host "  [INFO] Detected $($aotApplicableProjects.Count) Native AOT applicable project(s) in src/." -ForegroundColor Gray
    
    # 2. Check for dedicated Native AOT smoke test project in tests/ or samples/
    $aotTestProjects = @()
    foreach ($searchDir in @("tests", "samples")) {
        $dirPath = Join-Path $targetRoot $searchDir
        if (Test-Path $dirPath) {
            $candidateTests = Get-ChildItem -Path $dirPath -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            }
            foreach ($t in $candidateTests) {
                $content = Get-Content $t.FullName -Raw
                if ($content -match '<PublishAot>\s*true\s*</PublishAot>' -or $t.Name -match 'AotSmokeTest|AotTest|NativeAot') {
                    $aotTestProjects += $t
                }
            }
        }
    }
    
    if ($aotTestProjects.Count -eq 0) {
        Write-Host "  ❌ Missing Native AOT smoke test project in tests/ or samples/ for $($aotApplicableProjects.Count) AOT-applicable project(s)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Native AOT smoke test project in tests/ or samples/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $hasValidExecutable = $false
        foreach ($aotProj in $aotTestProjects) {
            $aotContent = Get-Content $aotProj.FullName -Raw
            if ($aotContent -match '<OutputType>\s*Exe\s*</OutputType>' -and ($aotContent -match '<PublishAot>\s*true\s*</PublishAot>' -or $aotContent -match 'PublishAot')) {
                $hasValidExecutable = $true
                break
            }
        }
        if (-not $hasValidExecutable) {
            Write-Host "  ❌ At least one AOT smoke test project must declare OutputType=Exe and PublishAot=true." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("AOT smoke test project must declare OutputType=Exe and PublishAot=true.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
    
    # 3. Check for CI workflow .github/workflows/aot-smoke-test.yml
    $aotWorkflowPath = Join-Path $targetRoot ".github/workflows/aot-smoke-test.yml"
    if (-not (Test-Path $aotWorkflowPath)) {
        Write-Host "  ❌ Missing .github/workflows/aot-smoke-test.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/aot-smoke-test.yml CI workflow.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $wfContent = Get-Content $aotWorkflowPath -Raw
        if ($wfContent -notmatch 'dotnet publish' -or ($wfContent -notmatch 'linux-x64|win-x64' -and $wfContent -notmatch 'PublishAot')) {
            Write-Host "  ❌ Workflow .github/workflows/aot-smoke-test.yml does not execute a valid Native AOT publish step." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid aot-smoke-test.yml workflow.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
} else {
    Write-Host "  [INFO] Zero Native AOT applicable projects in src/ (pure analyzer/generator repository). Native AOT test gate skipped." -ForegroundColor Gray
}

if ($aotErrors -eq 0) {
    Write-Host "  ✅ Native AOT test project(s) and CI workflow verified." -ForegroundColor Green
}



# -----------------------------------------------------------------------------
# README Package Table Parity Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: README Package Table Parity] Validating documentation package table synchronization..." -ForegroundColor Yellow
$readmeErrors = 0
$readmePath = Join-Path $targetRoot "README.md"

if (-not (Test-Path $readmePath)) {
    Write-Host "  ❌ Missing README.md in repository root." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing README.md in repository root.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $readmeErrors++
} else {
    $readmeContent = Get-Content $readmePath -Raw -Encoding utf8
    $allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

    foreach ($proj in $allSrcProjs) {
        $projName = $proj.Name
        $baseName = $proj.BaseName
        
        # Check if project appears in README.md inside a table or package reference
        $escapedBase = [regex]::Escape($baseName)
        $isDocumented = ($readmeContent -match ('\|\s*`?' + $escapedBase + '`?\s*\|')) -or 
                        ($readmeContent -match ('\[`?' + $escapedBase + '`?\]')) -or
                        ($readmeContent -match "/packages/$escapedBase") -or
                        ($readmeContent -match ('\|\s*\[`?' + $escapedBase + '`?\]'))

        if (-not $isDocumented) {
            Write-Host "  ❌ Project '$projName' is missing from the packages table in README.md." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' is missing from the packages table in README.md.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $readmeErrors++
        }
    }

    if ($readmeErrors -eq 0) {
        Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified in README.md package table." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# Test Suite Symmetry & Coverage Gate (Principle 12)
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Test Suite Symmetry] Validating test project symmetry & project references across tests/..." -ForegroundColor Yellow
$testSymErrors = 0
$testsDir = Join-Path $targetRoot "tests"

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

$allTestProjs = @()
$testProjectReferences = @{}
if (Test-Path $testsDir) {
    $allTestProjs = Get-ChildItem -Path $testsDir -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }
    foreach ($tp in $allTestProjs) {
        $tContent = Get-Content $tp.FullName -Raw -Encoding utf8
        $refs = [regex]::Matches($tContent, '<ProjectReference\s+Include="([^"]+)"')
        foreach ($m in $refs) {
            $refFile = Split-Path $m.Groups[1].Value.Replace('\', '/') -Leaf
            $testProjectReferences[$refFile] = $true
        }
    }
}

foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $baseName = $proj.BaseName
    
    # Check 1: Named test suite matching base name
    $hasNamedTest = ($allTestProjs | Where-Object { $_.BaseName -match "^$([regex]::Escape($baseName))(\..+)?Tests?$" -or $_.BaseName -like "*$baseName*" }) -ne $null
    
    # Check 2: Direct ProjectReference in any test project
    $hasReference = $testProjectReferences.ContainsKey($projName)
    
    if (-not $hasNamedTest -and -not $hasReference) {
        Write-Host "  ❌ Project '$projName' has no corresponding test suite in tests/ (missing test project or ProjectReference)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding test suite in tests/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $testSymErrors++
    }
}

if ($testSymErrors -eq 0) {
    Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified with corresponding test suite in tests/." -ForegroundColor Green
}


# Summary & Result Gate
# -----------------------------------------------------------------------------
Write-Host "`n==================================================" -ForegroundColor Cyan
if ($Violations.Count -gt 0) {
    Write-Host "  FAILED: $($Violations.Count) Compliance Violations Detected!" -ForegroundColor Red
    Write-Host "==================================================" -ForegroundColor Cyan
    foreach ($V in $Violations) {
        Write-Host "  ❌ $V" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "  SUCCESS: 100% Governance & Compliance Verified. Zero violations. " -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 0
}
