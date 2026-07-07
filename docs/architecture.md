# Architecture

```
 Pulumi (C#) apply
        │
        ▼
 AzureManagementCluster
   ResourceGroup + AKS ("management cluster")
   runs: cluster-api, cluster-api-provider-azure (CAPZ),
         cluster-api-provider-hetzner (CAPH)   [installed out of band, see below]
        │
        │ Pulumi.Kubernetes.Provider, authenticated with the AKS kubeconfig
        ▼
 ClusterApiWorkloadCluster (applies CAPI custom resources)
   Cluster ──► controlPlaneRef: KubeadmControlPlane (Azure VMs, via CAPZ)
           └─► infrastructureRef: AzureCluster
   MachineDeployment ──► HCloudMachineTemplate (Hetzner Cloud servers, via CAPH)
        │
        ▼
   Real workload cluster: control plane on Azure, workers on Hetzner
```

**Why a management cluster instead of applying CAPI resources to the
workload cluster itself:** Cluster API's controllers need somewhere to
run *before* the cluster they're managing exists. A small, cheap AKS
cluster hosts `cluster-api`, `cluster-api-provider-azure`, and
`cluster-api-provider-hetzner`; it reconciles the `Cluster` /
`MachineDeployment` custom resources applied by
`ClusterApiWorkloadCluster.cs` into real Azure VMs (control plane) and
Hetzner Cloud servers (workers).

**What Pulumi owns vs. what CAPI owns:** Pulumi provisions the
management cluster and applies the CAPI custom resources (declarative
desired state). From there, the CAPI + CAPZ + CAPH controllers own the
actual machine lifecycle — provisioning, health checks, and remediation
of the workload cluster's nodes. This mirrors the real split of
responsibility from the NEC India environment: Pulumi/C# for
provisioning, Cluster API for ongoing machine lifecycle.

**Prerequisites not covered by this stack** (installed once into the
management cluster, typically via `clusterctl init`):

```bash
clusterctl init --infrastructure azure,hetzner
```

**Secrets:** the Hetzner Cloud API token is passed as a Pulumi secret
config value (`pulumi config set --secret hcloudToken <token>`) and
materialized as a Kubernetes Secret consumed by CAPH — never written to
the manifests or state in plaintext.
