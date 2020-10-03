using System.ComponentModel;
using Xamarin.Forms;
using MediaPlayer.Test.ViewModels;

namespace MediaPlayer.Test.Views
{
    public partial class ItemDetailPage : ContentPage
    {
        public ItemDetailPage()
        {
            InitializeComponent();
            BindingContext = new ItemDetailViewModel();
        }
    }
}