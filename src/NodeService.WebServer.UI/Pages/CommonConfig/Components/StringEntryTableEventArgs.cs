using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.UI.Pages.CommonConfig.Components;

public class AddingNewItemEventArgs : EventArgs
{
    public AddingNewItemEventArgs(StringEntry item)
    {
        DataItem = item;
    }

    public bool Handled { get; set; }

    public StringEntry DataItem { get; private set; }
}

public class ValueChangingEventArgs<T> : EventArgs
{
    public ValueChangingEventArgs(StringEntry item, T value)
    {
        DataItem = item;
        Value = value;
    }

    public bool Handled { get; set; }

    public StringEntry DataItem { get; private set; }

    public T Value { get; private set; }
}