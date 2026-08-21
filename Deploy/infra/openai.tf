resource "azurerm_resource_group" "main" {
  name     = "rg-${var.project}-tfm"
  location = var.location
  tags     = var.tags
}

resource "azurerm_cognitive_account" "openai" {
  name                  = "oai-${var.project}-tfm"
  location              = azurerm_resource_group.main.location
  resource_group_name   = azurerm_resource_group.main.name
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = "oai-${var.project}-tfm"

  # Se accede con identidad administrada, no con clave.
  local_auth_enabled = true

  tags = var.tags
}

# Modelo económico para el trabajo repetitivo: una llamada por cada objeto
# de base de datos analizado.
resource "azurerm_cognitive_deployment" "documentation" {
  name                 = var.documentation_model.name
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = var.documentation_model.name
    version = var.documentation_model.version
  }

  sku {
    name     = "Standard"
    capacity = var.documentation_model.capacity
  }
}

# Modelo más capaz, una sola llamada por análisis: el plan de migración es la
# única decisión que exige razonar sobre el grafo de dependencias completo.
resource "azurerm_cognitive_deployment" "planning" {
  name                 = var.planning_model.name
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = var.planning_model.name
    version = var.planning_model.version
  }

  sku {
    name     = "Standard"
    capacity = var.planning_model.capacity
  }

  # Los despliegues sobre la misma cuenta no pueden crearse en paralelo.
  depends_on = [azurerm_cognitive_deployment.documentation]
}
