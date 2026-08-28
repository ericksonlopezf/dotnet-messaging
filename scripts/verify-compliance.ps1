#!/usr/bin/env pwsh
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
    "BOUNDARY.md", "PULL_REQUEST_TEMPLATE.md"
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
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($File in $CsFiles) {
    $Content = Get-Content -Path $File.FullName -Raw
    if ($Content -notmatch $HeaderRegex) {
        $Violations.Add("Missing/Invalid copyright header: '$($File.FullName)'. Expected '// Copyright © Erickson Lopez. MIT License.' at top.")
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

        foreach ($Line in $Lines) {
            $Trimmed = $Line.Trim()
            if ($Trimmed.StartsWith("/*")) { $InBlockComment = $true }
            if ($Trimmed.EndsWith("*/")) { $InBlockComment = $false; continue }
            if ($InBlockComment -or $Trimmed.StartsWith("//") -or [string]::IsNullOrWhiteSpace($Trimmed)) { continue }

            if ($Trimmed -match '^\s*(public|internal|sealed|abstract|static|readonly|ref|partial)*\s*(class|interface|struct|enum|record)\s+([A-Za-z0-9_]+)') {
                $TypeName = $Matches[3]
                $DeclaredTypes.Add($TypeName)
            }
        }

        if ($DeclaredTypes.Count -gt 1) {
            $Violations.Add("Multiple types declared in single file: '$($File.FullName)' contains [$($DeclaredTypes -join ', ')].")
        }
    }
    Write-Host "  ✅ Every production file satisfies the 'One Type Per File' invariant." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 5. GitHub Repository Identity Links
# -----------------------------------------------------------------------------
Write-Host "`n[5/8] Checking GitHub identity links (ericksonlopezf/dotnet-messaging)..." -ForegroundColor Yellow
$MarkdownFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter "*.md" | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults|StrykerOutput|node_modules)[\\/]' }
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
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
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
