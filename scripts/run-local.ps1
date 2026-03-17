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

# Validar que o projeto compila antes de executar
Write-Host ""
Write-Host "Compilando projeto..." -ForegroundColor Cyan

$buildOutput = dotnet build "$PSScriptRoot\..\src\AutonomousSoftwareFactory\AutonomousSoftwareFactory.csproj" --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERRO] Build falhou. Corrija os erros antes de executar:" -ForegroundColor Red
    Write-Host $buildOutput -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Build concluido com sucesso" -ForegroundColor Green
Write-Host ""

dotnet run --project "$PSScriptRoot\..\src\AutonomousSoftwareFactory" --no-build -- --requirement $Requirement
