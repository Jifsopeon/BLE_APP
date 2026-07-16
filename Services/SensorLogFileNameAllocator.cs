using System.Text.RegularExpressions;

namespace BLE_APP.Services;

public static partial class SensorLogFileNameAllocator
{
    public static int ParseLogNumber(string fileName)
    {
        var match = LogFileNameRegex().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    public static int FindHighestExistingNumber(IEnumerable<string> fileNames)
        => fileNames.Select(ParseLogNumber).DefaultIfEmpty(0).Max();

    public static string AllocateNextFileName(ISet<string> existingFileNames)
        => AllocateNextFileName(existingFileNames, lastAllocatedNumber: 0, out _);

    public static string AllocateNextFileName(ISet<string> existingFileNames, int lastAllocatedNumber, out int allocatedNumber)
    {
        var next = Math.Max(lastAllocatedNumber, FindHighestExistingNumber(existingFileNames)) + 1;

        string candidate;
        do
        {
            candidate = $"Log{next:0000}.csv";
            allocatedNumber = next;
            next++;
        }
        while (existingFileNames.Contains(candidate));

        return candidate;
    }

    [GeneratedRegex("^Log(\\d{4})\\.csv$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LogFileNameRegex();
}
