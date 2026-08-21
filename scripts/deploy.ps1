<#
.SYNOPSIS
    Construye la imagen y actualiza el Container App.

.DESCRIPTION
    La imagen se construye con 'az acr build', es decir dentro de Azure y no en
    la máquina local. Eso evita tener que autenticarse contra el registro y
    subir cientos de megas por la línea de casa.

    Requisitos previos:
      - terraform apply con deploy_app = true, para que existan el registro y
        el Container App.
      - Sesión activa de az login en la suscripción correcta.

.EXAMPLE
    ./scripts/deploy.ps1
#>

[CmdletBinding()]
param(
    [string] $Tag = (Get-Date -Format 'yyyyMMdd-HHmmss')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraPath = Join-Path $repoRoot 'infra'

Write-Host 'Leyendo las salidas de Terraform...' -ForegroundColor Cyan

Push-Location $infraPath
try {
    $registry = (terraform output -raw container_registry)
    $resourceGroup = (terraform output -raw resource_group)
}
finally {
    Pop-Location
}

if ([string]::IsNullOrWhiteSpace($registry)) {
    throw 'No hay registro de contenedores. Ejecuta terraform apply con deploy_app = true.'
}

$registryName = $registry.Split('.')[0]
$image = "$registry/legacy-lens:$Tag"

Write-Host "Construyendo $image en Azure..." -ForegroundColor Cyan
az acr build `
    --registry $registryName `
    --image "legacy-lens:$Tag" `
    --file (Join-Path $repoRoot 'Dockerfile') `
    $repoRoot

Write-Host 'Actualizando el Container App...' -ForegroundColor Cyan
az containerapp update `
    --name "ca-legacylens-tfm" `
    --resource-group $resourceGroup `
    --image $image `
    --output none

$fqdn = az containerapp show `
    --name "ca-legacylens-tfm" `
    --resource-group $resourceGroup `
    --query 'properties.configuration.ingress.fqdn' `
    --output tsv

Write-Host ''
Write-Host "Desplegado: https://$fqdn" -ForegroundColor Green
