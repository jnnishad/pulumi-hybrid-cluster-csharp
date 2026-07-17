using Pulumi;
using Kubernetes = Pulumi.Kubernetes;

namespace HybridCluster
{
    /// <summary>
    /// Top-level stack: stands up an AKS management cluster, then applies
    /// the Cluster API resources that build the real hybrid workload
    /// cluster (Azure control plane + Hetzner Cloud workers).
    /// </summary>
    public class HybridClusterStack : Stack
    {
        [Output] public Output<string> ManagementClusterName { get; private set; }
        [Output] public Output<string> ManagementKubeConfig { get; private set; }
        [Output] public Output<string> WorkloadClusterName { get; private set; }

        public HybridClusterStack()
        {
            var config = new Config();
            var azureLocation = config.Get("azureLocation") ?? "westeurope";
            var clusterName = config.Get("clusterName") ?? "hybrid-prod";
            var hetznerWorkerCount = config.GetInt32("hetznerWorkerCount") ?? 4;
            var hetznerServerType = config.Get("hetznerServerType") ?? "cpx41";
            var hcloudToken = config.RequireSecret("hcloudToken");

            var management = new AzureManagementCluster(clusterName, azureLocation);

            var managementProvider = new Kubernetes.Provider($"{clusterName}-mgmt-provider", new Kubernetes.ProviderArgs
            {
                KubeConfig = management.KubeConfig,
            });

            var workload = new ClusterApiWorkloadCluster(
                clusterName,
                managementProvider,
                hcloudToken,
                hetznerWorkerCount,
                hetznerServerType,
                azureLocation);

            ManagementClusterName = management.Cluster.Name;
            ManagementKubeConfig = management.KubeConfig;
            WorkloadClusterName = Output.Create(clusterName);
        }
    }
}
