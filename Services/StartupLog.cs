using System;
using System.IO;

namespace SaveSpace.Services
{
    /// <summary>
    /// Schrijft elke opstartstap naar %TEMP%\SaveSpace-opstart.log. Dit werkt ook als de
    /// gewone logmap of de bibliotheek niet beschikbaar is.
    /// </summary>
    public static class StartupLog
    {
        public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "SaveSpace-opstart.log");

        public static void Mark(string step)
        {
            try
            {
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {step}{Environment.NewLine}");
            }
            catch { }
        }

        public static void Fail(object exception)
        {
            Mark("FOUT: " + exception);
        }
    }
}
