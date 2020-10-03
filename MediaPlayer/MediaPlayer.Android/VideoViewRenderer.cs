using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Views;
using Com.Google.Android.Exoplayer2;
using Com.Google.Android.Exoplayer2.Decoder;
using Com.Google.Android.Exoplayer2.Drm;
using Com.Google.Android.Exoplayer2.Ext.Okhttp;
using Com.Google.Android.Exoplayer2.Metadata;
using Com.Google.Android.Exoplayer2.Source;
using Com.Google.Android.Exoplayer2.Source.Hls;
using Com.Google.Android.Exoplayer2.Text;
using Com.Google.Android.Exoplayer2.Trackselection;
using Com.Google.Android.Exoplayer2.UI;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Util;
using Com.Google.Android.Exoplayer2.Video;
using MediaPlayer;
using MediaPlayer.Android;
using Square.OkHttp3;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;

[assembly: ExportRenderer(typeof(VideoView), typeof(VideoViewRenderer))]
namespace MediaPlayer.Android
{
    public class VideoViewRenderer : CustomViewRenderer<VideoView, PlayerView>,
        IVideoPlayer,
        IPlayerEventListener,
        IMetadataOutput,
        ITextOutput,
        IVideoRendererEventListener
    {
        private static OkHttpClient httpClient;
        private SimpleExoPlayer player;
        private IHlsDataSourceFactory dataSourceFactory;
        private OkHttpDataSourceFactory httpFactory;

        private bool timerIsRunning;

        public event EventHandler<PositionChangedEventArgs> CurrentPositionChanged;
        public event EventHandler<VideoStateChangedEventArgs> VideoStateChanged;
        public event EventHandler<PlaybackErrorEventArgs> PlaybackError;
        public event EventHandler<StreamingResponseEventArgs> StreamingResponse;
        public event EventHandler<VideoBufferingEventArgs> Buffering;

        public static void Initialize()
        {

        }

        public VideoViewRenderer(Context context) : base(context)
        {
            // https://github.com/square/okhttp/issues/3372
            httpClient ??= new OkHttpClient().NewBuilder()
                .WriteTimeout(5, Java.Util.Concurrent.TimeUnit.Minutes)
                .ReadTimeout(5, Java.Util.Concurrent.TimeUnit.Minutes)
                .AddInterceptor(chain =>
                {
                    var request = chain.Request();

                    try
                    {
                        var response = chain.Proceed(request);
                        if (response.IsSuccessful)
                        {
                            var body = response.Body();
                            var contentLength = body.ContentLength();
                            StreamingResponse?.Invoke(this, new StreamingResponseEventArgs(contentLength));
                        }
                        return response;
                    }
                    catch (Java.IO.InterruptedIOException ex)
                    {
                        var contentLength = ex.BytesTransferred;
                        StreamingResponse?.Invoke(this, new StreamingResponseEventArgs(contentLength));
                        PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(ex));
                        return DefaultEmptyHttpResponse(request);
                    }
                    catch (Exception ex)
                    {
                        if (ex is Java.Net.ConnectException)
                        {

                        }
                        if (ex is Java.Net.UnknownHostException)
                        {
                            // probably no internet
                        }
                        PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(ex));
                        return DefaultEmptyHttpResponse(request);
                    }
                })
                .Build();
        }

        private static Response DefaultEmptyHttpResponse(Request request)
        {
            return new Response.Builder()
                .Code(499)
                .Protocol(Protocol.Http11)
                .Message(string.Empty)
                .Request(request)
                .Build();
        }

        protected override PlayerView OnPrepareControl(ElementChangedEventArgs<VideoView> e)
        {
            return new PlayerView(Context);
        }

