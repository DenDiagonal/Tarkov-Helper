using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    public class ItemIconLoadTests
    {
        [Fact]
        public void Test_LoadIcon_ByItemId()
        {
            // Sample popular items:
            // 5ba26835d4351e0035628ff5: 4.6x30mm AP SX
            // 5656eb674bdc2d35148b457c: 40mm VOG-25 grenade
            // 59f32bb586f774757e1e8442: BEAR dogtag
            var sampleIds = new[] { "5ba26835d4351e0035628ff5", "5656eb674bdc2d35148b457c", "59f32bb586f774757e1e8442" };

            var thread = new Thread(() =>
            {
                foreach (var id in sampleIds)
                {
                    var icon = ImageCacheService.Instance.GetLocalItemIcon(id);
                    Assert.NotNull(icon);
                    Assert.True(icon.PixelWidth > 0, $"PixelWidth for {id} should be > 0");
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}
