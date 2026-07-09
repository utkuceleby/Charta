using Xunit;

// HarfBuzz is a native library; keep its tests strictly sequential so no two native shaping calls
// overlap on the test host, which can crash the process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
