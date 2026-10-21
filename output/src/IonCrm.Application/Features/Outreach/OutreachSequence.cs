using System.Text.Json;
using System.Text.Json.Serialization;

namespace IonCrm.Application.Features.Outreach;

/// <summary>One step of the sequence: which template, how many days after the previous step, attachments or not.</summary>
public record OutreachStep(string Template, int DelayDays, bool Attachments = false);

/// <summary>Parsed <c>sequence.json</c> of a marketing folder.</summary>
public record SequenceDefinition(string TimeZone, int StartHour, int EndHour, bool WeekdaysOnly, List<OutreachStep> Steps)
{
    public static SequenceDefinition Default => new("Europe/London", 9, 17, true, new()
    {
        new("intro.en-GB.html", 0, true), new("followup-1.en-GB.html", 4), new("followup-2.en-GB.html", 9),
    });

    /// <summary>Configured zone, else Europe/London, else UTC — never the TR fallback of the social planner.</summary>
    public TimeZoneInfo Tz
    {
        get
        {
            foreach (var id in new[] { TimeZone, "Europe/London" })
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>Pure scheduling/rendering rules of the outreach sequence. Unit-tested.</summary>
public static class OutreachSequence
{
    private sealed class SequenceJson
    {
        public string? TimeZone { get; set; }
        public WindowJson? SendWindow { get; set; }
        public List<StepJson>? Steps { get; set; }
    }
    private sealed class WindowJson { public int StartHour { get; set; } = 9; public int EndHour { get; set; } = 17; public bool WeekdaysOnly { get; set; } = true; }
    private sealed class StepJson { public string Template { get; set; } = ""; public int DelayDays { get; set; } public bool Attachments { get; set; } }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static SequenceDefinition Parse(string json)
    {
        var j = JsonSerializer.Deserialize<SequenceJson>(json, JsonOpts) ?? new SequenceJson();
        var steps = (j.Steps ?? new()).Where(s => !string.IsNullOrWhiteSpace(s.Template)).Select(s => new OutreachStep(s.Template.Trim(), Math.Max(0, s.DelayDays), s.Attachments)).ToList();
        if (steps.Count == 0) steps = SequenceDefinition.Default.Steps;
        var w = j.SendWindow ?? new WindowJson();
        return new SequenceDefinition(string.IsNullOrWhiteSpace(j.TimeZone) ? "Europe/London" : j.TimeZone!, Math.Clamp(w.StartHour, 0, 23), Math.Clamp(w.EndHour, 1, 24), w.WeekdaysOnly, steps);
    }

    /// <summary>
    /// First moment at or after <paramref name="earliestUtc"/> that falls inside the send window
    /// (local hours [StartHour, EndHour), weekdays only when configured). Inside the window → unchanged.
    /// </summary>
    public static DateTime NextSendTime(DateTime earliestUtc, SequenceDefinition def)
    {
        var tz = def.Tz;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(earliestUtc, DateTimeKind.Utc), tz);
        for (var i = 0; i < 14; i++)
        {
            var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (def.WeekdaysOnly && weekend) { local = local.Date.AddDays(1).AddHours(def.StartHour); continue; }
            if (local.Hour < def.StartHour) { local = local.Date.AddHours(def.StartHour); break; }
            if (local.Hour >= def.EndHour) { local = local.Date.AddDays(1).AddHours(def.StartHour); continue; }
            break;
        }
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
    }

    /// <summary>When step <paramref name="stepIndex"/> should go out, given when the previous step was sent.</summary>
    public static DateTime ScheduleStep(int stepIndex, DateTime previousSentUtc, SequenceDefinition def)
    {
        var delay = stepIndex <= 0 ? 0 : Math.Max(0, def.Steps[stepIndex].DelayDays - def.Steps[stepIndex - 1].DelayDays);
        return NextSendTime(previousSentUtc.AddDays(delay), def);
    }

    /// <summary>Replaces {{UnsubscribeUrl}}; when the template has no placeholder, appends a small footer before &lt;/body&gt;.</summary>
    public static string InjectUnsubscribe(string html, string url)
    {
        if (html.Contains("{{UnsubscribeUrl}}")) return html.Replace("{{UnsubscribeUrl}}", url);
        var footer = $"<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#8C99A9;text-align:center;\">If you would prefer not to receive these messages, <a href=\"{url}\" style=\"color:#8C99A9;\">unsubscribe here</a>.</p>";
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? html + footer : html.Insert(idx, footer);
    }

    public static string NewUnsubscribeToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Scan regions per country for the global lead agent (biggest lift markets first).</summary>
public static class GlobalLeadRegions
{
    public static readonly Dictionary<string, string[]> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = new[]
        {
            "London", "Greater Manchester", "Birmingham and the West Midlands", "Leeds and West Yorkshire", "Glasgow", "Liverpool and Merseyside",
            "Bristol and the South West", "Sheffield and South Yorkshire", "Edinburgh", "Newcastle and the North East", "Nottingham and the East Midlands",
            "Leicester", "Cardiff and South Wales", "Belfast and Northern Ireland", "Southampton and Hampshire", "Brighton and Sussex",
            "Reading and the Thames Valley", "Cambridge and East Anglia", "Aberdeen", "Kent", "Essex", "Surrey", "Oxford", "Milton Keynes", "Stoke-on-Trent", "Hull",
        },
        ["IE"] = new[] { "Dublin", "Cork", "Galway", "Limerick", "Belfast border counties", "Waterford" },
    };

    public static List<string> Next(string country, int count, ISet<string> recentlyScanned)
    {
        var regions = ByCountry.TryGetValue(country, out var r) ? r : new[] { country };
        var folded = recentlyScanned.Select(x => x.ToLowerInvariant().Trim()).ToHashSet();
        var next = regions.Where(x => !folded.Contains(x.ToLowerInvariant())).Take(Math.Max(1, count)).ToList();
        return next.Count == 0 ? regions.Take(Math.Max(1, count)).ToList() : next;
    }
}
