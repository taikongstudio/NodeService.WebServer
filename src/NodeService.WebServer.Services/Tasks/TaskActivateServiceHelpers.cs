internal static class TaskActivateServiceHelpers
{

    public static void ApplyEnvironmentVariables(TaskDefinition taskDefinition)
    {
        if (taskDefinition.TaskTypeDesc == null)
        {
            return;
        }
        if (taskDefinition.TaskTypeDesc.Value.FullName == "NodeService.WindowsService.Services.ExecuteBatchScriptJob")
        {
            if (taskDefinition.Options.TryGetValue(
                "Scripts",
                out var scriptsObject) && scriptsObject is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            {
                var scriptsString = jsonElement.GetString();
                if (scriptsString == null)
                {
                    return;
                }
                foreach (var item in taskDefinition.EnvironmentVariables)
                {
                    scriptsString = scriptsString.Replace($"$({item.Name})", item.Value);
                }
                taskDefinition.Options["Scripts"] = scriptsString;
            }
        }
    }
}