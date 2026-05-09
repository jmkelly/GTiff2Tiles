using System.Diagnostics;
using System.Globalization;
using System.Threading;
using GTiff2Tiles.Core.Localization;

// ReSharper disable MemberCanBePrivate.Global

namespace GTiff2Tiles.Core.Helpers;

/// <summary>
/// Class with methods to simplify progress-reporting
/// </summary>
public static class ProgressHelper
{
    internal static TileProgressReporter CreateTileProgressReporter(int tilesCount,
                                                                   IProgress<double> progress = null,
                                                                   Stopwatch stopwatch = null,
                                                                   Action<string> reporter = null)
    {
        return progress == null && reporter == null
            ? null
            : new TileProgressReporter(tilesCount, progress, stopwatch, reporter);
    }

    /// <summary>
    /// Calculate estimated time left, based on your current progress and time from start
    /// </summary>
    /// <param name="percentage">Current progress;
    /// <remarks><para/>Should be in range (0.0, 100.0]</remarks></param>
    /// <param name="stopwatch">Time passed from the start</param>
    /// <returns>Estimated <see cref="TimeSpan"/> left</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public static TimeSpan GetEstimatedTimeLeft(double percentage, Stopwatch stopwatch)
    {
        #region Preconditions checks

        if (percentage <= 0.0 || percentage > 100.0) throw new ArgumentOutOfRangeException(nameof(percentage));
        ArgumentNullException.ThrowIfNull(stopwatch);

        #endregion

        double timePassed = stopwatch.ElapsedMilliseconds;
        double estimatedAllTime = 100.0 * timePassed / percentage;
        double estimatedTimeLeft = estimatedAllTime - timePassed;

        return TimeSpan.FromMilliseconds(estimatedTimeLeft);
    }

    /// <summary>
    /// Prints estimated time left
    /// </summary>
    /// <param name="percentage">Current progress;
    /// <remarks><para/>Should be in range (0.0, 100.0]</remarks></param>
    /// <param name="stopwatch">Time passed from the start;
    /// <remarks><para/>If set to <see langword="null"/> no time printed</remarks></param>
    /// <param name="reporter">Delegate to work with reported string
    /// <remarks><para/>E.g. <see cref="Console.WriteLine(string)"/>; if set to <see langword="null"/> no time printed</remarks></param>
    public static void PrintEstimatedTimeLeft(double percentage, Stopwatch stopwatch = null, Action<string> reporter = null)
    {
        #region Preconditions checks

        // Don't print anything, no need to throw exception
        if (stopwatch == null) return;
        if (reporter == null) return;

        #endregion

        TimeSpan timeSpan = GetEstimatedTimeLeft(percentage, stopwatch);

        string reportString = string.Format(CultureInfo.InvariantCulture, Strings.EstimatedTime, Environment.NewLine,
                                            timeSpan.Days, timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds,
                                            timeSpan.Milliseconds);
        reporter.Invoke(reportString);
    }
}

internal sealed class TileProgressReporter
{
    private const int MaxProgressUpdates = 100;

    private readonly object _reportLock = new();
    private readonly int _tilesCount;
    private readonly int _reportInterval;
    private readonly IProgress<double> _progress;
    private readonly Stopwatch _stopwatch;
    private readonly Action<string> _reporter;

    private int _completedTiles;
    private int _lastReportedTileCount;

    public TileProgressReporter(int tilesCount, IProgress<double> progress = null,
                                Stopwatch stopwatch = null, Action<string> reporter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilesCount);

        _tilesCount = tilesCount;
        _progress = progress;
        _stopwatch = stopwatch;
        _reporter = reporter;
        _reportInterval = Math.Max(1, tilesCount / MaxProgressUpdates);
    }

    public void Advance()
    {
        int completedTiles = Interlocked.Increment(ref _completedTiles);

        int reportTileCount = completedTiles == _tilesCount
            ? _tilesCount
            : completedTiles - completedTiles % _reportInterval;

        if (reportTileCount <= 0) return;
        if (reportTileCount <= Volatile.Read(ref _lastReportedTileCount)) return;

        lock (_reportLock)
        {
            if (reportTileCount <= _lastReportedTileCount) return;

            _lastReportedTileCount = reportTileCount;

            double percentage = (double)reportTileCount / _tilesCount * 100.0;

            _progress?.Report(percentage);
            ProgressHelper.PrintEstimatedTimeLeft(percentage, _stopwatch, _reporter);
        }
    }
}
