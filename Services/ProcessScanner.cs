using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.IndexJelly.Api;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IndexJelly.Services;

/// <summary>
/// Finds the ffmpeg / ffprobe processes the server runs for indexing (probing, trickplay, chapter images,
/// keyframes, audio normalization…) and matches their input file to a library item. This is what makes it
/// possible to show which media a task is working on. Reads /proc, so it works on Linux (Docker) only.
/// Playback transcodes are left out; StreamJelly shows those.
/// </summary>
internal static class ProcessScanner
{
    public static bool Available => OperatingSystem.IsLinux() && Directory.Exists("/proc");

    public static List<ProcessActivity> Scan(ILibraryManager libraryManager, bool showPaths, ILogger logger)
    {
        var result = new List<ProcessActivity>();
        if (!Available)
        {
            return result;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IndexJelly: could not list processes");
            return result;
        }

        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    var name = p.ProcessName;
                    var isProbe = name.StartsWith("ffprobe", StringComparison.OrdinalIgnoreCase);
                    if (!isProbe && !name.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var args = ReadArgs(p.Id);
                    if (args.Length == 0)
                    {
                        continue;
                    }

                    var kind = Classify(isProbe, string.Join(' ', args));
                    if (kind is null)
                    {
                        continue;
                    }

                    var activity = new ProcessActivity { Pid = p.Id, Kind = kind };
                    try
                    {
                        activity.StartedUtc = p.StartTime.ToUniversalTime();
                    }
                    catch
                    {
                        // not allowed to read it; elapsed time is then not shown
                    }

                    var input = InputOf(args);
                    if (input is not null)
                    {
                        var item = FindItem(libraryManager, input);
                        if (item is not null)
                        {
                            activity.Item = showPaths ? item : ItemRefs.WithoutPath(item);
                        }
                        else
                        {
                            activity.FileName = showPaths ? input : Path.GetFileName(input);
                        }
                    }

                    result.Add(activity);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "IndexJelly: could not inspect process {Pid}", p.Id);
                }
            }
        }

        return result.OrderBy(a => a.StartedUtc ?? DateTime.MaxValue).ToList();
    }

    /// <summary>Tells the kind of indexing work from the command line; null for playback transcodes.</summary>
    internal static string? Classify(bool isProbe, string args)
    {
        bool Has(string s) => args.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (Has("hls") || Has("/transcodes/") || Has("\\transcodes\\"))
        {
            return null;
        }

        if (isProbe)
        {
            return Has("-show_packets") || Has("skip_frame") || Has("-show_frames") ? "keyframes" : "probe";
        }

        if (Has("ebur128") || Has("loudnorm") || Has("volumedetect"))
        {
            return "audioNormalization";
        }

        if (Has("blackdetect") || Has("blackframe") || Has("silencedetect") || Has("scdet"))
        {
            return "segments";
        }

        // a single frame (chapter image, episode thumbnail); also written with -f image2, so checked before trickplay
        if (Has("-vframes") || Has("-frames:v"))
        {
            return "imageExtraction";
        }

        // trickplay: a frame every N seconds (fps filter) into numbered images
        if (Has("trickplay") || Has("%08d") || (Has("image2") && Has("fps=")))
        {
            return "trickplay";
        }

        if (Has("/subtitles/") || Has("\\subtitles\\") || Has("/attachments/"))
        {
            return "subtitleExtraction";
        }

        return "ffmpeg";
    }

    /// <summary>The first real input file (-i), without the file: prefix and quotes.</summary>
    internal static string? InputOf(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] != "-i")
            {
                continue;
            }

            var v = args[i + 1].Trim().Trim('"');
            if (v.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                v = v.Substring(5).Trim('"');
            }

            if (v.Length == 0 || v.StartsWith("pipe:", StringComparison.OrdinalIgnoreCase) || v.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            return v;
        }

        return null;
    }

    private static string[] ReadArgs(int pid)
    {
        try
        {
            return File.ReadAllText("/proc/" + pid + "/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static ItemRef? FindItem(ILibraryManager libraryManager, string path)
    {
        try
        {
            var item = libraryManager.FindByPath(path, false);
            return item is null ? null : ItemRefs.From(item);
        }
        catch
        {
            return null;
        }
    }
}
