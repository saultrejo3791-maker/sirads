
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SIRASD.Desktop.csproj'
$output = Join-Path $PSScriptRoot 'Entrega_Windows'

Write-Host 'Generando SIRASD 0.9 para Windows...' -ForegroundColor Cyan
dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $output

Write-Host ''
Write-Host 'Proceso terminado.' -ForegroundColor Green
Write-Host "Archivo generado en: $output\SIRASD.exe"
Read-Host 'Presiona Enter para cerrar'
