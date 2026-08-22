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

  name                = "cae-${var.project}-tfm"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags

  # Los dos argumentos van juntos: enlazar el workspace sin declarar el destino
  # hace que el proveedor rechace el recurso. Omitir logs_destination no
  # significa "por defecto", significa "solo streaming, sin persistir".
  # El plan no lo detecta porque la comprobacion esta en la creacion.
  logs_destination           = "log-analytics"
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main[0].id
}

# Identidad asignada por el usuario, no asignada por el sistema. La diferencia
# no es estetica: una identidad de sistema nace con el Container App, asi que su
# principal_id no existe hasta que el recurso esta creado, y los roles solo se
# pueden asignar despues. Pero Azure no termina de aprovisionar el Container App
# hasta poder autenticarse contra el registro, que necesita AcrPull. El ciclo se
# cierra y el aprovisionamiento se queda esperando indefinidamente.
#
# Creando la identidad por separado, los dos roles existen antes que la
# aplicacion y el ciclo desaparece.
resource "azurerm_user_assigned_identity" "app" {
  count = var.deploy_app ? 1 : 0

  name                = "id-${var.project}-tfm"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags
}

resource "azurerm_container_app" "main" {
  count = var.deploy_app ? 1 : 0

  name                         = "ca-${var.project}-tfm"
  container_app_environment_id = azurerm_container_app_environment.main[0].id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app[0].id]
  }

  registry {
    server   = azurerm_container_registry.main[0].login_server
    identity = azurerm_user_assigned_identity.app[0].id
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

      # Cadena de conexión sin credenciales: Active Directory Default hace que
      # el cliente de SQL pida un token con la identidad administrada del
      # contenedor. Para que funcione, esa identidad tiene que existir como
      # usuario dentro de la base de datos, y de eso se encarga el paso de
      # actualización del pipeline.
      env {
        name = "ConnectionStrings__DefaultConnection"
        value = join("", [
          "Server=tcp:${azurerm_mssql_server.main[0].fully_qualified_domain_name},1433;",
          "Database=${azurerm_mssql_database.main[0].name};",
          "Authentication=Active Directory Default;",
          "Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
        ])
      }

      # Con una identidad asignada por el usuario hay que decir cual es:
      # DefaultAzureCredential no la adivina, y sin esta variable pediria un
      # token para la identidad de sistema, que ya no existe. Afecta tanto a
      # SQL como a OpenAI, porque las dos usan la misma cadena de credenciales.
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.app[0].client_id
      }

      # Tampoco hay clave de OpenAI: al no configurarse Ai__ApiKey, la
      # aplicación usa esa misma identidad administrada.
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

  # Los roles se asignan a la identidad, no a la aplicacion, asi que Terraform
  # no ve ninguna dependencia entre ellos y crearia las tres cosas en paralelo.
  # Sin esto el ciclo vuelve convertido en carrera: a veces el registro estaria
  # autorizado a tiempo y a veces no.
  depends_on = [
    azurerm_role_assignment.acr_pull,
    azurerm_role_assignment.openai_user,
  ]

  lifecycle {
    # La imagen la sustituye el pipeline con az containerapp update en cada
    # despliegue, y no se le pasa de vuelta a Terraform. Sin ignorarla, el
    # siguiente apply de infraestructura devolveria la aplicacion a la imagen
    # de arranque publica y tumbaria lo desplegado.
    ignore_changes = [template[0].container[0].image]
  }
}

# La aplicación lee el registro con su identidad, sin credenciales de admin.
resource "azurerm_role_assignment" "acr_pull" {
  count = var.deploy_app ? 1 : 0

  scope                = azurerm_container_registry.main[0].id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app[0].principal_id
}

# Y llama a Azure OpenAI con esa misma identidad, sin clave en configuración.
resource "azurerm_role_assignment" "openai_user" {
  count = var.deploy_app ? 1 : 0

  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.app[0].principal_id
}
