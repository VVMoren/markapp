using System.Windows.Controls;
using markapp.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace markapp.Views.Pages
{
    public partial class DashboardPage : Page, INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            InitializeComponent(); // Обязательно первым

            ViewModel = viewModel;
            DataContext = ViewModel;
        }
    }
}
