using System;
using Forens.Common.Time;
using Xunit;

namespace Forens.Core.Tests.Common
{
    public class TimeRangeTests
    {
        [Theory]
        [InlineData("2026-05-06T14:22:33Z")]
        [InlineData("2026-05-06T14:22:33.456Z")]
        [InlineData("2026-05-06T14:22:33+02:00")]
        [InlineData("2026-05-06T14:22:33-05:00")]
        public void ParseStrictUtc_accepts_iso_with_offset_or_Z(string input)
        {
            var dt = TimeRange.ParseStrictUtc(input, "ts");
            Assert.Equal(TimeSpan.Zero, dt.Offset);
        }

        [Theory]
        [InlineData("2026-05-06T14:22:33")]
        [InlineData("2026-05-06 14:22:33")]
        public void ParseStrictUtc_rejects_naked_local(string input)
        {
            Assert.Throws<FormatException>(() => TimeRange.ParseStrictUtc(input, "ts"));
        }

        [Fact]
        public void Constructor_rejects_from_after_to()
        {
            Assert.Throws<ArgumentException>(() => new TimeRange(
                DateTimeOffset.Parse("2026-05-10T00:00:00Z"),
                DateTimeOffset.Parse("2026-05-01T00:00:00Z")));
        }

        [Fact]
        public void Includes_handles_open_ended_ranges()
        {
            var ts = DateTimeOffset.Parse("2026-05-05T12:00:00Z");
            Assert.True(new TimeRange(null, null).Includes(ts));
            Assert.True(new TimeRange(DateTimeOffset.Parse("2026-05-01T00:00:00Z"), null).Includes(ts));
            Assert.False(new TimeRange(DateTimeOffset.Parse("2026-05-10T00:00:00Z"), null).Includes(ts));
            Assert.True(new TimeRange(null, DateTimeOffset.Parse("2026-05-10T00:00:00Z")).Includes(ts));
            Assert.False(new TimeRange(null, DateTimeOffset.Parse("2026-05-01T00:00:00Z")).Includes(ts));
        }
    }
}
