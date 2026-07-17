using Pulumi;

// Entry point — Pulumi's dotnet runtime discovers and runs the Stack
// defined in HybridClusterStack.cs. Kept minimal on purpose: composition
// happens inside the stack class so it's unit-testable independent of
// the Pulumi CLI (see docs/architecture.md).
return await Deployment.RunAsync<HybridCluster.HybridClusterStack>();
