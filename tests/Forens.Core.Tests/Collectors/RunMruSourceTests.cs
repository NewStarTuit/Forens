using Forens.Core.Collectors;
using Xunit;

namespace Forens.Core.Tests.Collectors
{
    public class RunMruSourceTests
    {
        [Fact]
        public void Metadata_declares_expected_capabilities()
        {
            var src = new RunMruSource();
            Assert.Equal("runmru", src.Metadata.Id);
            Assert.Equal(Forens.Core.Collection.Category.User, src.Metadata.Category);
            Assert.False(src.Metadata.RequiresElevation);
        }

        [Theory]
        [InlineData(@"calc\1", "calc")]
        [InlineData(@"C:\path\to\thing\1", @"C:\path\to\thing")]
        [InlineData("nothing-special", "nothing-special")]
        [InlineData(null, null)]
        [InlineData("", "")]
        public void StripBackslash1_removes_trailing_terminator(string input, string expected)
        {
            Assert.Equal(expected, RunMruSource.StripBackslash1(input));
        }

        [Theory]
        [InlineData("fadcebg", "f", 0)]
        [InlineData("fadcebg", "a", 1)]
        [InlineData("fadcebg", "g", 6)]
        [InlineData("fadcebg", "z", -1)]
        [InlineData("", "a", -1)]
        [InlineData(null, "a", -1)]
        [InlineData("abc", "ab", -1)] // slot must be one char
        public void ComputeOrder_returns_index_in_MRUList_or_minus_one(string mruList, string slot, int expected)
        {
            Assert.Equal(expected, RunMruSource.ComputeOrder(mruList, slot));
        }
    }
}
