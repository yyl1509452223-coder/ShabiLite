using System.Text.RegularExpressions;

namespace ShabiLite.Services
{
    internal sealed class DownloadProgress
    {
        public int Percent { get; set; }
        public string Message { get; set; }
    }

    internal static class WorkshopIdParser
    {
        public static bool TryExtract(string input, out string workshopId)
        {
            workshopId = null;
            var value = (input ?? string.Empty).Trim();
            if (Regex.IsMatch(value, @"^\d{6,20}$"))
            {
                workshopId = value;
                return true;
            }

            var match = Regex.Match(value, @"(?:[?&]id=|/filedetails/)(\d{6,20})", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            workshopId = match.Groups[1].Value;
            return true;
        }
    }
}
