using System;
using System.Collections.Generic;
using MediaPlayer.Test.ViewModels;
using MediaPlayer.Test.Views;
using Xamarin.Forms;

namespace MediaPlayer.Test
{
    public partial class AppShell : Xamarin.Forms.Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(ItemDetailPage), typeof(ItemDetailPage));
            Routing.RegisterRoute(nameof(NewItemPage), typeof(NewItemPage));
        }

    }
}
