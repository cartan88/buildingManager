# Nightly backup of the BuildingManager database (SQL Server Express has no SQL Agent).
# It must run elevated: SQL Server writes the .bak into its own folder under Program Files,
# which only administrators can read. Schedule it from an *administrator* terminal, e.g.:
#   schtasks /Create /SC DAILY /ST 23:00 /RL HIGHEST /TN "BuildingManager backup" /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\Misc\_Projects\Building Manager\scripts\backup-database.ps1\""
param(
    [string]$Server = ".\SQLEXPRESS",
    [string]$Database = "BuildingManager",
    # Point this at a OneDrive/Google Drive folder so backups leave the PC.
    [string]$BackupDir = "$env:USERPROFILE\OneDrive\Backups\BuildingManager",
    [int]$KeepDays = 30
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $BackupDir | Out-Null

# SQL Server's service account writes the file, so back up to its own folder first, then copy.
$sqlDir = sqlcmd -S $Server -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))"
if ($LASTEXITCODE -ne 0 -or -not $sqlDir) { throw "Could not connect to $Server" }
$sqlDir = "$sqlDir".Trim()
$file = "$Database-$(Get-Date -Format 'yyyyMMdd-HHmm').bak"

sqlcmd -S $Server -E -C -b -Q "BACKUP DATABASE [$Database] TO DISK = N'$sqlDir\$file' WITH INIT, CHECKSUM"
if ($LASTEXITCODE -ne 0) { throw "Backup failed" }

Move-Item "$sqlDir\$file" (Join-Path $BackupDir $file) -Force
Get-ChildItem $BackupDir -Filter "$Database-*.bak" |
    Where-Object LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) |
    Remove-Item
Write-Host "Backed up to $(Join-Path $BackupDir $file)"
