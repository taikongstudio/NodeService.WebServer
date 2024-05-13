using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.UI.Pages.CommonConfig.Components;

public class AddingNewItemEventArgs : EventArgs
{
    public AddingNewItemEventArgs(StringEntry item)
    {
        DataItem = item;
    }

    public bool Handled { get; set; }

    public StringEntry DataItem { get; set; }
}

public class ValueChangedEventArgs<T> : EventArgs
{
    public ValueChangedEventArgs(StringEntry item, T value)
    {
        DataItem = item;
        Value = value;
    }

    public bool Handled { get; set; }

    public StringEntry DataItem { get; set; }

    public T Value { get; set; }
}