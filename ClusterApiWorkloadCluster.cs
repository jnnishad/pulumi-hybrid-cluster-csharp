using System.Collections.Generic;
using Pulumi;
using Kubernetes = Pulumi.Kubernetes;
using Core = Pulumi.Kubernetes.Core.V1;
using ApiExtensions = Pulumi.Kubernetes.ApiExtensions;

namespace HybridCluster
{
    /// <summary>
    /// Applies the Cluster API custom resources that describe the actual
    /// workload cluster: control-plane machines on Azure (via CAPZ) and
    /// worker nodes on Hetzner Cloud (via CAPH). These CRs are reconciled
    /// by the controllers running in the AKS management cluster — Pulumi's
    /// job here is just to get the desired-state manifests applied, the
    /// same as `kubectl apply -f cluster.yaml` would, but versioned and
    /// diffed like everything else in this stack.
    /// </summary>
    public class ClusterApiWorkloadCluster
    {
        public ClusterApiWorkloadCluster(
            string name,
            Kubernetes.Provider managementClusterProvider,
            Input<string> hcloudToken,
            int workerCount,
            string hetznerServerType,
            string azureLocation)
        {
            var providerOpts = new CustomResourceOptions { Provider = managementClusterProvider };

            var hcloudSecret = new Core.Secret($"{name}-hcloud-token", new Core.SecretArgs
            {
                Metadata = new Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                {
                    Name = "hetzner-token",
                    Namespace = "default",
                },
                StringData = new InputMap<string>
                {
                    { "hcloud", hcloudToken },
                },
            }, providerOpts);

            var cluster = new ApiExtensions.CustomResource($"{name}-cluster", new ApiExtensions.CustomResourceArgs
            {
                ApiVersion = "cluster.x-k8s.io/v1beta1",
                Kind = "Cluster",
                Metadata = new Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs { Name = name },
                OtherFields =
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["clusterNetwork"] = new Dictionary<string, object>
                        {
                            ["pods"] = new Dictionary<string, object> { ["cidrBlocks"] = new[] { "192.168.0.0/16" } },
                        },
                        ["controlPlaneRef"] = new Dictionary<string, object>
                        {
                            ["apiVersion"] = "controlplane.cluster.x-k8s.io/v1beta1",
                            ["kind"] = "KubeadmControlPlane",
                            ["name"] = $"{name}-control-plane",
                        },
                        ["infrastructureRef"] = new Dictionary<string, object>
                        {
                            ["apiVersion"] = "infrastructure.cluster.x-k8s.io/v1beta1",
                            ["kind"] = "AzureCluster",
                            ["name"] = $"{name}-azure",
                        },
                    },
                },
            }, providerOpts);

            var azureCluster = new ApiExtensions.CustomResource($"{name}-azure-cluster", new ApiExtensions.CustomResourceArgs
            {
                ApiVersion = "infrastructure.cluster.x-k8s.io/v1beta1",
                Kind = "AzureCluster",
                Metadata = new Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs { Name = $"{name}-azure" },
                OtherFields =
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["location"] = azureLocation,
                        ["resourceGroup"] = $"{name}-workload-rg",
                        ["networkSpec"] = new Dictionary<string, object>
                        {
                            ["vnet"] = new Dictionary<string, object> { ["name"] = $"{name}-vnet" },
                        },
                    },
                },
            }, providerOpts);

            // Worker MachineDeployment backed by the Hetzner Cloud infrastructure
            // provider (CAPH) — this is what actually creates the Hetzner servers.
            var hetznerWorkers = new ApiExtensions.CustomResource($"{name}-hetzner-workers", new ApiExtensions.CustomResourceArgs
            {
                ApiVersion = "cluster.x-k8s.io/v1beta1",
                Kind = "MachineDeployment",
                Metadata = new Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs { Name = $"{name}-hetzner-md" },
                OtherFields =
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["clusterName"] = name,
                        ["replicas"] = workerCount,
                        ["template"] = new Dictionary<string, object>
                        {
                            ["spec"] = new Dictionary<string, object>
                            {
                                ["clusterName"] = name,
                                ["infrastructureRef"] = new Dictionary<string, object>
                                {
                                    ["apiVersion"] = "infrastructure.cluster.x-k8s.io/v1beta1",
                                    ["kind"] = "HCloudMachineTemplate",
                                    ["name"] = $"{name}-hetzner-template",
                                },
                            },
                        },
                    },
                },
            }, providerOpts);

            var hetznerMachineTemplate = new ApiExtensions.CustomResource($"{name}-hetzner-template", new ApiExtensions.CustomResourceArgs
            {
                ApiVersion = "infrastructure.cluster.x-k8s.io/v1beta1",
                Kind = "HCloudMachineTemplate",
                Metadata = new Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs { Name = $"{name}-hetzner-template" },
                OtherFields =
                {
                    ["spec"] = new Dictionary<string, object>
                    {
                        ["template"] = new Dictionary<string, object>
                        {
                            ["spec"] = new Dictionary<string, object>
                            {
                                ["type"] = hetznerServerType,
                                ["imageName"] = "ubuntu-22.04",
                            },
                        },
                    },
                },
            }, providerOpts);
        }
    }
}
