param(
    [Parameter(Mandatory=$true)][string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Entregas')
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$deliveryRoot = [IO.Path]::GetFullPath($OutputDirectory)
$buildRoot = Join-Path $deliveryRoot '_build'
$payloadRoot = Join-Path $deliveryRoot '_payload'
New-Item -ItemType Directory -Force -Path $deliveryRoot | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'LEEME-Instalacion.txt') $deliveryRoot -Force
Copy-Item (Join-Path $PSScriptRoot 'AVISOS-TERCEROS.txt') $deliveryRoot -Force
& dotnet publish (Join-Path $sourceRoot 'SIRASD.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false ('-p:OutputPath='+$buildRoot+[IO.Path]::DirectorySeparatorChar) -o $payloadRoot
if($LASTEXITCODE -ne 0){throw 'Falló la publicación de SIRASD.'}
& $CompilerPath ('/DPayloadDir='+$payloadRoot) ('/DDeliveryDir='+$deliveryRoot) ('/DSourceRoot='+$sourceRoot) (Join-Path $PSScriptRoot 'SIRASD-Setup.iss')
if($LASTEXITCODE -ne 0){throw 'Falló la creación del instalador.'}
Copy-Item (Join-Path $payloadRoot 'SIRASD.exe') (Join-Path $deliveryRoot 'SIRASD-0.9.7-Portatil-Windows-x64.exe') -Force
Get-ChildItem $deliveryRoot -Filter '*.exe' | Get-FileHash -Algorithm SHA256
