<#
.SYNOPSIS
    Applies existing CourtBook EF Core migrations to a target staging database.
.DESCRIPTION
    SAFETY RULES:
    1. This script ONLY applies existing migrations from the repository.
    2. It NEVER creates, modifies, or drops migrations.
    3. It NEVER executes destructive database drops or schema resets.
.PARAMETER ConnectionString
    The SQL Server connection string for the staging database.
#>
[CmdletBinding()]
param (
    [Parameter(Position = 0, Mandatory = $false)]
    [string]$ConnectionString = $env:ConnectionStrings__DefaultConnection
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    Write-Error "ERROR: Connection string is required. Provide it as parameter or set `$env:ConnectionStrings__DefaultConnection."
    exit 1
}

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "CourtBook -- Applying Existing Migrations to Staging Database" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
Set-Location $RootDir

Write-Host "Verifying existing migrations in repository..." -ForegroundColor Yellow
dotnet ef migrations list --project src/CourtBook.Infrastructure --startup-project src/CourtBook.API

Write-Host ""
Write-Host "Applying existing migrations to target database..." -ForegroundColor Yellow
dotnet ef database update --project src/CourtBook.Infrastructure --startup-project src/CourtBook.API --connection $ConnectionString

Write-Host ""
Write-Host "SUCCESS: All existing migrations applied successfully to staging database." -ForegroundColor Green
Write-Host "==============================================================================" -ForegroundColor Cyan
