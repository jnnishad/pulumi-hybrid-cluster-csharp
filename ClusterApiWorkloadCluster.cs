using Pulumi;
using Kubernetes = Pulumi.Kubernetes;
using Core = Pulumi.Kubernetes.Core.V1;
using CoreInputs = Pulumi.Kubernetes.Types.Inputs.Core.V1;
using MetaInputs = Pulumi.Kubernetes.Types.Inputs.Meta.V1;
using Yaml = Pulumi.Kubernetes.Yaml;

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
    ///
    /// Status: earlier revisions of this file hand-wrote each CR through
    /// Pulumi.Kubernetes.ApiExtensions.CustomResourceArgs, guessing at a
    /// property surface (ApiVersion/Kind/OtherFields as settable members)
    /// that doesn't actually exist on that type -- real `dotnet build` in
    /// CI caught it with ten compiler errors (CustomResourceArgs is
    /// abstract, ApiVersion/Kind are read-only, there's no OtherFields).
    /// Rewritten to apply plain YAML manifests via Pulumi.Kubernetes.Yaml.
    /// ConfigGroup instead, which is the documented, supported way to get
    /// arbitrary/CRD-backed resources like CAPI's into a cluster from C#
    /// without generating a full typed SDK for the CAPI CRDs.
    /// </summary>
    public class ClusterApiWorkloadCluster
    {
        public ClusterApiWorkloadCluster(
            string name,
            Kubernetes.Provider managementClusterProvider,
            Input<string> hcloudToken,
            int workerCount,
            string hetznerServerType,
            string azureLocation,
            string kubernetesVersion = "v1.29.4")
        {
            var providerOpts = new CustomResourceOptions { Provider = managementClusterProvider };

            var hcloudSecret = new Core.Secret($"{name}-hcloud-token", new CoreInputs.SecretArgs
            {
                Metadata = new MetaInputs.ObjectMetaArgs
                {
                    Name = "hetzner-token",
                    Namespace = "default",
                },
                StringData = new InputMap<string>
                {
                    { "hcloud", hcloudToken },
                },
            }, providerOpts);

            // Cluster: top-level CAPI object. controlPlaneRef and
            // infrastructureRef point at the KubeadmControlPlane and
            // AzureCluster manifests applied below -- previously the
            // control-plane side of this reference was never created,
            // leaving CAPI with a dangling reference it could never
            // reconcile. controlPlaneMachineTemplate/controlPlane below
            // close that gap.
            var clusterManifest = new Yaml.ConfigGroup($"{name}-cluster", new Yaml.ConfigGroupArgs
            {
                Yaml = $@"
apiVersion: cluster.x-k8s.io/v1beta1
kind: Cluster
metadata:
  name: {name}
spec:
  clusterNetwork:
    pods:
      cidrBlocks: [""192.168.0.0/16""]
  controlPlaneRef:
    apiVersion: controlplane.cluster.x-k8s.io/v1beta1
    kind: KubeadmControlPlane
    name: {name}-control-plane
  infrastructureRef:
    apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
    kind: AzureCluster
    name: {name}-azure
---
apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
kind: AzureCluster
metadata:
  name: {name}-azure
spec:
  location: {azureLocation}
  resourceGroup: {name}-workload-rg
  networkSpec:
    vnet:
      name: {name}-vnet
---
apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
kind: AzureMachineTemplate
metadata:
  name: {name}-control-plane-template
spec:
  template:
    spec:
      vmSize: Standard_D4s_v5
      osDisk:
        osType: Linux
        diskSizeGB: 128
        managedDisk:
          storageAccountType: Premium_LRS
---
apiVersion: controlplane.cluster.x-k8s.io/v1beta1
kind: KubeadmControlPlane
metadata:
  name: {name}-control-plane
spec:
  replicas: 3
  version: {kubernetesVersion}
  machineTemplate:
    infrastructureRef:
      apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
      kind: AzureMachineTemplate
      name: {name}-control-plane-template
  kubeadmConfigSpec:
    clusterConfiguration: {{}}
    initConfiguration:
      nodeRegistration:
        kubeletExtraArgs:
          cloud-provider: external
    joinConfiguration:
      nodeRegistration:
        kubeletExtraArgs:
          cloud-provider: external
---
apiVersion: cluster.x-k8s.io/v1beta1
kind: MachineDeployment
metadata:
  name: {name}-hetzner-md
spec:
  clusterName: {name}
  replicas: {workerCount}
  template:
    spec:
      clusterName: {name}
      infrastructureRef:
        apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
        kind: HCloudMachineTemplate
        name: {name}-hetzner-template
---
apiVersion: infrastructure.cluster.x-k8s.io/v1beta1
kind: HCloudMachineTemplate
metadata:
  name: {name}-hetzner-template
spec:
  template:
    spec:
      type: {hetznerServerType}
      imageName: ubuntu-22.04
",
            }, providerOpts);
        }
    }
}
