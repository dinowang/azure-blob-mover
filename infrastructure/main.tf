provider "azurerm" {
  features {
    virtual_machine {
      delete_os_disk_on_deletion = true
    }
  }
}

variable "code" {
  default = "asec"
}

resource "azurerm_resource_group" "default" {
  name     = "${var.code}-rg"
  location = "Southeast Asia"
}

resource "azurerm_storage_account" "public" {
  location                 = azurerm_resource_group.default.location
  resource_group_name      = azurerm_resource_group.default.name
  name                     = "${var.code}public"
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_storage_account" "private" {
  location                 = azurerm_resource_group.default.location
  resource_group_name      = azurerm_resource_group.default.name
  name                     = "${var.code}private"
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_virtual_network" "default" {
  location            = azurerm_resource_group.default.location
  resource_group_name = azurerm_resource_group.default.name
  name                = "${var.code}-vnet"
  address_space       = ["10.0.0.0/16"]
  dns_servers         = ["10.0.0.4"]
}

resource "azurerm_subnet" "private_endpoint" {
  resource_group_name  = azurerm_resource_group.default.name
  virtual_network_name = azurerm_virtual_network.default.name
  name                 = "PrivateEndpointSubnet"
  address_prefixes     = ["10.0.1.0/24"]

  enforce_private_link_endpoint_network_policies = true
}

resource "azurerm_subnet" "vnet_integration" {
  resource_group_name  = azurerm_resource_group.default.name
  virtual_network_name = azurerm_virtual_network.default.name
  name                 = "VnetIntegrationSubnet"
  address_prefixes     = ["10.0.2.0/24"]

  delegation {
    name = "delegation"

    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

resource "azurerm_private_dns_zone" "file" {
  resource_group_name = azurerm_resource_group.default.name
  name                = "privatelink.file.core.windows.net"
}

resource "azurerm_private_dns_zone_virtual_network_link" "default" {
  resource_group_name   = azurerm_resource_group.default.name
  virtual_network_id    = azurerm_virtual_network.default.id
  private_dns_zone_name = azurerm_private_dns_zone.file.name
  name                  = "${var.code}-private-dns-link"
  registration_enabled  = true
}

resource "azurerm_private_endpoint" "storage_account_private" {
  location            = azurerm_resource_group.default.location
  resource_group_name = azurerm_resource_group.default.name
  subnet_id           = azurerm_subnet.private_endpoint.id
  name                = "${azurerm_storage_account.private.name}-pe"

  private_service_connection {
    name                           = "${azurerm_storage_account.private.name}-psc"
    private_connection_resource_id = azurerm_storage_account.private.id
    subresource_names              = ["file"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "dns-group"
    private_dns_zone_ids = [azurerm_private_dns_zone.file.id]
  }
}

resource "azurerm_storage_account" "mover" {
  location                 = azurerm_resource_group.default.location
  resource_group_name      = azurerm_resource_group.default.name
  name                     = "${var.code}funcdiag"
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_application_insights" "mover" {
  location            = azurerm_resource_group.default.location
  resource_group_name = azurerm_resource_group.default.name
  name                = "${var.code}-func-ai"
  application_type    = "web"
}

resource "azurerm_app_service_plan" "mover" {
  location            = azurerm_resource_group.default.location
  resource_group_name = azurerm_resource_group.default.name
  name                = "${var.code}-func-plan"

  sku {
    tier = "Standard"
    size = "S1"
  }
}

resource "azurerm_function_app" "mover" {
  location                   = azurerm_resource_group.default.location
  resource_group_name        = azurerm_resource_group.default.name
  app_service_plan_id        = azurerm_app_service_plan.mover.id
  storage_account_name       = azurerm_storage_account.mover.name
  storage_account_access_key = azurerm_storage_account.mover.primary_access_key
  name                       = "${var.code}-func"
  version                    = "~3"

  site_config {
    always_on                 = true
    ftps_state                = "Disabled"
    use_32_bit_worker_process = false
  }

  app_settings = {
    "APPINSIGHTS_INSTRUMENTATIONKEY"        = "${azurerm_application_insights.mover.instrumentation_key}"
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = "${azurerm_application_insights.mover.connection_string}"
    "StoragesPublic"                        = "${azurerm_storage_account.public.primary_blob_connection_string}"
    "StoragesPrivate"                       = "${azurerm_storage_account.public.primary_connection_string}"
    "WEBSITE_DNS_SERVER"                    = "168.63.129.16"
    "WEBSITE_VNET_ROUTE_ALL"                = "1"
    "WEBSITE_RUN_FROM_PACKAGE"              = "1"
    "FUNCTION_WORKER_RUNTIME"               = "dotnet"
  }
}

resource "azurerm_app_service_virtual_network_swift_connection" "mover" {
  app_service_id = azurerm_function_app.mover.id
  subnet_id      = azurerm_subnet.vnet_integration.id
}

resource "azurerm_eventgrid_system_topic" "public" {
  location               = azurerm_resource_group.default.location
  resource_group_name    = azurerm_resource_group.default.name
  source_arm_resource_id = azurerm_storage_account.public.id
  name                   = "${var.code}-public-topic"
  topic_type             = "Microsoft.Storage.StorageAccounts"
}

resource "azurerm_eventgrid_event_subscription" "default" {
  name       = "${var.code}-public-subscription"
  scope      = azurerm_storage_account.public.id
  topic_name = azurerm_eventgrid_system_topic.public.name

  included_event_types = [
    "Microsoft.Storage.BlobCreated",
    "Microsoft.Storage.BlobDeleted",
    "Microsoft.Storage.DirectoryCreated",
    "Microsoft.Storage.DirectoryDeleted"
  ]

  azure_function_endpoint {
    function_id          = "${azurerm_function_app.mover.id}/functions/BlobToFile"
    max_events_per_batch = 1
  }
}
