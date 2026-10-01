using Microsoft.VisualStudio.TestTools.UnitTesting;

// Test classes run in parallel, one worker per core; the tests of one class run one after another, so a class may
// keep state in its instance or its statics. A class that cannot share the process with others says so with
// [DoNotParallelize] — MSTest runs those afterwards, alone:
//
//  - a test that bounds how long something takes (a Stopwatch against a budget, a receive timeout against a fake
//    peer): a busy thread pool stretches the measurement, and a CI runner has two cores;
//  - a test that changes process-wide state other tests read or write (a registered type converter, an environment
//    variable, the ServicePointManager's connection limit, the TikWireTrace sink).
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
