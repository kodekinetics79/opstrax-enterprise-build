namespace Opstrax.Tests;

// These suites share and, in one corruption-fixture test, temporarily alter the
// global device_installations exclusion constraints. Running them concurrently
// can let another suite write during that deliberate gap and leave the database
// with overlapping installation history. Keep this narrow schema-mutating group
// exclusive while the rest of the test assembly remains parallel.
[CollectionDefinition("fleet-identity-schema", DisableParallelization = true)]
public sealed class FleetIdentitySchemaCollection;
