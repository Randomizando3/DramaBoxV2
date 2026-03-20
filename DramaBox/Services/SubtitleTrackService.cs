using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DramaBox.Services;

public sealed class SubtitleCue
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; init; } = "";
}

public sealed class SubtitleTrack
{
    public static SubtitleTrack Empty { get; } = new(Array.Empty<SubtitleCue>());

    private readonly List<SubtitleCue> _cues;

    public IReadOnlyList<SubtitleCue> Cues => _cues;
    public bool HasCues => _cues.Count > 0;

    public SubtitleTrack(IEnumerable<SubtitleCue> cues)
    {
        _cues = (cues ?? Array.Empty<SubtitleCue>())
            .Where(x => x != null && x.End > x.Start && !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.Start)
            .ToList();
    }

    public string GetTextAt(TimeSpan position)
    {
        if (_cues.Count == 0)
            return "";

        var left = 0;
        var right = _cues.Count - 1;

        while (left <= right)
        {
            var mid = left + ((right - left) / 2);
            var cue = _cues[mid];

            if (position < cue.Start)
            {
                right = mid - 1;
                continue;
            }

            if (position >= cue.End)
            {
                left = mid + 1;
                continue;
            }

            return cue.Text;
        }

        return "";
    }
}

public sealed class SubtitleTrackService
{
    private readonly HttpClient _http;

    public SubtitleTrackService(HttpClient http)
    {
        _http = http;
    }

    public async Task<SubtitleTrack> LoadFromUrlAsync(
        string? url,
        string? formatHint = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(url))
            return SubtitleTrack.Empty;

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return SubtitleTrack.Empty;

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
                return SubtitleTrack.Empty;

            var format = NormalizeFormat(formatHint);
            if (string.IsNullOrWhiteSpace(format))
                format = DetectFormatFromPath(url);

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                return ParseJson(raw);

            if (string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase))
                return ParseVtt(raw);

            if (LooksLikeJson(raw))
                return ParseJson(raw);

            return ParseVtt(raw);
        }
        catch
        {
            return SubtitleTrack.Empty;
        }
    }

    public static string NormalizeFormat(string? raw)
    {
        var value = (raw ?? "").Trim().TrimStart('.').ToLowerInvariant();

        return value switch
        {
            "webvtt" => "vtt",
            "vtt" => "vtt",
            "json" => "json",
            _ => ""
        };
    }

    public static string DetectFormatFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            var clean = path;
            var qIndex = clean.IndexOf('?', StringComparison.Ordinal);
            if (qIndex >= 0)
                clean = clean[..qIndex];

            var ext = System.IO.Path.GetExtension(clean);
            return NormalizeFormat(ext);
        }
        catch
        {
            return "";
        }
    }

    private static bool LooksLikeJson(string raw)
    {
        var trimmed = (raw ?? "").TrimStart();
        return trimmed.StartsWith("[", StringComparison.Ordinal) ||
               trimmed.StartsWith("{", StringComparison.Ordinal);
    }

    private static SubtitleTrack ParseVtt(string raw)
    {
        var normalized = NormalizeNewLines(raw)
            .Replace("\uFEFF", "")
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return SubtitleTrack.Empty;

        var blocks = Regex.Split(normalized, @"\n\s*\n");
        var cues = new List<SubtitleCue>();

        foreach (var block in blocks)
        {
            var lines = block
                .Split('\n')
                .Select(x => x.TrimEnd())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (lines.Count == 0)
                continue;

            if (lines[0].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                lines[0].StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                lines[0].StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
                lines[0].StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var timingIndex = lines.FindIndex(x => x.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0)
                continue;

            var timing = lines[timingIndex];
            var parts = timing.Split(new[] { "-->" }, 2, StringSplitOptions.None);
            if (parts.Length != 2)
                continue;

            if (!TryParseTimestamp(parts[0], out var start))
                continue;

            var endToken = parts[1].Trim();
            var spaceIndex = endToken.IndexOf(' ');
            if (spaceIndex >= 0)
                endToken = endToken[..spaceIndex];

            if (!TryParseTimestamp(endToken, out var end))
                continue;

            var text = string.Join("\n", lines.Skip(timingIndex + 1)).Trim();
            text = CleanupText(text);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            cues.Add(new SubtitleCue
            {
                Start = start,
                End = end,
                Text = text
            });
        }

        return new SubtitleTrack(cues);
    }

    private static SubtitleTrack ParseJson(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            JsonElement items = document.RootElement;

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (TryGetPropertyIgnoreCase(document.RootElement, out var cuesProperty, "cues", "items", "subtitles"))
                    items = cuesProperty;
            }

            if (items.ValueKind != JsonValueKind.Array)
                return SubtitleTrack.Empty;

            var cues = new List<SubtitleCue>();

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryReadTime(item, out var start, "start", "startTime", "from"))
                    continue;

                TimeSpan end;
                if (!TryReadTime(item, out end, "end", "endTime", "to"))
                {
                    if (!TryReadTime(item, out var duration, "duration", "length"))
                        continue;

                    end = start + duration;
                }

                if (!TryReadText(item, out var text, "text", "caption", "value"))
                    continue;

                text = CleanupText(text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                cues.Add(new SubtitleCue
                {
                    Start = start,
                    End = end,
                    Text = text
                });
            }

            return new SubtitleTrack(cues);
        }
        catch
        {
            return SubtitleTrack.Empty;
        }
    }

    private static bool TryReadText(JsonElement element, out string text, params string[] names)
    {
        text = "";

        if (!TryGetPropertyIgnoreCase(element, out var value, names))
            return false;

        text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => value.ToString()
        };

        return true;
    }

    private static bool TryReadTime(JsonElement element, out TimeSpan time, params string[] names)
    {
        time = TimeSpan.Zero;

        if (!TryGetPropertyIgnoreCase(element, out var value, names))
            return false;

        return TryParseJsonTime(value, out time);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseJsonTime(JsonElement value, out TimeSpan time)
    {
        time = TimeSpan.Zero;

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetDouble(out var seconds))
                {
                    time = TimeSpan.FromSeconds(Math.Max(0, seconds));
                    return true;
                }
                return false;

            case JsonValueKind.String:
                return TryParseTimestamp(value.GetString(), out time);

            default:
                return false;
        }
    }

    private static bool TryParseTimestamp(string? raw, out TimeSpan value)
    {
        value = TimeSpan.Zero;

        var text = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Replace(',', '.');

        if (TimeSpan.TryParseExact(
                text,
                new[]
                {
                    @"hh\:mm\:ss\.FFF",
                    @"hh\:mm\:ss\.ff",
                    @"hh\:mm\:ss\.f",
                    @"hh\:mm\:ss",
                    @"mm\:ss\.FFF",
                    @"mm\:ss\.ff",
                    @"mm\:ss\.f",
                    @"mm\:ss"
                },
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 3)
            return false;

        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondsPart))
            return false;

        var seconds = secondsPart;
        var minutes = 0;
        var hours = 0;

        if (parts.Length >= 2 && !int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
            return false;

        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
            return false;

        value = TimeSpan.FromHours(hours)
              + TimeSpan.FromMinutes(minutes)
              + TimeSpan.FromSeconds(seconds);

        return true;
    }

    private static string NormalizeNewLines(string raw)
        => (raw ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    private static string CleanupText(string raw)
    {
        var text = NormalizeNewLines(raw).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace("\\N", "\n");
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join("\n", lines);
    }
}
