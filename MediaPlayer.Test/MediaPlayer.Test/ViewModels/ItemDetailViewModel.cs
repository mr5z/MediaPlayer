using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using MediaPlayer.Test.Models;
using Xamarin.Forms;

namespace MediaPlayer.Test.ViewModels
{
    [QueryProperty(nameof(ItemId), nameof(ItemId))]
    public class ItemDetailViewModel : BaseViewModel
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Description { get; set; }
        public string ItemId { get; set; }

        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);

            switch (args.PropertyName)
            {
                case nameof(ItemId):
                    LoadItemId(ItemId);
                    break;
            }
        }

        public async void LoadItemId(string itemId)
        {
            try
            {
                var item = await DataStore.GetItemAsync(itemId);
                Id = item.Id;
                Text = item.Text;
                Description = item.Description;
            }
            catch (Exception)
            {
                Debug.WriteLine("Failed to Load Item");
            }
        }
    }
}
