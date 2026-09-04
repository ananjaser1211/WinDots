// Platform tests share one desktop, one media-session manager, and one audio endpoint; running test classes in
// parallel makes them see each other's fake players. Run them strictly one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
