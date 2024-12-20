﻿internal static class TaskActivateServiceHelpers
{

    public static void ApplyEnvironmentVariables(TaskDefinition taskDefinition)
    {
        if (taskDefinition.TaskTypeDesc == null)
        {
            return;
        }
        switch (taskDefinition.TaskTypeDesc.Value.FullName)
        {
            case "NodeService.WindowsService.Services.ExecuteBatchScriptJob":
                ApplyScriptsEnvVars(taskDefinition);
                break;
            default:
                break;
        }
    }

    private static void ApplyScriptsEnvVars(TaskDefinition taskDefinition)
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