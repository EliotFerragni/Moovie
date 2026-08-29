// The interface language is global state that a few tests switch. Other tests assert on English
// messages, so running collections in parallel would make those flaky. The whole suite runs in
// well under a second, so serialising it costs nothing worth having.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
