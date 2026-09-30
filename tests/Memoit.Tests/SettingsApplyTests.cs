using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class SettingsApplyTests
{
    [Fact]
    public void StartupFailureDoesNotSaveOtherSettings()
    {
        bool saved = false;
        int attempts = 0;
        Assert.Throws<IOException>(() => SettingsApply.Save(new(), true, () => false,
            _ => { if (++attempts == 1) throw new IOException("registry denied"); }, _ => saved = true));
        Assert.False(saved);
    }

    [Fact]
    public void StartupPartialWriteFailureRollsBack()
    {
        bool startup = false, saved = false;
        int attempts = 0;
        Assert.Throws<IOException>(() => SettingsApply.Save(new(), true, () => startup,
            value => { startup = value; if (++attempts == 1) throw new IOException("legacy cleanup failed"); },
            _ => saved = true));
        Assert.False(startup);
        Assert.False(saved);
    }

    [Fact]
    public void FileFailureRestoresStartupAndReportsFailure()
    {
        bool startup = false;
        Assert.Throws<IOException>(() => SettingsApply.Save(new(), true, () => startup,
            value => startup = value, _ => throw new IOException("disk full")));
        Assert.False(startup);
    }

    [Fact]
    public void UnchangedStartupDoesNotWriteRegistry()
    {
        bool saved = false;
        SettingsApply.Save(new(), false, () => false,
            _ => throw new InvalidOperationException("unexpected registry write"), _ => saved = true);
        Assert.True(saved);
    }

    [Fact]
    public void RollbackFailureReportsBothCauses()
    {
        int writes = 0;
        var error = Assert.Throws<AggregateException>(() => SettingsApply.Save(new(), true, () => false,
            _ => { if (++writes == 2) throw new IOException("rollback failed"); },
            _ => throw new IOException("save failed")));
        Assert.Equal(2, error.InnerExceptions.Count);
    }
}
