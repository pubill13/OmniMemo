namespace Memoit.Services;

public static class SettingsApply
{
    public static void Save(LayoutSettings settings, bool autoStart, Func<bool> readStartup,
        Action<bool> writeStartup, Action<LayoutSettings> saveSettings)
    {
        bool previous = readStartup();
        try
        {
            if (previous != autoStart) writeStartup(autoStart);
            saveSettings(settings);
        }
        catch (Exception saveError)
        {
            if (previous != autoStart)
            {
                try { writeStartup(previous); }
                catch (Exception rollbackError)
                { throw new AggregateException("설정 저장과 자동 실행 복구에 실패했습니다.", saveError, rollbackError); }
            }
            throw;
        }
    }
}
