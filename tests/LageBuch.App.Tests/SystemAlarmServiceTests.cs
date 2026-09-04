using LageBuch.App.Services;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Tests;

public class SystemAlarmServiceTests
{
    [Fact]
    public void Dispose_removes_the_temp_wav_file_TempFileFor_wrote()
    {
        // TempFileFor is only ever called on non-Windows — Windows plays PlaySound straight from
        // memory, so there's no temp file for Dispose to clean up there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new SystemAlarmService();
        var path = service.TempFileFor(AlarmSound.TaskDue, new byte[] { 1, 2, 3 });

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        service.Dispose();

        Assert.False(File.Exists(path));
    }
}
