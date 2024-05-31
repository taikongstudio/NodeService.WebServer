using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.UI.Pages.CommonConfig.Components;

public class AddingNewItemEventArgs<TDataContext> : EventArgs
{
    public AddingNewItemEventArgs(TDataContext item)
    {
        DataItem = item;
    }

    public bool Handled { get; set; }

    public TDataContext DataItem { get; set; }
}

public class ValueChangedEventArgs<TDataContext, TProperty> : EventArgs
{
    public ValueChangedEventArgs(TDataContext item, TProperty value)
    {
        DataItem = item;
        Value = value;
    }

    public bool Handled { get; set; }

    public TDataContext DataItem { get; set; }

    public TProperty Value { get; set; }
}