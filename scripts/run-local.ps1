# Run Local — Autonomous Software Factory
# Executa o pipeline localmente.

param(
    [Parameter(Mandatory=$true)]
    [string]$Requirement
)

Write-Host "=== Autonomous Software Factory ===" -ForegroundColor Cyan
Write-Host "Requisito: $Requirement" -ForegroundColor Yellow

if (-not (Test-Path $Requirement)) {
    Write-Host "[ERRO] Arquivo de requisito nao encontrado: $Requirement" -ForegroundColor Red
    exit 1
}

dotnet run --project src/AutonomousSoftwareFactory -- --requirement $Requirement
