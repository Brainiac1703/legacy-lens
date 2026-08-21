<#
.SYNOPSIS
    Crea la cuenta de almacenamiento donde vivirá el estado de Terraform.

.DESCRIPTION
    Esto no puede gestionarlo Terraform: es el problema del huevo y la gallina —
    haría falta un estado para crear el sitio donde se guarda el estado. Por eso
    se aprovisiona una sola vez con az y queda fuera del ciclo de Terraform.

    Se ejecuta una única vez por suscripción.

.EXAMPLE
    ./scripts/bootstrap-tfstate.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-legacylens-tfstate',
    [string] $Location = 'francecentral',
    [string] $ContainerName = 'tfstate'
)

$ErrorActionPreference = 'Stop'

$subscriptionId = az account show --query id --output tsv
Write-Host "Suscripción: $subscriptionId" -ForegroundColor Cyan

# El nombre de una cuenta de almacenamiento es único a nivel mundial, así que se
# deriva de la suscripción: el mismo script sobre la misma suscripción produce
# siempre el mismo nombre, y sobre otra no colisiona.
$hash = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($subscriptionId))
).Replace('-', '').ToLower()

$storageAccount = "stlegacylens$($hash.Substring(0, 8))"

Write-Host "Creando el grupo de recursos $ResourceGroup..." -ForegroundColor Cyan
az group create `
    --name $ResourceGroup `
    --location $Location `
    --tags project=legacy-lens purpose=terraform-state managed-by=script `
    --output none

Write-Host "Creando la cuenta de almacenamiento $storageAccount..." -ForegroundColor Cyan
az storage account create `
    --name $storageAccount `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --min-tls-version TLS1_2 `
    --allow-blob-public-access false `
    --output none

# El estado de Terraform contiene valores sensibles, así que se protege contra
# borrado accidental y se versiona: un apply que corrompa el estado se puede
# deshacer volviendo a una versión anterior del blob.
Write-Host 'Activando versionado y retención de borrados...' -ForegroundColor Cyan
az storage account blob-service-properties update `
    --account-name $storageAccount `
    --resource-group $ResourceGroup `
    --enable-versioning true `
    --enable-delete-retention true `
    --delete-retention-days 30 `
    --output none

Write-Host "Creando el contenedor $ContainerName..." -ForegroundColor Cyan
az storage container create `
    --name $ContainerName `
    --account-name $storageAccount `
    --auth-mode login `
    --output none

$backendPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Deploy/infra/backend.hcl'

@"
resource_group_name  = "$ResourceGroup"
storage_account_name = "$storageAccount"
container_name       = "$ContainerName"
key                  = "legacy-lens.tfstate"
"@ | Set-Content -Path $backendPath -Encoding utf8

Write-Host ''
Write-Host "Escrito $backendPath" -ForegroundColor Green
Write-Host ''
Write-Host 'Siguiente paso: migrar el estado local al remoto.' -ForegroundColor Yellow
Write-Host '  cd Deploy/infra'
Write-Host '  terraform init -migrate-state -backend-config=backend.hcl'
Write-Host ''
Write-Host 'Y para el pipeline, anota estos valores:' -ForegroundColor Yellow
Write-Host "  TFSTATE_RESOURCE_GROUP  = $ResourceGroup"
Write-Host "  TFSTATE_STORAGE_ACCOUNT = $storageAccount"
Write-Host "  TFSTATE_CONTAINER       = $ContainerName"
