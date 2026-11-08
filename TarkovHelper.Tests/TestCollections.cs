using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Explicit collection definition for tests that mutate process-global singleton state
    /// (such as DatabaseUpdateService.Instance and DatabaseTransactionApplier.Instance)
    /// to guarantee strictly serialized execution without cross-test thread interference.
    /// </summary>
    [CollectionDefinition("DatabaseUpdateServiceCollection", DisableParallelization = true)]
    public class DatabaseUpdateServiceCollectionDefinition
    {
    }
}
