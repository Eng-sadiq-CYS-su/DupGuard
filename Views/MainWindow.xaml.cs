using System.Windows;
using DupGuard.ViewModels;

namespace DupGuard.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Set DataContext to MainViewModel
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        protected override void OnClosed(System.EventArgs e)
        {
            // Cleanup
            _viewModel.CancelScan();

            base.OnClosed(e);
        }
    }
}
