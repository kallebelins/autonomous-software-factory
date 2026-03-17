# Setup — Autonomous Software Factory
# Verifica pré-requisitos e cria pastas necessárias.

Write-Host "=== Setup: Autonomous Software Factory ===" -ForegroundColor Cyan

# Verificar .NET
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetVersion = dotnet --version
    Write-Host "[OK] .NET SDK: $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "[ERRO] .NET SDK nao encontrado. Instale em https://dotnet.microsoft.com" -ForegroundColor Red
}

# Verificar Git
if (Get-Command git -ErrorAction SilentlyContinue) {
    $gitVersion = git --version
    Write-Host "[OK] Git: $gitVersion" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Git nao encontrado." -ForegroundColor Red
}

# Criar pastas de trabalho
$folders = @("workspace/repos", "workspace/artifacts", "workspace/temp", "logs")
foreach ($folder in $folders) {
    if (-not (Test-Path $folder)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Write-Host "[CRIADO] $folder" -ForegroundColor Yellow
    } else {
        Write-Host "[OK] $folder ja existe" -ForegroundColor Green
    }
}

# Restaurar pacotes NuGet
Write-Host ""
Write-Host "Restaurando pacotes NuGet..." -ForegroundColor Cyan

$restoreResult = dotnet restore "$PSScriptRoot\..\AutonomousSoftwareFactory.slnx" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] dotnet restore concluido com sucesso" -ForegroundColor Green
} else {
    Write-Host "[ERRO] dotnet restore falhou:" -ForegroundColor Red
    Write-Host $restoreResult -ForegroundColor Red
}

Write-Host ""
Write-Host "Setup concluido." -ForegroundColor Cyan
