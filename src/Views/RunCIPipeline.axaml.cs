using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RunCIPipeline : UserControl
    {
        public RunCIPipeline()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var inputs = this.GetVisualDescendants();
            foreach (var input in inputs)
            {
                if (input is InputElement { Focusable: true, IsTabStop: true } focusable)
                {
                    focusable.Focus();
                    return;
                }
            }
        }
    }
}
