using System;
using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class BrowserBookmarksSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new BrowserBookmarksSource();
            Assert.Equal("browser-bookmarks", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.Browser, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
        }

        [Fact]
        public void ChromiumTimeUtc_decodes_microseconds_since_1601()
        {
            // 2026-01-01T00:00:00Z is 13367808000000000 ticks since 1601-01-01.
            // Chromium stores microseconds-since-1601, so the value is ticks/10.
            var jan1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long ticks = jan1.Subtract(new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks;
            long micros = ticks / 10;
            var dto = BrowserBookmarksSource.ChromiumTimeUtc(micros.ToString());
            Assert.NotNull(dto);
            Assert.Equal(2026, dto.Value.Year);
            Assert.Equal(1, dto.Value.Month);
            Assert.Equal(1, dto.Value.Day);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("not a number")]
        [InlineData("-1")]
        public void ChromiumTimeUtc_returns_null_for_invalid_input(string raw)
        {
            Assert.Null(BrowserBookmarksSource.ChromiumTimeUtc(raw));
        }
    }
}
