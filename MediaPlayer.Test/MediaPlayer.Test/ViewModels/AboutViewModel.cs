using Prism.Commands;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Xamarin.Essentials;
using Xamarin.Forms;
using XamarinUtility.Extensions;

namespace MediaPlayer.Test.ViewModels
{
    public class AboutViewModel : BaseViewModel
    {
        public AboutViewModel()
        {
            Title = "About";
            OpenWebCommand = new Command(async () => await Browser.OpenAsync("https://aka.ms/xamain-quickstart"));
            VideoPlayerReadyCommand = new Command<IVideoPlayer>(VideoPlayerReady);
        }

        private void VideoPlayerReady(IVideoPlayer videoPlayer)
        {
            _ = ConfigurePlayer(videoPlayer);
        }

        private async Task ConfigurePlayer(IVideoPlayer videoPlayer)
        {
            var result = await videoPlayer.FromUrl(new Uri("https://bitdash-a.akamaihd.net/content/sintel/hls/playlist.m3u8"));
            if (result == VideoLoadStatus.Loaded)
            {
                var a = 1 + 1;
            }
            else
            {
                var a = 2 + 1;
            }
        }

        public ICommand OpenWebCommand { get; }

        public Command<IVideoPlayer> VideoPlayerReadyCommand { get; set; }
    }
}