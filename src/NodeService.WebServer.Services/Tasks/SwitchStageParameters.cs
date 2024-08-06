namespace NodeService.WebServer.Services.Tasks;

public readonly record struct SwitchStageParameters
{
    public SwitchStageParameters(string taskFlowExecutionInstanceId, int stageIndex)
    {
        TaskFlowExecutionInstanceId = taskFlowExecutionInstanceId;
        StageIndex = stageIndex;
    }

    public string TaskFlowExecutionInstanceId { get; init; }

    public int StageIndex { get; init; }
}