#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
        [Obsolete]
        protected override void OnInitialize(ElementChangedEventArgs<VideoView> e)
        {
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
            base.OnInitialize(e);

            httpFactory = CreateHttpDataSourceFactory(Context, "MediaPlayer.Android");
            player = CreatePlayerWithDrm(Context, httpFactory);
            AddPlayerListeners();
            Control.Player = player;
            Element.VideoPlayerReadyCommand?.Execute(this);
        }

        protected override void OnCleanUp(ElementChangedEventArgs<VideoView> e)
        {
            base.OnCleanUp(e);
            StopPositionListenerInterval();
        }

        private void AddPlayerListeners()
        {
            player.AddListener(this);
            player.AddTextOutput(this);
#pragma warning disable CS0618 // Type or member is obsolete
            player.SetMetadataOutput(this);
            player.AddVideoDebugListener(this);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private void RemovePlayerListeners()
        {
            player.RemoveListener(this);
            player.RemoveTextOutput(this);
#pragma warning disable CS0618 // Type or member is obsolete
            player.RemoveMetadataOutput(this);
            player.RemoveVideoDebugListener(this);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopPositionListenerInterval();
                dataSourceFactory?.Dispose();
                RemovePlayerListeners();
                player.Release();
            }
            base.Dispose(disposing);
        }

        private void StartPositionListenerInterval()
        {
            timerIsRunning = true;
            StartTimer(TimeSpan.FromSeconds(Element.UpdateIntervalInSeconds), () =>
            {
                if (timerIsRunning && State == VideoState.Playing)
                {
                    ReportPositionChanged(CurrentPosition, BufferedPosition);
                }
                return timerIsRunning;
            });
        }

        private void StopPositionListenerInterval()
        {
            timerIsRunning = false;
        }

        private void ReportVideoState(VideoState newState)
        {
            State = newState;
            VideoStateChanged?.Invoke(this, new VideoStateChangedEventArgs(newState));
            Element.ReportVideoStateChanged(newState);
        }

        private void ReportVideoBuffering(bool isBuffering)
        {
            Buffering?.Invoke(this, new VideoBufferingEventArgs(isBuffering));
        }

        private void ReportPositionChanged(TimeSpan newPosition, TimeSpan newBufferedPosition)
        {
            // TODO fix the interval listener and remove the null conditional operator
            CurrentPositionChanged?.Invoke(this, new PositionChangedEventArgs(newPosition, newBufferedPosition));
            Element?.ReportCurrentPositionChanged(newPosition, newBufferedPosition);
        }

        public bool AutoPlay { get; set; }

        public bool ShowDefaultControls
        {
            get => Control.UseController;
            set => Control.UseController = value;
        }

        public TimeSpan Duration => TimeSpan.FromMilliseconds(GetDuration());

        public TimeSpan CurrentPosition => TimeSpan.FromMilliseconds(player.CurrentPosition);

        public TimeSpan BufferedPosition => TimeSpan.FromMilliseconds(player.BufferedPosition);

        public double DurationMilliseconds => Duration.TotalMilliseconds;

        public VideoState State { get; private set; }

        public bool IsSeeking { get; private set; }

        // TODO remove Math.Max but guarantee that player.Duration is non-negative number
        private long GetDuration()
        {
            return Math.Max(0, player.Duration == C.TimeUnset ? 0 : player.Duration);
        }

        public void Authorize(string authorizationValue)
        {
            httpFactory.DefaultRequestProperties.Set("Authorization", authorizationValue);
        }

        private TaskCompletionSource<VideoLoadStatus> loadCompletion;
        [Obsolete]
        public Task<VideoLoadStatus> FromUrl(Uri source, CancellationToken cancellationToken)
        {
            StopPositionListenerInterval();
            //var mediaSource = CreateDashMediaSource(source);
            var mediaSource = CreateHlsMediaSource(source.AbsoluteUri);
            player.Prepare(mediaSource);
            ReportVideoState(VideoState.Idle);
            StartPositionListenerInterval();
            ConfigurePlayer();
            loadCompletion = new TaskCompletionSource<VideoLoadStatus>();
            return loadCompletion.Task;
        }

        public Task<VideoLoadStatus> FromLocal(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(VideoLoadStatus.Failed);
        }

        public Task<VideoLoadStatus> FromResource(string name, string extension, CancellationToken cancellationToken)
        {
            return Task.FromResult(VideoLoadStatus.Failed);
        }

        private void ConfigurePlayer()
        {
            if (Element.ShowDefaultControls)
            {
                ShowDefaultControls = true;
            }

            if (Element.AutoPlay)
            {
                AutoPlay = true;
            }

            if (AutoPlay)
            {
                Play();
            }
        }

        private IMediaSource CreateHlsMediaSource(string source)
        {
            if (dataSourceFactory == null)
            {
                dataSourceFactory = new DefaultHlsDataSourceFactory(httpFactory);
            }
            return new HlsMediaSource.Factory(dataSourceFactory).CreateMediaSource(global::Android.Net.Uri.Parse(source));
        }

        public void Pause()
        {
            player.PlayWhenReady = false;
        }

        public void Play()
        {
            player.PlayWhenReady = true;
        }

        public void Stop()
        {
            player.Stop();
        }

        public Task<bool> Forward(TimeSpan step)
        {
            var position = CurrentPosition;
            var totalSteps = position + step;
            return SeekTo(totalSteps);
        }

        public Task<bool> Backward(TimeSpan step)
        {
            var position = CurrentPosition;
            var totalSteps = position - step;
            return SeekTo(totalSteps);
        }

        private TaskCompletionSource<bool> seekCompletion;
        public async Task<bool> SeekTo(TimeSpan position)
        {
            IsSeeking = true;

            var newPosition = Math.Clamp(position.TotalMilliseconds, 0, Duration.TotalMilliseconds);
            ReportPositionChanged(TimeSpan.FromMilliseconds(newPosition), BufferedPosition);
            player.SeekTo((long)newPosition);
            seekCompletion = new TaskCompletionSource<bool>();
            var result = await seekCompletion.Task;

            IsSeeking = false;

            return result;
        }

        public void PreLoad(string source, int sizeInBytes)
        {
            throw new NotImplementedException();
        }

        public void CancelPendingSeeks()
        {
            // No need to implement
            // as ExoPlayer is better compared to AVPlayer
        }

        #region Interface Definitions

        public void OnLoadingChanged(bool isLoading)
        {
        }

        public void OnPlaybackParametersChanged(PlaybackParameters playbackParameters)
        {

        }

        public void OnPlayerError(ExoPlaybackException error)
        {
            PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(error.GetBaseException()));
        }

        public void OnPlayerStateChanged(bool playWhenReady, int playbackState)
        {
            var isBuffering = playbackState == Player.StateBuffering;
            ReportVideoBuffering(isBuffering);
            switch (playbackState)
            {
                case Player.StateEnded:
                    ReportVideoState(VideoState.Ended);
                    break;
                case Player.StateIdle:
                    ReportVideoState(VideoState.Idle);
                    break;
                case Player.StateReady:
                    loadCompletion.TrySetResult(VideoLoadStatus.Loaded);
                    ReportVideoState(playWhenReady ? VideoState.Playing : VideoState.Paused);
                    break;
            }
        }

        public void OnPositionDiscontinuity(int reason)
        {

        }

        public void OnRepeatModeChanged(int repeatMode)
        {

        }

        public void OnSeekProcessed()
        {
            seekCompletion?.TrySetResult(true);
        }

        public void OnShuffleModeEnabledChanged(bool shuffleModeEnabled)
        {

        }

        public void OnTimelineChanged(Timeline timeline, Java.Lang.Object manifest, int reason)
        {

        }

        public void OnTracksChanged(TrackGroupArray trackGroups, TrackSelectionArray trackSelections)
        {
            HandleTracksChanged(trackGroups, trackSelections);
        }

        public void OnMetadata(Metadata metadata)
        {
            for (var i = 0; i < metadata.Length(); ++i)
            {
                var m = metadata.Get(i);
                var a = m;
            }
        }

        private void HandleTracksChanged(TrackGroupArray trackGroups, TrackSelectionArray trackSelections)
        {
            for (int i = 0; i < trackGroups.Length; i++)
            {
                var trackGroup = trackGroups.Get(i);
                for (int j = 0; j < trackGroup.Length; j++)
                {
                    var trackMetadata = trackGroup.GetFormat(j).Metadata;
                    if (trackMetadata != null)
                    {
                        var entry = trackMetadata.Get(0);
                        Log("metadata: {0}", entry);
                    }
                }
            }
        }

        public void OnCues(IList<Cue> cues)
        {
            foreach (var c in cues)
            {
                Log("line: {0}", c.Text.ToString());
            }
        }

        public void OnDroppedFrames(int count, long elapsedMs)
        {
            Log("OnDroppedFrames(count: {0}, elapsedMs: {1})", count, elapsedMs);
        }

        public void OnRenderedFirstFrame(Surface surface)
        {
            Log("OnRenderedFirstFrame(surface:)");
        }

        public void OnVideoDecoderInitialized(string decoderName, long initializedTimestampMs, long initializationDurationMs)
        {
            Log("OnVideoDecoderInitialized(decoderName:initializedTimestampMs:initializationDurationMs:)");
        }

        public void OnVideoDisabled(DecoderCounters counters)
        {
            Log("OnVideoDisabled(surface:)");
        }

        public void OnVideoEnabled(DecoderCounters counters)
        {
            Log("OnVideoEnabled(surface:)");
        }

        public void OnVideoInputFormatChanged(Format format)
        {
            Log("OnVideoInputFormatChanged(surface:)");
        }

        public void OnVideoSizeChanged(int width, int height, int unappliedRotationDegrees, float pixelWidthHeightRatio)
        {
            Log("OnVideoSizeChanged(width:height:width:unappliedRotationDegrees:pixelWidthHeightRatio)");
        }

        #endregion

        #region Helpers

        private static void Log(string message, params object[] args)
        {
            XamarinUtility.Debug.Log(message, args);
        }

        [Obsolete]
        private static DefaultTrackSelector CreateTrackSelector(Context context)
        {
            var bandwidthMeter = new DefaultBandwidthMeter();
            var videoTrackSelectionFactory = new AdaptiveTrackSelection.Factory(bandwidthMeter);
            var trackSelector = new DefaultTrackSelector(videoTrackSelectionFactory);
            var trackParameter = DefaultTrackSelector.Parameters.GetDefaults(context);
            trackParameter.DisabledTextTrackSelectionFlags = C.TrackTypeText;  // disables closed caption
            trackSelector.SetParameters(trackParameter);
            return trackSelector;
        }

        [Obsolete]
        private static DefaultLoadControl CreateLoadControl(int bufferSize)
        {
            var allocator = new DefaultAllocator(true, C.DefaultBufferSegmentSize);
            var maxBufferSize = (int)(bufferSize * 1.5);
            const int playbackRebuffer = DefaultLoadControl.DefaultBufferForPlaybackAfterRebufferMs;
            const int playbackBuffer = DefaultLoadControl.DefaultBufferForPlaybackMs;
            return new DefaultLoadControl(allocator,
                maxBufferMs: maxBufferSize,
                minBufferMs: bufferSize,
                bufferForPlaybackAfterRebufferMs: playbackRebuffer,
                bufferForPlaybackMs: playbackBuffer,
                prioritizeTimeOverSizeThresholds: true,
                targetBufferBytes: -1);
        }

        // https://github.com/xamarin/Xamarin.Forms/blob/5246fe14ccc03e298562819febc7a11c3f104e25/Xamarin.Forms.Platform.Android/Forms.cs#L705
        private static void StartTimer(TimeSpan interval, Func<bool> shoulLoop)
        {
            var handler = new Handler(Looper.MainLooper);
            handler.PostDelayed(() =>
            {
                if (shoulLoop())
                    StartTimer(interval, shoulLoop);

                handler.Dispose();
                handler = null;
            }, (long)interval.TotalMilliseconds);
        }

        [Obsolete]
        private static SimpleExoPlayer CreatePlayerWithDrm(Context context, OkHttpDataSourceFactory httpFactory)
        {
            //var handler = new Handler();
            //var bandwidthMeter = new DefaultBandwidthMeter();
            var drmLicenseUrl = "https://license.uat.widevine.com/cenc/getcontentkey/widevine_test";
            var drmCallback = new HttpMediaDrmCallback(drmLicenseUrl,
                httpFactory
            );
            var keyRequest = new Dictionary<string, string>();
            var drmSessionManager = new DefaultDrmSessionManager(C.WidevineUuid,
                FrameworkMediaDrm.NewInstance(C.WidevineUuid), drmCallback, keyRequest);

            const int bufferSize = 3 * 60 * 1000; // 3 MiB?
            var trackSelector = CreateTrackSelector(context);
            var loadControl = CreateLoadControl(bufferSize);
            var renderer = new DefaultRenderersFactory(context, drmSessionManager);
            return ExoPlayerFactory.NewSimpleInstance(context, renderer, trackSelector, loadControl);
        }

        [Obsolete]
        private static SimpleExoPlayer CreatePlayer(Context context)
        {
            // TODO this maybe not the actual buffer size
            // as of this writing, I just assume it is since
            // the documentation for this is unclear
            const int bufferSize = 3 * 60 * 1000; // 3 MiB?
            var trackSelector = CreateTrackSelector(context);
            var loadControl = CreateLoadControl(bufferSize);
            return ExoPlayerFactory.NewSimpleInstance(context, trackSelector, loadControl);
        }

        private static OkHttpDataSourceFactory CreateHttpDataSourceFactory(Context context, string applicationName)
        {
            var userAgent = Util.GetUserAgent(context, applicationName);
            return new OkHttpDataSourceFactory(httpClient, userAgent);
        }

        //private static IMediaSource CreateDashMediaSource(OkHttpDataSourceFactory httpFactory, string source)
        //{
        //    var dashFactory = new DefaultDashChunkSource.Factory(httpFactory);
        //    return new DashMediaSource.Factory(dashFactory, httpFactory).CreateMediaSource(Android.Net.Uri.Parse(source));
        //}

        #endregion
    }
}
