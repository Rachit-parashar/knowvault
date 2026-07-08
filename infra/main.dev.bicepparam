using 'main.bicep'

param environmentName = 'dev'
param location = 'eastus2'
param sqlAdminLogin = 'knowvaultadmin'
// Pass the password at deploy time:
//   az deployment sub create ... --parameters main.dev.bicepparam sqlAdminPassword=<value>
param sqlAdminPassword = ''
