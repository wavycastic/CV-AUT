using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CvAut.ViewModels;
using CvAut.Views;

namespace CvAut
{
    /// <summary>
    /// Maps a view model to its view. Uses a static switch instead of reflection so it
    /// is fully Native AOT / trimming safe (the default Avalonia template relies on
    /// Type.GetType/Activator.CreateInstance which gets trimmed away under AOT).
    /// </summary>
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            return param switch
            {
                MainWindowViewModel => new MainWindow(),
                _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
