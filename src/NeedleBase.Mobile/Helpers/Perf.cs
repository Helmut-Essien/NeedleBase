using System.Diagnostics;

namespace NeedleBase.Mobile.Helpers;

/// <summary>Debug-only timing log (logcat tag NB_PERF). No PII: names and milliseconds only.</summary>
public static class Perf
{
    [Conditional("DEBUG")]
    public static void Log(string what, long ms)
    {
#if ANDROID
        Android.Util.Log.Info("NB_PERF", $"{what} {ms}ms");
#else
        Debug.WriteLine($"NB_PERF {what} {ms}ms");
#endif
    }
}
