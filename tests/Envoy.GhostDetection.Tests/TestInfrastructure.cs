using Xunit;

// The ghost-detection suites exercise real static dataset hooks
// (WarnDataset.LoadOverride / PermIndex.LoadOverride / WarnDataset.Replace).
// Shared mutable statics + parallel test classes = real flakes, not nose
// pollution in theory but in practice (observed with WarnRefresherTests vs
// HiringFreezeSignalTests). The suites are tiny (a few hundred ms total), so
// run them serially and stay deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
