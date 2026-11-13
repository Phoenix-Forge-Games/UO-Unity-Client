// One Britannia Reborn shard: an Azure Container Instance running the Linux server image, with the baked world and
// the saves on two Azure Files shares and a public IP for UDP 7770. Deployed by .github/workflows/shard.yml.

@description('Container group name (br-shard-test, br-shard-live).')
param name string

@description('Full image reference, e.g. myacr.azurecr.io/shard:<sha>.')
param image string

@description('The registry login server, e.g. myacr.azurecr.io.')
param acrLoginServer string

@description('Resource ID of a user-assigned identity with AcrPull on the registry.')
param acrIdentityId string

param location string = resourceGroup().location

@minValue(1)
param cpu int = 1

@minValue(1)
param memoryGb int = 4

@description('DNS label: the shard is <dnsLabel>.<region>.azurecontainer.io.')
param dnsLabel string

param storageAccountName string

@secure()
param storageAccountKey string

@description('File share holding WorldData (mounted read-only).')
param worldShare string

@description('File share holding the saves (accounts, houses, boats...).')
param dataShare string

@description('Website API base for logins, e.g. https://britanniareborn.com/api.')
param accountApi string

@secure()
param shardKey string

@allowed([
  'Always'
  'OnFailure'
  'Never'
])
param restartPolicy string = 'Always'

var port = 7770

resource shard 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${acrIdentityId}': {}
    }
  }
  properties: {
    osType: 'Linux'
    restartPolicy: restartPolicy
    imageRegistryCredentials: [
      {
        server: acrLoginServer
        identity: acrIdentityId
      }
    ]
    ipAddress: {
      type: 'Public'
      dnsNameLabel: dnsLabel
      ports: [
        {
          port: port
          protocol: 'UDP'
        }
      ]
    }
    containers: [
      {
        name: 'shard'
        properties: {
          image: image
          resources: {
            requests: {
              cpu: cpu
              memoryInGB: memoryGb
            }
          }
          ports: [
            {
              port: port
              protocol: 'UDP'
            }
          ]
          environmentVariables: [
            {
              name: 'BR_WORLD_DIR'
              value: '/mnt/world'
            }
            {
              name: 'BR_DATA_DIR'
              value: '/mnt/data'
            }
            {
              name: 'BR_ACCOUNT_API'
              value: accountApi
            }
            {
              name: 'BR_SHARD_KEY'
              secureValue: shardKey
            }
          ]
          volumeMounts: [
            {
              name: 'world'
              mountPath: '/mnt/world'
              readOnly: true
            }
            {
              name: 'data'
              mountPath: '/mnt/data'
              readOnly: false
            }
          ]
        }
      }
    ]
    volumes: [
      {
        name: 'world'
        azureFile: {
          shareName: worldShare
          storageAccountName: storageAccountName
          storageAccountKey: storageAccountKey
          readOnly: true
        }
      }
      {
        name: 'data'
        azureFile: {
          shareName: dataShare
          storageAccountName: storageAccountName
          storageAccountKey: storageAccountKey
          readOnly: false
        }
      }
    ]
  }
}

output fqdn string = shard.properties.ipAddress.fqdn
output ip string = shard.properties.ipAddress.ip
