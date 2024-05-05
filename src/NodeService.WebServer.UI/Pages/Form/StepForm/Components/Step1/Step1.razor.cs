using AntDesign;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.UI.Models;

namespace NodeService.WebServer.UI.Pages.Form;

public partial class Step1
{
    private readonly FormItemLayout _formLayout = new()
    {
        WrapperCol = new ColLayoutParam
        {
            Xs = new EmbeddedProperty { Span = 24, Offset = 0 },
            Sm = new EmbeddedProperty { Span = 19, Offset = 5 }
        }
    };

    private readonly StepFormModel _model = new();

    [CascadingParameter] public StepForm StepForm { get; set; }

    public void OnValidateForm()
    {
        StepForm.Next();
    }
}