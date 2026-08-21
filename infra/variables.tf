variable "subscription_id" {
  description = "Suscripción de Azure donde se despliega."
  type        = string
}

variable "project" {
  description = "Prefijo corto para nombrar los recursos."
  type        = string
  default     = "legacylens"
}

variable "location" {
  description = <<-EOT
    Región de despliegue. France Central es la elección por dos motivos:
    tiene cuota disponible de gpt-4.1-mini y gpt-4o con SKU Standard, y es la
    región con menor latencia desde España entre las que cumplen lo anterior.
  EOT
  type        = string
  default     = "francecentral"
}

variable "documentation_model" {
  description = "Modelo económico, usado una vez por objeto analizado."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
  default = {
    name     = "gpt-4.1-mini"
    version  = "2025-04-14"
    capacity = 100
  }
}

variable "planning_model" {
  description = "Modelo más capaz, usado una sola vez por análisis para el plan."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
  default = {
    name     = "gpt-4o"
    version  = "2024-11-20"
    capacity = 50
  }
}

variable "deploy_app" {
  description = <<-EOT
    Permite aprovisionar por etapas. Con false solo se crea Azure OpenAI, que
    es lo que hace falta para desarrollar en local. Con true se añade el
    registro de contenedores y el Container App que publica la aplicación.
  EOT
  type        = bool
  default     = false
}

variable "container_image" {
  description = "Imagen a desplegar. El CI la sustituye por la recién construida."
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "tags" {
  description = "Etiquetas aplicadas a todo. Identifican el proyecto y su dueño."
  type        = map(string)
  default = {
    project    = "legacy-lens"
    purpose    = "tfm-master-desarrollo-ia"
    managed-by = "terraform"
  }
}
