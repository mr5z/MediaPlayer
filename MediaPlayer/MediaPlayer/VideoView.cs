using System;
using System.Runtime.CompilerServices;
using Xamarin.Forms;
using XamarinUtility;

namespace MediaPlayer
{
    public class VideoView : View
    {
        public double UpdateIntervalInSeconds { get; set; } = 1;

        event EventHandler<PositionChangedEventArgs> CurrentPositionChanged;
        event EventHandler<VideoStateChangedEventArgs> VideoStateChanged;

        public static BindableProperty VideoPlayerReadyCommandProperty = BindableHelper.CreateProperty<Command<IVideoPlayer>>();
        public Command<IVideoPlayer> VideoPlayerReadyCommand
        {
            get => (Command<IVideoPlayer>)GetValue(VideoPlayerReadyCommandProperty);
            set => SetValue(VideoPlayerReadyCommandProperty, value);
        }

        public static BindableProperty AutoPlayProperty = BindableHelper.CreateProperty<bool>(mode: BindingMode.OneWayToSource);
        public bool AutoPlay
        {
            get => (bool)GetValue(AutoPlayProperty);
            set => SetValue(AutoPlayProperty, value);
        }

        public static BindableProperty ShowDefaultControlsProperty = BindableHelper.CreateProperty<bool>();
        public bool ShowDefaultControls
        {
            get => (bool)GetValue(ShowDefaultControlsProperty);
            set => SetValue(ShowDefaultControlsProperty, value);
        }

        public static BindableProperty SourceProperty = BindableHelper.CreateProperty<string>(mode: BindingMode.OneWayToSource);
        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public void ReportCurrentPositionChanged(TimeSpan newPosition, TimeSpan newBufferedPosition)
        {
            CurrentPositionChanged?.Invoke(this, new PositionChangedEventArgs(newPosition, newBufferedPosition));
        }

        public void ReportVideoStateChanged(VideoState newState)
        {
            VideoStateChanged?.Invoke(this, new VideoStateChangedEventArgs(newState));
        }
    }
}
