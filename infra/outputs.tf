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
