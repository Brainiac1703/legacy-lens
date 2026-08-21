output "openai_endpoint" {
  description = "Endpoint de Azure OpenAI. Va en Ai__Endpoint."
  value       = azurerm_cognitive_account.openai.endpoint
}

output "documentation_deployment" {
  description = "Nombre del despliegue usado para documentar objetos."
  value       = azurerm_cognitive_deployment.documentation.name
}

output "planning_deployment" {
  description = "Nombre del despliegue usado para el plan de migración."
  value       = azurerm_cognitive_deployment.planning.name
}

output "resource_group" {
  value = azurerm_resource_group.main.name
}

output "app_url" {
  description = "URL pública de la aplicación, vacía si aún no se ha desplegado."
  value       = var.deploy_app ? "https://${azurerm_container_app.main[0].ingress[0].fqdn}" : ""
}

output "container_registry" {
  description = "Servidor del registro de contenedores, para el CI."
  value       = var.deploy_app ? azurerm_container_registry.main[0].login_server : ""
}

output "sql_server_fqdn" {
  description = "Nombre del servidor SQL, para aplicar migraciones desde el pipeline."
  value       = var.deploy_app ? azurerm_mssql_server.main[0].fully_qualified_domain_name : ""
}

output "sql_database_name" {
  value = var.deploy_app ? azurerm_mssql_database.main[0].name : ""
}

output "container_app_principal_id" {
  description = <<-EOT
    Identidad administrada de la aplicación. El pipeline la necesita para darla
    de alta como usuario dentro de la base de datos: crear el recurso no basta,
    hay que ejecutar CREATE USER FROM EXTERNAL PROVIDER dentro del propio SQL.
  EOT
  value       = var.deploy_app ? azurerm_container_app.main[0].identity[0].principal_id : ""
}

output "container_app_name" {
  description = "Nombre del Container App, que es también el del usuario de base de datos."
  value       = var.deploy_app ? azurerm_container_app.main[0].name : ""
}
