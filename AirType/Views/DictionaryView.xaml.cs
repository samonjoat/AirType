using System.Windows;
using System.Windows.Controls;
using AirType.ViewModels;
using AirType.Services;
using AirType.Services.Dictionary;

namespace AirType.Views;

public partial class DictionaryView : UserControl
{
    public DictionaryView()
    {
        InitializeComponent();

        if (Application.Current is App app && app.Services != null)
        {
            var dictionaryManager = app.Services.DictionaryManager;
            DataContext = new DictionaryViewModel(dictionaryManager);
        }
    }
}
