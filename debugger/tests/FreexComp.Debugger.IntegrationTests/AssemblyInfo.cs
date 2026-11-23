using FreexComp.Debugger.Tests.TestSupport;

// Phase trait for every test of this assembly (testing rules: traits).
[assembly: Trait("Phase", "6")]
// No process started by a test may outlive that test, nor the run (debugger testing DBG-334).
[assembly: ProcessLeakCheck]
[assembly: AssemblyFixture(typeof(ProcessLeakFixture))]
