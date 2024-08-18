using NodeService.Infrastructure.Logging;

public static class TaskLogKafkaProducerServiceHelpers
{

    public static bool TryParseTaskProgressEntry(LogEntry item, out TaskProgressEntry taskProgressEntry)
    {
        try
        {
            taskProgressEntry = default;

            if (item == null || item.Value == null)
            {
                return false;
            }
            var workerIndex = item.Value.IndexOf("Worker");
            var masterIndex = item.Value.IndexOf("Master");
            var progressIndex = item.Value.IndexOf("Progress");
            if (workerIndex >= 0 && progressIndex > 0)
            {
                taskProgressEntry = new TaskProgressEntry()
                {
                    TaskName = ParseProcessId(item, "Worker", workerIndex),
                    Progress = ParseProgress(item, progressIndex)
                };
                return true;
            }
            else if (masterIndex >= 0 && progressIndex > 0)
            {
                taskProgressEntry = new TaskProgressEntry()
                {
                    TaskName = ParseProcessId(item, "Master", masterIndex),
                    Progress = ParseProgress(item, progressIndex)
                };
                return true;
            }
        }
        finally
        {

        }
        return false;
    }

    private static double ParseProgress(LogEntry item, int progressIndex)
    {
        if (item == null || string.IsNullOrEmpty(item.Value))
        {
            return 0;
        }

        if (progressIndex == -1 || item.Value.Length - progressIndex <= 1)
        {
            return 0;
        }

        var progressStrings = item.Value[(progressIndex + 8)..].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (progressStrings.Length < 2)
        {
            return 0;
        }
        else if (progressStrings.Length == 2)
        {
            for (int i = 0; i < progressStrings.Length; i++)
            {
                var str = progressStrings[i];
                for (int chIndex = 0; chIndex < str.Length; chIndex++)
                {
                    var ch = str[chIndex];
                    switch (ch)
                    {
                        case >= '0' and <= '9':
                            break;
                        default:
                            str = str.Substring(0, chIndex);
                            break;
                    }
                }
                progressStrings[i] = str;
            }
            if (double.TryParse(progressStrings[0], out double num1) && double.TryParse(progressStrings[1], out double num2))
            {
                if (!double.IsNormal(num1) || !double.IsNormal(num2))
                {
                    return 0;
                }
                if (num2 == 0)
                {
                    return 0;
                }
                return num1 / num2;
            }
        }
        return 0;
    }

    private static string ParseProcessId(LogEntry item, string taskPrefix,int startIndex)
    {
        var processId = string.Empty;
        var sb = new StringBuilder();
        var start = false;
        for (int i = startIndex; i < item.Value.Length; i++)
        {
            var ch = item.Value[i];
            switch (ch)
            {
                case '(':
                    start = true;
                    break;
                case ')':
                    processId = sb.ToString();
                    return $"{taskPrefix}({processId})";
                default:
                    if (start)
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return string.Empty;
    }

}