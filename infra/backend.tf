# ---------------------------------------------------------------------------
# Estado remoto.
#
# Configuración parcial a propósito: los datos de la cuenta de almacenamiento no
# se fijan aquí porque cambian entre entornos y porque el fichero es público. Se
# pasan al inicializar:
#
#   terraform init -backend-config=backend.hcl
#
# y en el pipeline con -backend-config=key=value, autenticándose con OIDC.
#
# El backend de azurerm usa un blob lease como bloqueo, que es lo que impide que
# un apply desde el pipeline y otro desde una máquina local se pisen.
#
# Para trabajar sin estado remoto (por ejemplo la primera vez, antes de crear la
# cuenta de almacenamiento) basta con:
#
#   terraform init -backend=false
# ---------------------------------------------------------------------------

terraform {
  backend "azurerm" {
    # Se accede al estado con identidad de Azure, no con la clave de la cuenta de
    # almacenamiento. Es coherente con el resto del proyecto: no hay ningún
    # secreto que guardar ni rotar. Requiere el rol Storage Blob Data Contributor
    # sobre la cuenta, que asigna scripts/bootstrap-tfstate.ps1.
    use_azuread_auth = true
  }
}
