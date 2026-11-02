using System;
using System.Collections.Generic;

namespace KeepAwake.Core
{
    public sealed class DurationOption
    {
        public DurationOption(string label, TimeSpan? span)
        {
            Label = label;
            Span = span;
        }

        public string Label { get; }
        public TimeSpan? Span { get; }
        public bool IsForever => Span == null;
        public int Minutes => Span.HasValue ? (int)Span.Value.TotalMinutes : 0;
    }

    public static class Durations
    {
        public const int ForeverMinutes = 0;
        public const int DefaultMinutes = 480;

        public static readonly IReadOnlyList<DurationOption> All = new[]
        {
            new DurationOption("1 hour", TimeSpan.FromHours(1)),
            new DurationOption("2 hours", TimeSpan.FromHours(2)),
            new DurationOption("4 hours", TimeSpan.FromHours(4)),
            new DurationOption("8 hours", TimeSpan.FromHours(8)),
            new DurationOption("24 hours", TimeSpan.FromHours(24)),
            new DurationOption("Forever", null),
        };

        public static readonly IReadOnlyList<DurationOption> Extend = new[]
        {
            new DurationOption("1 hour", TimeSpan.FromHours(1)),
            new DurationOption("2 hours", TimeSpan.FromHours(2)),
            new DurationOption("4 hours", TimeSpan.FromHours(4)),
            new DurationOption("Forever", null),
        };

        public static DurationOption FromMinutes(int minutes)
        {
            foreach (var d in All)
                if (d.Minutes == minutes) return d;
            return All[3];
        }

        public static string ActivateLabel(DurationOption d) =>
            d.IsForever ? "Keep awake forever" : "Keep awake for " + d.Label;
    }
}
