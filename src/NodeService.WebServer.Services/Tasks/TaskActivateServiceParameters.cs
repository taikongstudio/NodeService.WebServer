using OneOf;

namespace NodeService.WebServer.Services.Tasks;

public readonly struct TaskActivateServiceParameters
{
    public TaskActivateServiceParameters(FireTaskParameters fireTaskParameters)
    {
        this.Parameters = fireTaskParameters;
    }

    public TaskActivateServiceParameters(FireTaskFlowParameters fireTaskFlowParameters)
    {
        this.Parameters = fireTaskFlowParameters;
    }

    public TaskActivateServiceParameters(RetryTaskParameters retryTaskParameters)
    {
        Parameters = retryTaskParameters;
    }
    public TaskActivateServiceParameters(SwitchStageParameters  switchTaskFlowStageParameters)
    {
        Parameters = switchTaskFlowStageParameters;
    }

    public OneOf<FireTaskParameters, FireTaskFlowParameters, RetryTaskParameters, SwitchStageParameters> Parameters { get; init; }

}
