using BenchmarkDotNet.Running;

namespace tik4net.Benchmarks
{
    /// <summary>
    /// Entry point. Run from the repository root:
    /// <code>dotnet run -c Release --project tik4net.benchmarks</code>
    /// </summary>
    public static class Program
    {
        /// <summary>Runs every benchmark in this assembly (or the ones selected on the command line).</summary>
        public static void Main(string[] args)
            => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
