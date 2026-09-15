//
// ProgramEndToEndTests redirects the process-global Console.Out/Error
// around each Program.Main call, which is unsafe if another test class
// writes to the console concurrently (xUnit parallelizes across test
// classes by default). Disabling parallelization keeps the whole suite
// safe from that race; the suite is small enough (well under a second)
// that running it sequentially costs nothing meaningful.
//
[assembly: CollectionBehavior(DisableTestParallelization = true)]
