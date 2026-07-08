# Tears down the dev environment completely, including the soft-deleted
# shadows that block redeployment:
#  - Azure OpenAI keeps deleted accounts 48h and reserves their subdomain
#    (redeploy fails with CustomDomainInUse)
#  - Key Vault keeps deleted vaults 7 days and reserves their name
# Both must be purged because resource names derive from uniqueString(rg.id),
# so a redeploy always wants the exact same names back.
param(
    [string]$ResourceGroup = 'rg-knowvault-dev'
)

$ErrorActionPreference = 'Stop'

Write-Host "Deleting resource group $ResourceGroup..."
az group delete --name $ResourceGroup --yes

Write-Host 'Purging soft-deleted Cognitive Services accounts...'
az cognitiveservices account list-deleted --query '[].{name:name, location:location}' -o json |
    ConvertFrom-Json | ForEach-Object {
        az cognitiveservices account purge --location $_.location --resource-group $ResourceGroup --name $_.name
        Write-Host "  purged $($_.name)"
    }

Write-Host 'Purging soft-deleted key vaults...'
az keyvault list-deleted --query '[].{name:name, location:properties.location}' -o json |
    ConvertFrom-Json | ForEach-Object {
        az keyvault purge --name $_.name --location $_.location
        Write-Host "  purged $($_.name)"
    }

Write-Host 'Teardown complete. Redeploy with scripts\deploy-dev.ps1.'
