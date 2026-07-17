using System.Collections.Generic;
using Pulumi;
using AzureNative = Pulumi.AzureNative;
using Resources = Pulumi.AzureNative.Resources;
using ContainerService = Pulumi.AzureNative.ContainerService;

namespace HybridCluster
{
    /// <summary>
    /// A small AKS cluster whose only job is to host the Cluster API
    /// controllers (core CAPI + CAPZ + CAPH). This is the "management
    /// cluster" pattern: it never runs application workloads, it just
    /// reconciles the CAPI custom resources defined in
    /// ClusterApiWorkloadCluster that create the real Azure control-plane
    /// machines and Hetzner worker nodes.
    /// </summary>
    public class AzureManagementCluster
    {
        public Resources.ResourceGroup ResourceGroup { get; }
        public ContainerService.ManagedCluster Cluster { get; }
        public Output<string> KubeConfig { get; }

        public AzureManagementCluster(string name, string location)
        {
            ResourceGroup = new Resources.ResourceGroup($"{name}-mgmt-rg", new Resources.ResourceGroupArgs
            {
                ResourceGroupName = $"{name}-mgmt-rg",
                Location = location,
            });

            Cluster = new ContainerService.ManagedCluster($"{name}-mgmt", new ContainerService.ManagedClusterArgs
            {
                ResourceGroupName = ResourceGroup.Name,
                ResourceName = $"{name}-mgmt",
                Location = location,
                DnsPrefix = $"{name}-mgmt",
                AgentPoolProfiles = new[]
                {
                    new ContainerService.Inputs.ManagedClusterAgentPoolProfileArgs
                    {
                        Name = "system",
                        Count = 2,
                        VmSize = "Standard_D4s_v5",
                        Mode = "System",
                        OsType = "Linux",
                    },
                },
                Identity = new ContainerService.Inputs.ManagedClusterIdentityArgs
                {
                    Type = ContainerService.ResourceIdentityType.SystemAssigned,
                },
            });

            KubeConfig = GetKubeConfig(ResourceGroup.Name, Cluster.Name);
        }

        private static Output<string> GetKubeConfig(Output<string> resourceGroupName, Output<string> clusterName)
        {
            var creds = Output.Tuple(resourceGroupName, clusterName).Apply(names =>
                ContainerService.ListManagedClusterUserCredentials.InvokeAsync(
                    new ContainerService.ListManagedClusterUserCredentialsArgs
                    {
                        ResourceGroupName = names.Item1,
                        ResourceName = names.Item2,
                    }));

            return creds.Apply(c =>
            {
                var encoded = c.Kubeconfigs[0].Value;
                var bytes = System.Convert.FromBase64String(encoded);
                return System.Text.Encoding.UTF8.GetString(bytes);
            });
        }
    }
}
