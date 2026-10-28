@description('Azure region for App Service and Table Storage.')
param location string = resourceGroup().location
@description('Globally unique public App Service name.')
param appName string = 'badgecluster-${uniqueString(resourceGroup().id)}'
@description('App Service pricing tier; B1 is an initial small paid deployment.')
@allowed(['B1', 'S1', 'P1v3'])
param sku string = 'B1'
var storageName = 'bc${uniqueString(resourceGroup().id, appName)}'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
  }
}
resource tables 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}
resource contributions 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tables
  name: 'BadgeCluster'
}
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: { name: sku, capacity: 1 }
  properties: { reserved: true }
}
resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/ready'
      appSettings: [
        { name: 'Storage__TableEndpoint', value: storage.properties.primaryEndpoints.table }
        { name: 'Storage__TableName', value: contributions.name }
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
      ]
    }
  }
}
resource tableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, app.id, 'table-data-contributor')
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  }
}
output appUrl string = 'https://${app.properties.defaultHostName}'
output deployedAppName string = app.name
output tableEndpoint string = storage.properties.primaryEndpoints.table
