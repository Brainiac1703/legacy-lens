# ---------------------------------------------------------------------------
# Estado remoto.
#
# Configuración parcial a propósito: los datos de la cuenta de almacenamiento no
# se fijan aquí porque cambian entre entornos y porque el fichero es público. Se
# pasan al inicializar:
#
#   terraform init -backend-config=backend.hcl
#
# y en el pipeline con -backend-config=key=value.
#
# El método de autenticación tampoco se fija aquí, y es deliberado: los dos
# escenarios legítimos son distintos.
#
#   En una máquina de desarrollo, contra una cuenta de almacenamiento corporativa
#   sobre la que quizá no se tengan permisos de RBAC, lo práctico es la clave de
#   acceso en backend.hcl, que está excluido del repositorio.
#
#   En el pipeline se usa OIDC con identidad de Azure, pasando
#   -backend-config="use_azuread_auth=true". Así no hay ninguna clave que guardar
#   como secreto de GitHub, que es la razón de usar OIDC en primer lugar.
#
# Fijar use_azuread_auth aquí impediría el primer caso, así que se decide fuera.
#
# El backend de azurerm usa un blob lease como bloqueo, que es lo que impide que
# un apply desde el pipeline y otro desde una máquina local se pisen.
#
# Para trabajar sin estado remoto basta con:
#
#   terraform init -backend=false
# ---------------------------------------------------------------------------

terraform {
  backend "azurerm" {}
}
