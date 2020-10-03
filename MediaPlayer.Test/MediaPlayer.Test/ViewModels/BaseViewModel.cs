using Xamarin.Forms;

using MediaPlayer.Test.Models;
using MediaPlayer.Test.Services;
using Prism.Mvvm;

namespace MediaPlayer.Test.ViewModels
{
    public class BaseViewModel : BindableBase
    {
        public IDataStore<Item> DataStore => DependencyService.Get<IDataStore<Item>>();

        public bool IsBusy { get; set; }

        public string Title { get; set; }
    }
}
