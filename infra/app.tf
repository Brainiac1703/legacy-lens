# ----------------------------------------------------------------------------
# Publicación de la aplicación. Se controla con var.deploy_app para poder
# aprovisionar primero solo Azure OpenAI y desarrollar en local.
# ----------------------------------------------------------------------------

resource "azurerm_log_analytics_workspace" "main" {
  count = var.deploy_app ? 1 : 0

  name                = "log-${var.project}-tfm"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

resource "azurerm_container_registry" "main" {
  count = var.deploy_app ? 1 : 0

  name                = "acr${var.project}tfm"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Basic"
  admin_enabled       = false
  tags                = var.tags
}

resource "azurerm_container_app_environment" "main" {
  count = var.deploy_app ? 1 : 0

  name                       = "cae-${var.project}-tfm"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main[0].id
  tags                       = var.tags
}

resource "azurerm_container_app" "main" {
  count = var.deploy_app ? 1 : 0

  name                         = "ca-${var.project}-tfm"
  container_app_environment_id = azurerm_container_app_environment.main[0].id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type = "SystemAssigned"
  }

  registry {
    server   = azurerm_container_registry.main[0].login_server
    identity = "system"
  }

  template {
    # Una sola réplica: el circuito de Blazor Server mantiene estado en memoria
    # y este proyecto no necesita escalar. La afinidad de sesión de abajo es lo
    # que permitiría subir el mínimo sin romper las conexiones.
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "web"
      image  = var.container_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "Ai__Endpoint"
        value = azurerm_cognitive_account.openai.endpoint
      }

      env {
        name  = "Ai__DocumentationDeployment"
        value = azurerm_cognitive_deployment.documentation.name
      }

      env {
        name  = "Ai__PlanningDeployment"
        value = azurerm_cognitive_deployment.planning.name
      }

      # No hace falta ninguna variable con credenciales: al no configurarse
      # Ai__ApiKey, la aplicación usa la identidad administrada del contenedor.
    }
  }

  # Blazor Server mantiene un circuito SignalR con estado por usuario, así que
  # escalar horizontalmente exigiría afinidad de sesión. El provider de azurerm
  # no expone todavía stickySessions, aunque la plataforma sí lo soporta: haría
  # falta el provider azapi. Con una sola réplica no aplica, y queda anotado
  # como lo primero que habría que resolver antes de subir el mínimo de réplicas.
  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
}

# La aplicación lee el registro con su identidad, sin credenciales de admin.
resource "azurerm_role_assignment" "acr_pull" {
  count = var.deploy_app ? 1 : 0

  scope                = azurerm_container_registry.main[0].id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_container_app.main[0].identity[0].principal_id
}

# Y llama a Azure OpenAI con esa misma identidad, sin clave en configuración.
resource "azurerm_role_assignment" "openai_user" {
  count = var.deploy_app ? 1 : 0

  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_container_app.main[0].identity[0].principal_id
}
