using System;

namespace tik4net
{
    /// <summary>
    /// Functions to convert MikroTik timespans to useable formats.
    /// Credits: D-Bullock 
    /// </summary>
    public static class TikTimeHelper
    {
        /// <summary>
        /// Convert the seconds passed in to a MikroTik time string
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns>A string in the format the MikroTik expects for it's timespan fields</returns>
        public static string ToTikTime(int? seconds)
        {
            return ToTikTime((long?)seconds);
        }
        /// <summary>
        /// Convert the seconds passed in to a MikroTik time string
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns>A string in the format the MikroTik expects for it's timespan fields</returns>
        public static string ToTikTime(long? seconds)
        {
            if (!seconds.HasValue || seconds == 0)
                return "none";

            var t = TimeSpan.FromSeconds(seconds.Value);
            var weeks = (long)t.TotalDays / 7;
            t -= TimeSpan.FromDays(weeks * 7);
            return
                (weeks != 0 ? weeks + "w" : string.Empty) +
                (t.Days != 0 ? t.Days + "d" : string.Empty) +
                (t.Hours != 0 ? t.Hours + "h" : string.Empty) +
                (t.Minutes != 0 ? t.Minutes + "m" : string.Empty) +
                (t.Seconds != 0 ? t.Seconds + "s" : string.Empty);
        }

        /// <summary>
        /// Convert a MikroTik time string to seconds
        /// </summary>
        /// <param name="time">The time as specified by MikroTik</param>
        /// <returns></returns>
        public static int FromTikTimeToSeconds(string time)
        {
            // DAF: code review: What about usage of Regex to parse this format? 

            if (time.IsNullOrWhiteSpace() || string.Equals(time, "none", StringComparison.OrdinalIgnoreCase))
                return 0;

            // Sanitise the input
            time = time.ToLower();
            int output = 0;
            string[] split;
            if ((split = time.Split('w')).Length >= 2)
            {
                if (split.Length != 2)
                    throw new FormatException("Multiple week sections specified");
                output += int.Parse(split[0]) * 604800;
                time = split[1];
            }
            if ((split = time.Split('d')).Length >= 2)
            {
                if (split.Length != 2)
                    throw new FormatException("Multiple day sections specified");
                output += int.Parse(split[0]) * 86400;
                time = split[1];
            }
            if ((split = time.Split('h')).Length >= 2)
            {
                if (split.Length != 2)
                    throw new FormatException("Multiple hour sections specified");
                output += int.Parse(split[0]) * 3600;
                time = split[1];
            }
            if ((split = time.Split('m')).Length >= 2)
            {
                if (split.Length != 2)
                    throw new FormatException("Multiple minute sections specified");
                output += int.Parse(split[0]) * 60;
                time = split[1];
            }
            if ((split = time.Split('s')).Length >= 2)
            {
                if (split.Length != 2)
                    throw new FormatException("Multiple second sections specified");
                output += int.Parse(split[0]);
                time = split[1];
            }
            return output;
        }
    }
}
