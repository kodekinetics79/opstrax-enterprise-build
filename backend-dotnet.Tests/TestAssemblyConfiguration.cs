using Xunit;

// The integration suite deliberately exercises global PostgreSQL DDL and ACL drift
// (constraints, policies, grants, roles). Those fixtures cannot safely overlap even
// when their test classes use different xUnit collections. Keep the assembly serial;
// CI parallelizes independent jobs, while this process preserves one coherent schema.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
