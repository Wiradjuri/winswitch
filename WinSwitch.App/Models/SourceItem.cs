using WinSwitch.Utilities;

namespace WinSwitch.Models;

public class SourceItem : ObservableObject
{
    private string _name = "";
    private string _path = "";
    private bool _isSelected;

    public string Name
    {
        get => _name;
        init => _name = value;
    }

    public string Path
    {
        get => _path;
        init => _path = value;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
