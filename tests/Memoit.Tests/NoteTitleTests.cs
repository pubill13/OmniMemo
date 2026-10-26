using Memoit.Models;
using Xunit;

namespace Memoit.Tests;

public sealed class NoteTitleTests
{
    [Theory]
    [InlineData("", "새 메모")]
    [InlineData(" \r\n\t\r \n", "새 메모")]
    [InlineData("\r\n \t\n  한글 제목  \r나머지", "한글 제목")]
    [InlineData("\u2003제목\u2003\n본문", "제목")]
    [InlineData("first\rsecond", "first")]
    public void TitleKeepsFirstNonblankTrimmedLine(string body, string expected)
        => Assert.Equal(expected, new Note { Body = body }.Title);

    [Fact]
    public void LongLineTitleAllocatesOnlyItsPreview()
    {
        var note = new Note { Body = "  " + new string('가', 1_000_000) + "  \n본문" };
        Assert.Equal(new string('가', 80), note.Title);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string title = note.Title;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(80, title.Length);
        Assert.True(allocated < 1024, $"Title allocated {allocated} bytes for an 80-character preview.");
    }

    [Fact]
    public void TruncationKeepsWhitespaceInsideTheFirstEightyCharacters()
    {
        string first = new string('a', 78) + "  ";
        Assert.Equal(first, new Note { Body = first + "z" }.Title);
        Assert.Equal(new string('a', 78), new Note { Body = first + "\r\nnext" }.Title);
    }
}
