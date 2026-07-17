# pulumi-hybrid-cluster-csharp

A Pulumi program, written in C#, that provisions a **hybrid Kubernetes
cluster**: control plane on **Azure**, worker nodes on **Hetzner
Cloud**, wired together with **Cluster API** — the pattern used to run
production Kubernetes at NEC India.

## Why Pulumi + C# instead of Terraform/HCL

Most of the infrastructure in this GitHub profile is Terraform, but this
particular pattern — Pulumi driving Cluster API's Kubernetes-native
machine lifecycle — was built in C# because it needed real control flow
(conditional CR composition, typed config, `Output<T>` chaining across
the AKS kubeconfig into the Kubernetes provider) that's awkward in pure
HCL. See [`terraform-multicloud-infra`](https://github.com/jnnishad/terraform-multicloud-infra)
for the equivalent single-cloud AKS/EKS provisioning in Terraform.

## What it builds

1. **`AzureManagementCluster.cs`** — a small AKS cluster whose only job
   is to host the Cluster API controllers (core CAPI + CAPZ + CAPH).
2. **`ClusterApiWorkloadCluster.cs`** — applies the CAPI custom resources
   (`Cluster`, `AzureCluster`, `KubeadmControlPlane` ref, `MachineDeployment`,
   `HCloudMachineTemplate`) that describe the real workload cluster:
   control-plane machines on Azure, worker nodes on Hetzner Cloud.
3. **`HybridClusterStack.cs`** — composes the two and exports the
   management cluster's kubeconfig and cluster names.

See `docs/architecture.md` for the full diagram and the reasoning behind
the management-cluster split.

## Usage

```bash
# one-time: install CAPI + provider CRDs/controllers into the management
# cluster once it exists (clusterctl, not part of this Pulumi program)
pulumi up   # creates the AKS management cluster + applies CAPI resources
clusterctl init --infrastructure azure,hetzner --kubeconfig <(pulumi stack output ManagementKubeConfig)

pulumi config set azureLocation westeurope
pulumi config set clusterName hybrid-prod
pulumi config set hetznerWorkerCount 4
pulumi config set --secret hcloudToken <your-hetzner-api-token>

pulumi up
```

## Structure

```
Program.cs                     Entry point — Deployment.RunAsync<HybridClusterStack>()
HybridClusterStack.cs           Top-level stack: config, composition, outputs
AzureManagementCluster.cs        AKS cluster hosting the CAPI controllers
ClusterApiWorkloadCluster.cs      CAPI custom resources: Azure control plane + Hetzner workers
docs/architecture.md              Diagram + design rationale
```

## Related repos

- [`terraform-multicloud-infra`](https://github.com/jnnishad/terraform-multicloud-infra) — the Terraform/HCL equivalent for single-cloud AKS/EKS
- [`k8s-observability-stack`](https://github.com/jnnishad/k8s-observability-stack) — what gets deployed onto the resulting workload cluster

## Status

CI (`dotnet build`) now runs on every push -- it wasn't wired up
initially, and would have caught a real bug sooner: the `Cluster`
custom resource's `controlPlaneRef` pointed at a `KubeadmControlPlane`
named `{name}-control-plane` that nothing in the program ever actually
created, so Cluster API had a dangling reference it could never
reconcile. Fixed by adding the missing `KubeadmControlPlane` and its
backing `AzureMachineTemplate` in `ClusterApiWorkloadCluster.cs`.

## License

MIT — see [LICENSE](LICENSE).
