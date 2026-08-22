<#
.SYNOPSIS
    Prepara la autenticación de los pipelines de GitHub contra Azure con OIDC.

.DESCRIPTION
    Crea dos identidades con credenciales federadas. GitHub presenta un token
    firmado en cada ejecución y Azure lo valida contra el repositorio y la rama:
    no hay ningún secreto de cliente que guardar ni que caduque.

    Son dos identidades y no una a propósito, con permisos distintos:

      legacy-lens-infra   Gestiona la infraestructura. Necesita permisos amplios
                          porque Terraform crea el grupo de recursos y asigna
                          roles.

      legacy-lens-deploy  Solo construye la imagen y actualiza el Container App.
                          Limitada al grupo de recursos de la aplicación.

    Así una fuga en el pipeline de despliegue —el que se ejecuta en cada commit—
    no permite tocar la infraestructura ni los permisos.

.NOTES
    La identidad de infraestructura recibe Contributor sobre la suscripción,
    porque Terraform crea el grupo de recursos, y Role Based Access Control
    Administrator limitado al grupo de recursos de la aplicación, porque
    Terraform asigna los roles de la identidad administrada.

    Ese segundo permiso es sensible: dentro de ese grupo de recursos permite
    conceder roles. Está acotado al grupo y no a la suscripción, que es la
    diferencia importante. Si en vuestro entorno no se acepta, la alternativa es
    quitar del Terraform los dos azurerm_role_assignment y crearlos a mano una
    vez; el pipeline dejaría de necesitar ese permiso.

.EXAMPLE
    ./scripts/bootstrap-github-oidc.ps1 -Repository Brainiac1703/legacy-lens
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Repository,

    [string] $AppResourceGroup = 'rg-legacylens-tfm',
    [string] $StateResourceGroup = 'rg-legacylens-tfstate',
    [string] $Branch = 'main'
)

$ErrorActionPreference = 'Stop'

$subscriptionId = az account show --query id --output tsv
$tenantId = az account show --query tenantId --output tsv

Write-Host "Suscripción: $subscriptionId" -ForegroundColor Cyan
Write-Host "Repositorio: $Repository" -ForegroundColor Cyan
Write-Host ''

# GitHub puede emitir el «subject» del token de dos formas, y Azure exige que la
# credencial federada coincida **exactamente** con la que reciba:
#
#   repo:propietario/repositorio:ref:refs/heads/main
#   repo:propietario@idPropietario/repositorio@idRepositorio:ref:refs/heads/main
#
# La segunda es la de identificadores inmutables, que sobrevive a renombrar el
# repositorio o la organización. Cuál se use depende de la configuración de la
# cuenta, así que se registran las dos: sobra una y no molesta, mientras que
# faltar la correcta produce un 401 con AADSTS700213 que no dice qué falta.
#
# Los identificadores se consultan a la API pública de GitHub. Si no se pueden
# obtener, se registran solo las clásicas y se avisa.
$repoInfo = $null
try {
    $repoInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository" `
        -Headers @{ 'User-Agent' = 'legacy-lens-bootstrap' } -ErrorAction Stop
}
catch {
    Write-Host 'AVISO: no se pudieron leer los identificadores del repositorio.' -ForegroundColor Yellow
    Write-Host 'Se registrarán solo los sujetos clásicos. Si el pipeline falla con' -ForegroundColor Yellow
    Write-Host 'AADSTS700213, copia el sujeto que aparece en el error y créalo a mano.' -ForegroundColor Yellow
}

function Get-Subjects {
    param([string] $Suffix)

    $subjects = @("repo:${Repository}:$Suffix")

    if ($repoInfo) {
        $owner = $Repository.Split('/')[0]
        $name = $Repository.Split('/')[1]
        $subjects += "repo:$owner@$($repoInfo.owner.id)/$name@$($repoInfo.id):$Suffix"
    }

    return $subjects
}

