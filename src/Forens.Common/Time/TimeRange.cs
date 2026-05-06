using System;
using System.Globalization;

namespace Forens.Common.Time
{
    public sealed class TimeRange
    {
        public TimeRange(DateTimeOffset? from, DateTimeOffset? to)
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("From must be less than or equal to To.");
            From = from;
            To = to;
        }

        public DateTimeOffset? From { get; }
        public DateTimeOffset? To { get; }

        public bool IsEmpty
        {
            get { return !From.HasValue && !To.HasValue; }
        }

        public bool Includes(DateTimeOffset timestamp)
        {
            if (From.HasValue && timestamp < From.Value) return false;
            if (To.HasValue && timestamp > To.Value) return false;
            return true;
        }

        public static DateTimeOffset ParseStrictUtc(string text, string paramName)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Timestamp is required.", paramName);

            const DateTimeStyles styles =
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal;

            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsed))
            {
                throw new FormatException(string.Format(
                    "Cannot parse '{0}' as ISO 8601 timestamp.", text));
            }

            if (!HasOffsetOrZ(text))
            {
                throw new FormatException(string.Format(
                    "Timestamp '{0}' must include a UTC offset (e.g., 'Z' or '+01:00').", text));
            }

            // Reparse with universal styles to normalize to UTC.
            DateTimeOffset utc;
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out utc);
            return utc.ToUniversalTime();
        }

        private static bool HasOffsetOrZ(string text)
        {
            if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;

            // Look for +HH:MM or -HH:MM after the 'T' separator.
            int t = text.IndexOf('T');
            if (t < 0) return false;
            for (int i = text.Length - 1; i > t; i--)
            {
                char c = text[i];
                if (c == '+' || c == '-') return true;
                if (!char.IsDigit(c) && c != ':') return false;
            }
            return false;
        }
    }
}
