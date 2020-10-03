using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaPlayer
{
    public interface IVideoPlayer
    {
        bool AutoPlay { get; set; }
        bool ShowDefaultControls { get; set; }
        TimeSpan Duration { get; }
        TimeSpan CurrentPosition { get; }
        TimeSpan BufferedPosition { get; }
        double DurationMilliseconds { get; }
        VideoState State { get; }
        bool IsSeeking { get; }

        void Authorize(string authorizationValue);

        // Transport controls
        Task<VideoLoadStatus> FromLocal(string filePath, CancellationToken cancellationToken = default);
        Task<VideoLoadStatus> FromUrl(Uri source, CancellationToken cancellationToken = default);
        Task<VideoLoadStatus> FromResource(string name, string extension, CancellationToken cancellationToken = default);
        void Play();
        void Pause();
        void Stop();
        Task<bool> SeekTo(TimeSpan position);
        Task<bool> Forward(TimeSpan step);
        Task<bool> Backward(TimeSpan step);

        // TODO
        // it would be nice if we can preload the video for X amount of seconds before navigating to it
        void PreLoad(string source, int sizeInBytes);
        void CancelPendingSeeks();

        event EventHandler<PositionChangedEventArgs> CurrentPositionChanged;
        event EventHandler<VideoStateChangedEventArgs> VideoStateChanged;
        event EventHandler<PlaybackErrorEventArgs> PlaybackError;
        event EventHandler<StreamingResponseEventArgs> StreamingResponse;
        event EventHandler<VideoBufferingEventArgs> Buffering;
    }

    public class PositionChangedEventArgs : EventArgs
    {
        public PositionChangedEventArgs(TimeSpan newPosition, TimeSpan newBufferedPosition)
        {
            NewPosition = newPosition;
            NewBufferedPosition = newBufferedPosition;
        }

        public TimeSpan NewPosition { get; }
        public TimeSpan NewBufferedPosition { get; }
    }

    public class VideoStateChangedEventArgs : EventArgs
    {
        public VideoStateChangedEventArgs(VideoState newState)
        {
            NewState = newState;
        }
        public VideoState NewState { get; }
    }

    public class PlaybackErrorEventArgs : EventArgs
    {
        public PlaybackErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }
        public Exception Exception { get; }
    }

    public class StreamingResponseEventArgs : EventArgs
    {
        public StreamingResponseEventArgs(long contentLength)
        {
            ContentLength = contentLength;
        }
        public long ContentLength { get; }
    }

    public class VideoBufferingEventArgs : EventArgs
    {
        public VideoBufferingEventArgs(bool isBuffering)
        {
            IsBuffering = isBuffering;
        }
        public bool IsBuffering { get; }
    }
}