function New-OidcIdentity {
    param([string] $Name, [string[]] $Subjects)

    $existing = az ad app list --display-name $Name --query '[0].appId' --output tsv

    if ($existing) {
        Write-Host "La aplicación $Name ya existe ($existing)" -ForegroundColor Yellow
        $appId = $existing
    }
    else {
        Write-Host "Creando la aplicación $Name..." -ForegroundColor Cyan
        $appId = az ad app create --display-name $Name --query appId --output tsv
        az ad sp create --id $appId --output none
        # La propagación del service principal en el directorio no es inmediata.
        Start-Sleep -Seconds 15
    }

    foreach ($subject in $Subjects) {
        $credentialName = "github-$($subject -replace '[^a-zA-Z0-9]', '-')"
        if ($credentialName.Length -gt 120) { $credentialName = $credentialName.Substring(0, 120) }

        $body = @{
            name        = $credentialName
            issuer      = 'https://token.actions.githubusercontent.com'
            subject     = $subject
            audiences   = @('api://AzureADTokenExchange')
            description = "GitHub Actions $Repository"
        } | ConvertTo-Json -Compress

        $tempFile = New-TemporaryFile
        try {
            $body | Set-Content -Path $tempFile -Encoding utf8
            az ad app federated-credential create --id $appId --parameters "@$tempFile" --output none 2>$null
            Write-Host "  credencial federada: $subject" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "  ya existía: $subject" -ForegroundColor DarkGray
        }
        finally {
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
    }

    return $appId
}

function Grant-Role {
    param([string] $AppId, [string] $Role, [string] $Scope)

    $principalId = az ad sp show --id $AppId --query id --output tsv

    az role assignment create `
        --assignee-object-id $principalId `
        --assignee-principal-type ServicePrincipal `
        --role $Role `
        --scope $Scope `
        --output none 2>$null

    Write-Host "  $Role -> $($Scope -replace '/subscriptions/[^/]+', '...')" -ForegroundColor DarkGray
}

# --- Identidad de infraestructura -----------------------------------------

Write-Host '=== Identidad de infraestructura ===' -ForegroundColor Green

$infraId = New-OidcIdentity -Name 'legacy-lens-infra' -Subjects (
    (Get-Subjects "ref:refs/heads/$Branch") + (Get-Subjects 'pull_request')
)

Write-Host 'Asignando permisos...' -ForegroundColor Cyan
Grant-Role -AppId $infraId -Role 'Contributor' -Scope "/subscriptions/$subscriptionId"

# El estado se lee y escribe con identidad solo si su cuenta de almacenamiento
# está en esta misma suscripción. Si está en otra —por ejemplo una cuenta de
# despliegues corporativa compartida— no se puede conceder el rol desde aquí, y
# el backend se autentica con clave a través del secreto TFSTATE_ACCESS_KEY.
if ((az group exists --name $StateResourceGroup) -eq 'true') {
    Grant-Role -AppId $infraId -Role 'Storage Blob Data Contributor' `
        -Scope "/subscriptions/$subscriptionId/resourceGroups/$StateResourceGroup"
}
else {
    Write-Host "  $StateResourceGroup no está en esta suscripción." -ForegroundColor Yellow
    Write-Host '  El backend usará clave de acceso: define el secreto TFSTATE_ACCESS_KEY.' -ForegroundColor Yellow
}

# Acotado al grupo de recursos de la aplicación, no a la suscripción.
$appRgExists = az group exists --name $AppResourceGroup
if ($appRgExists -eq 'true') {
    Grant-Role -AppId $infraId -Role 'Role Based Access Control Administrator' `
        -Scope "/subscriptions/$subscriptionId/resourceGroups/$AppResourceGroup"
}
else {
    Write-Host "  AVISO: $AppResourceGroup no existe todavía." -ForegroundColor Yellow
    Write-Host '  Ejecuta terraform apply una vez en local y vuelve a lanzar este script,' -ForegroundColor Yellow
    Write-Host '  o el pipeline no podrá crear las asignaciones de rol.' -ForegroundColor Yellow
}

# --- Identidad de despliegue ----------------------------------------------

Write-Host ''
Write-Host '=== Identidad de despliegue ===' -ForegroundColor Green

$deployId = New-OidcIdentity -Name 'legacy-lens-deploy' -Subjects (
    Get-Subjects "ref:refs/heads/$Branch"
)

Write-Host 'Asignando permisos...' -ForegroundColor Cyan

if ($appRgExists -eq 'true') {
    # Contributor sobre un único grupo de recursos: suficiente para az acr build
    # y para actualizar la revisión del Container App, y nada más.
    Grant-Role -AppId $deployId -Role 'Contributor' `
        -Scope "/subscriptions/$subscriptionId/resourceGroups/$AppResourceGroup"
}
else {
    Write-Host "  Pendiente: $AppResourceGroup no existe todavía." -ForegroundColor Yellow
}

# --- Resultado -------------------------------------------------------------

# El servidor SQL se crea con autenticación exclusivamente por Entra, así que
# necesita un administrador. Es la identidad de infraestructura, porque es la que
# después aplica las migraciones desde el pipeline.
#
# Ojo con el identificador: hace falta el del *service principal*, no el del
# registro de aplicación. Son distintos, y usar el equivocado hace que Azure
# rechace el administrador sin decir por qué.
$infraPrincipalId = az ad sp show --id $infraId --query id --output tsv

Write-Host ''
Write-Host '=== Configura esto en GitHub ===' -ForegroundColor Green
Write-Host ''
Write-Host 'Settings -> Secrets and variables -> Actions -> Variables:' -ForegroundColor Yellow
Write-Host ''
Write-Host "  AZURE_TENANT_ID           $tenantId"
Write-Host "  AZURE_SUBSCRIPTION_ID     $subscriptionId"
Write-Host "  AZURE_INFRA_CLIENT_ID     $infraId"
Write-Host "  AZURE_DEPLOY_CLIENT_ID    $deployId"
Write-Host "  SQL_ADMIN_LOGIN           legacy-lens-infra"
Write-Host "  SQL_ADMIN_OBJECT_ID       $infraPrincipalId"
Write-Host ''
Write-Host 'Y los del estado de Terraform, que imprime bootstrap-tfstate.ps1:' -ForegroundColor Yellow
Write-Host ''
Write-Host '  TFSTATE_STORAGE_ACCOUNT'
Write-Host '  TFSTATE_CONTAINER'
Write-Host '  TFSTATE_KEY'
Write-Host ''
Write-Host 'Son variables, no secretos: ninguno de estos valores es una credencial.' -ForegroundColor DarkGray
Write-Host 'Con OIDC no hay secreto que guardar, que es justo la ventaja.' -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Se pueden definir con gh en una sola tanda:' -ForegroundColor Yellow
Write-Host ''
Write-Host "  gh variable set AZURE_TENANT_ID --body $tenantId --repo $Repository"
Write-Host "  gh variable set AZURE_SUBSCRIPTION_ID --body $subscriptionId --repo $Repository"
Write-Host "  gh variable set AZURE_INFRA_CLIENT_ID --body $infraId --repo $Repository"
Write-Host "  gh variable set AZURE_DEPLOY_CLIENT_ID --body $deployId --repo $Repository"
Write-Host "  gh variable set SQL_ADMIN_LOGIN --body legacy-lens-infra --repo $Repository"
Write-Host "  gh variable set SQL_ADMIN_OBJECT_ID --body $infraPrincipalId --repo $Repository"
