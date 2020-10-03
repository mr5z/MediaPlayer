using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using AVKit;
using CoreMedia;
using Foundation;
using MediaPlayer;
using MediaPlayer.iOS;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;
using XamarinUtility.Extensions;

[assembly: ExportRenderer(typeof(VideoView), typeof(VideoViewRenderer))]
namespace MediaPlayer.iOS
{
    public class VideoViewRenderer : CustomViewRenderer<VideoView, UIView>, IVideoPlayer
    {
        // KVOs
        private NSObject errorLogObserver;
        private NSObject accessLogObserver;
        private NSObject playbackErrorObserver;
        private NSObject playToEndObserver;
        private NSObject statusObserver;
        private NSObject playbackBufferEmptyObserver;
        private NSObject playbackLikelyToKeepUpObserver;
        private NSObject playbackBufferFullObserver;
        private NSObject videoIntervalObserver;

        private readonly AVPlayer player = new AVPlayer();
        private AVPlayerItem currentPlayerItem;
        private readonly AVPlayerViewController viewController = new AVPlayerViewController();

        public bool AutoPlay { get; set; }

        public bool ShowDefaultControls
        {
            get => viewController.ShowsPlaybackControls;
            set => viewController.ShowsPlaybackControls = value;
        }

        public TimeSpan Duration => TimeSpan.FromSeconds(GetDurationInSeconds());

        public TimeSpan CurrentPosition => TimeSpan.FromSeconds(player.CurrentTime.Seconds);

        public TimeSpan BufferedPosition => TimeSpan.FromSeconds(GetBufferedPositionInSeconds());

        public double DurationMilliseconds => Duration.TotalMilliseconds;

        private VideoState internalState;
        public VideoState State
        {
            get => GetVideoState();
            private set => internalState = value;
        }

        public bool IsSeeking { get; private set; }

        public event EventHandler<PositionChangedEventArgs> CurrentPositionChanged;
        public event EventHandler<VideoStateChangedEventArgs> VideoStateChanged;
        public event EventHandler<PlaybackErrorEventArgs> PlaybackError;
        public event EventHandler<StreamingResponseEventArgs> StreamingResponse;
        public event EventHandler<VideoBufferingEventArgs> Buffering;

        #region View Renderer Events

        protected override UIView OnPrepareControl(ElementChangedEventArgs<VideoView> e)
        {
            viewController.Player = player;
            return viewController.View;
        }

        protected override void OnInitialize(ElementChangedEventArgs<VideoView> e)
        {
            base.OnInitialize(e);

            // TODO not sure about the behavior of these
            player.ExternalPlaybackVideoGravity = AVLayerVideoGravity.ResizeAspect;
            viewController.VideoGravity = AVLayerVideoGravity.ResizeAspect;
            //viewController.EntersFullScreenWhenPlaybackBegins = true;
            //viewController.ExitsFullScreenWhenPlaybackEnds = true;

            Element.VideoPlayerReadyCommand?.Execute(this);
        }

        protected override void OnCleanUp()
        {
            base.OnCleanUp();
            accessLogObserver?.Dispose();
            playbackErrorObserver?.Dispose();
            errorLogObserver?.Dispose();
            playToEndObserver?.Dispose();
            statusObserver?.Dispose();
            playbackBufferEmptyObserver?.Dispose();
            playbackLikelyToKeepUpObserver?.Dispose();
            playbackBufferFullObserver?.Dispose();
            StopPositionListenerInterval();
            player.ReplaceCurrentItemWithPlayerItem(null);
            currentPlayerItem = null;
        }

        #endregion

        #region Player Item Delegates

        private void AVPlayerItem_FailedPlayToEndHandler(object sender, AVPlayerItemErrorEventArgs e)
        {
            var exception = new Exception(e.Error.LocalizedDescription);
            PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(exception));
        }

        private void AVPlayerItem_NewAccessLogHandler(object sender, NSNotificationEventArgs e)
        {
            var item = e.Notification.Object as AVPlayerItem;
            var accessLog = item.AccessLog;
            var events = accessLog.Events;
            var lastEvent = events.LastOrDefault();
            if (lastEvent != null)
            {
                //Debug.Log($@"
                //BytesTransferred: {lastEvent.BytesTransferred},
                //IndicatedAverageBitrate: {lastEvent.IndicatedAverageBitrate},
                //IndicatedBitrate: {lastEvent.IndicatedBitrate},
                //ObservedBitrate: {lastEvent.ObservedBitrate},
                //ObservedBitrateStandardDeviation: {lastEvent.ObservedBitrateStandardDeviation},
                //ObservedMaxBitrate: {lastEvent.ObservedMaxBitrate},
                //ObservedMinBitrate: {lastEvent.ObservedMinBitrate},
                //TransferDuration: {lastEvent.TransferDuration},
                //SegmentedDownloadedCount: {lastEvent.SegmentedDownloadedCount},
                //PlaybackType: {lastEvent.PlaybackType},
                //StallCount: {lastEvent.StallCount},
                //SwitchBitrate: {lastEvent.SwitchBitrate}
                //");
                StreamingResponse?.Invoke(this, new StreamingResponseEventArgs(lastEvent.BytesTransferred));
            }
        }

        private void AVPlayerItem_NewErrorLogHandler(object sender, NSNotificationEventArgs e)
        {
            var item = e.Notification.Object as AVPlayerItem;
            var accessLog = item.ErrorLog;
            var events = accessLog.Events;
            var lastEvent = events.LastOrDefault();
            if (lastEvent != null)
            {
                var exception = new Exception($"statusCode: {lastEvent.ErrorStatusCode}, message: {lastEvent.ErrorComment}, domain: {lastEvent.ErrorDomain}");
                PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(exception));
            }
        }

        private void AVPlayerItem_DidPlayToEnd(object sender, NSNotificationEventArgs e)
        {
            ReportVideoState(VideoState.Ended);
        }

        private void AVPlayerItem_PlaybackBufferEmpty(NSObservedChange e)
        {
            ReportVideoBuffering();
        }

        private void AVPlayerItem_PlaybackBufferLikelyToKeepUp(NSObservedChange e)
        {
            ReportVideoBuffering();
        }

        private void AVPlayerItem_PlaybackBufferFull(NSObservedChange e)
        {
            ReportVideoBuffering();
        }

        private void AVPlayerItem_StatusChanged(NSObservedChange e)
        {
            var intStatus = int.Parse(e.NewValue.ToString());
            var status = (AVPlayerItemStatus)intStatus;
            switch (status)
            {
                case AVPlayerItemStatus.Unknown:
                    ReportVideoState(VideoState.NotReady);
                    break;
                case AVPlayerItemStatus.Failed:
                    loadCompletion.TrySetResult(VideoLoadStatus.Failed);
                    ReportVideoState(VideoState.Failed);
                    break;
                case AVPlayerItemStatus.ReadyToPlay:
                    var videoState = GetVideoState();
                    var asset = currentPlayerItem.Asset;
                    loadCompletion.TrySetResult(asset.Playable ? VideoLoadStatus.Loaded : VideoLoadStatus.Unplayable);
                    if (videoState != VideoState.Idle)
                    {
                        ReportVideoState(videoState);
                    }
                    break;
            }
        }

        #endregion

        private bool timerIsRunning;
        private void StartPositionListenerInterval()
        {
            timerIsRunning = true;
            videoIntervalObserver = player.AddPeriodicTimeObserver(
                CMTime.FromSeconds(Element.UpdateIntervalInSeconds, 1),
                CoreFoundation.DispatchQueue.MainQueue, (e) =>
                {
                    if (timerIsRunning && currentPlayerItem != null)
                    {
                        ReportVideoState(State);
                        ReportPositionChanged(recentSeekPosition ?? CurrentPosition, BufferedPosition);
                        recentSeekPosition = null;
                    }
                });
        }

        private void StopPositionListenerInterval()
        {
            timerIsRunning = false;
            if (videoIntervalObserver != null)
            {
                player.RemoveTimeObserver(videoIntervalObserver);
                videoIntervalObserver?.Dispose();
                videoIntervalObserver = null;
            }
        }

        private AVUrlAssetOptions GetOptions()
        {
            var nsDictionary = NSDictionary
                .FromObjectAndKey(
                    FromObject(authorizationValue),
                    FromObject("Authorization"));

            var wrappedDictionary = NSDictionary
                .FromObjectAndKey(nsDictionary, FromObject("AVURLAssetHTTPHeaderFieldsKey"));

            return new AVUrlAssetOptions(wrappedDictionary);
        }

        private string authorizationValue;
        public void Authorize(string authorizationValue)
        {
            this.authorizationValue = authorizationValue;
        }

        private void ConfigurePlayer(AVAsset asset)
        {
            currentPlayerItem = new AVPlayerItem(asset);
            currentPlayerItem.PreferredForwardBufferDuration = 60 * 2; // 2 minutes?
            AddPlayerItemListeners(currentPlayerItem);
            player.ReplaceCurrentItemWithPlayerItem(currentPlayerItem);
            StartPositionListenerInterval();
            loadCompletion = new TaskCompletionSource<VideoLoadStatus>();

            if (Element.AutoPlay)
            {
                AutoPlay = true;
            }

            if (Element.ShowDefaultControls)
            {
                ShowDefaultControls = true;
            }

            if (AutoPlay)
            {
                Play();
            }
        }

        private Task<VideoLoadStatus> LoadVideo(AVAsset asset, CancellationToken cancellationToken)
        {
            StopPositionListenerInterval();
            ConfigurePlayer(asset);
            cancellationToken.Register(() => loadCompletion.TrySetResult(VideoLoadStatus.Timeout));
            return loadCompletion.Task;
        }

        public Task<VideoLoadStatus> FromLocal(string filePath, CancellationToken cancellationToken)
        {
            var asset = AVAsset.FromUrl(NSUrl.FromString(filePath));
            return LoadVideo(asset, cancellationToken);
        }

        public Task<VideoLoadStatus> FromResource(string name, string extension, CancellationToken cancellationToken)
        {
            var path = NSUrl.CreateFileUrl(new [] { NSBundle.MainBundle.PathForResource(name, extension) });
            //var path = NSUrl.FromString(NSBundle.MainBundle.PathForResource(name, extension));
            var asset = AVAsset.FromUrl(path);
            return LoadVideo(asset, cancellationToken);
        }

        private TaskCompletionSource<VideoLoadStatus> loadCompletion;
        public Task<VideoLoadStatus> FromUrl(Uri source, CancellationToken cancellationToken)
        {
            var asset = authorizationValue == null ?
                AVUrlAsset.Create(source) :
                AVUrlAsset.Create(source, GetOptions());
            // TODO uncomment to setup for FairPlay
            //asset.ResourceLoader.SetDelegate()
            return LoadVideo(asset, cancellationToken);
        }

        public void Pause()
        {
            player.Pause();
        }

        public void Play()
        {
            player.Play();
        }

        public void Stop()
        {
            player.Pause();
            SeekTo(Duration).FireAndForget();
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

        private TimeSpan? recentSeekPosition;
        public async Task<bool> SeekTo(TimeSpan position)
        {
            IsSeeking = true;
            var newPosition = Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds);
            var time = CMTime.FromSeconds(newPosition, 1);
            // For now, we want accuracy over performance
            // CMTime.FromSeconds to increase Seek performance
            var timeTolerance = CMTime.Zero;
            var timeSpanPosition = TimeSpan.FromSeconds(newPosition);
            recentSeekPosition = timeSpanPosition;
            var result = await player.SeekAsync(time, timeTolerance, timeTolerance);
            ReportPositionChanged(timeSpanPosition, BufferedPosition);
            IsSeeking = false;
            return result;
        }

        public void PreLoad(string source, int sizeInBytes)
        {
            throw new NotImplementedException();
        }

        public void CancelPendingSeeks()
        {
            currentPlayerItem.CancelPendingSeeks();
        }

        private void AddPlayerItemListeners(AVPlayerItem playerItem)
        {
            playbackErrorObserver?.Dispose();
            playbackErrorObserver = AVPlayerItem.Notifications
                .ObserveItemFailedToPlayToEndTime(playerItem, AVPlayerItem_FailedPlayToEndHandler);

            accessLogObserver?.Dispose();
            accessLogObserver = AVPlayerItem.Notifications
                .ObserveNewAccessLogEntry(playerItem, AVPlayerItem_NewAccessLogHandler);

            errorLogObserver?.Dispose();
            errorLogObserver = AVPlayerItem.Notifications
                .ObserveNewErrorLogEntry(playerItem, AVPlayerItem_NewErrorLogHandler);

            playToEndObserver?.Dispose();
            playToEndObserver = AVPlayerItem.Notifications
                .ObserveDidPlayToEndTime(playerItem, AVPlayerItem_DidPlayToEnd);

            statusObserver?.Dispose();
            statusObserver = (NSObject)playerItem.AddObserver("status",
                NSKeyValueObservingOptions.OldNew,
                AVPlayerItem_StatusChanged);

            playbackBufferEmptyObserver?.Dispose();
            playbackBufferEmptyObserver = (NSObject)playerItem.AddObserver("playbackBufferEmpty",
                NSKeyValueObservingOptions.New,
                AVPlayerItem_PlaybackBufferEmpty);

            playbackLikelyToKeepUpObserver?.Dispose();
            playbackLikelyToKeepUpObserver = (NSObject)playerItem.AddObserver("playbackLikelyToKeepUp",
                NSKeyValueObservingOptions.New,
                AVPlayerItem_PlaybackBufferLikelyToKeepUp);

            playbackBufferFullObserver?.Dispose();
            playbackBufferFullObserver = (NSObject)playerItem.AddObserver("playbackBufferFull",
                NSKeyValueObservingOptions.New,
                AVPlayerItem_PlaybackBufferFull);
        }

        private void ReportVideoState(VideoState newState)
        {
            if (internalState == newState)
                return;

            internalState = newState;
            VideoStateChanged?.Invoke(this, new VideoStateChangedEventArgs(newState));
            // TODO find the reason why Element gets disposed abnormally
            Element?.ReportVideoStateChanged(newState);
        }

        private void ReportVideoBuffering()
        {
            var isBuffering = !currentPlayerItem.PlaybackLikelyToKeepUp;
            Buffering?.Invoke(this, new VideoBufferingEventArgs(isBuffering));
        }

        private void ReportPositionChanged(TimeSpan newPosition, TimeSpan newBufferedPosition)
        {
            CurrentPositionChanged?.Invoke(this, new PositionChangedEventArgs(newPosition, newBufferedPosition));
            Element.ReportCurrentPositionChanged(newPosition, newBufferedPosition);
        }

        private VideoState GetVideoState()
        {
            // Get the current item status, not the player itself
            // see ref: https://developer.apple.com/documentation/avfoundation/avplayer/1388096-status?language=objc#discussion
            var status = currentPlayerItem.Status;

            return status switch
            {
                AVPlayerItemStatus.ReadyToPlay => OnReadyToPlayState(player.TimeControlStatus),
                AVPlayerItemStatus.Failed => VideoState.NotReady,
                AVPlayerItemStatus.Unknown => VideoState.NotReady,
                _ => internalState
            };

            VideoState OnReadyToPlayState(AVPlayerTimeControlStatus status) => status switch
            {
                AVPlayerTimeControlStatus.Paused => VideoState.Paused,
                AVPlayerTimeControlStatus.Playing => VideoState.Playing,
                AVPlayerTimeControlStatus.WaitingToPlayAtSpecifiedRate => VideoState.Idle,
                _ => internalState
            };
        }

        //private static bool IsWaitingToMinimizeStalls(AVPlayer player)
        //{
        //    var reason = player.ReasonForWaitingToPlay;
        //    return reason == AVPlayer.WaitingToMinimizeStallsReason;
        //}

        private double GetBufferedPositionInSeconds()
        {
            var loadedTimeRanges = currentPlayerItem.LoadedTimeRanges;
            if (!loadedTimeRanges.Any())
                return 0;

            var timeRange = loadedTimeRanges.First().CMTimeRangeValue;
            var startSeconds = timeRange.Start.Seconds;
            var durationSeconds = timeRange.Duration.Seconds;
            return startSeconds + durationSeconds;
        }

        private double GetDurationInSeconds()
        {
            if (currentPlayerItem == null ||
                currentPlayerItem.Asset == null ||
                double.IsNaN(currentPlayerItem.Asset.Duration.Seconds))
                return 0;
            return currentPlayerItem.Asset.Duration.Seconds;
        }

        /*
         * 
         * D I G I T A L - R I G H T S - M A N A G E M E N T
         *
         */

        private HashSet<string> pendingPersistableContentKeyIdentifiers = new HashSet<string>();

        private bool ShouldRequestPersistableContentKey(string identifier)
        {
            return pendingPersistableContentKeyIdentifiers.Contains(identifier);
        }

        private bool PersistableContentKeyExistsOnDisk(string identifier)
        {
            // TODO check if it exist on disk
            return false;
        }

        private NSData RequestApplicationCertificate()
        {
            // TODO You must implement this method to retrieve your FPS application certificate.
            return new NSData("", NSDataBase64DecodingOptions.IgnoreUnknownCharacters);
        }

        private NSData RequestContentKeyFromKeySecurityModule(NSData spcData, string assetId)
        {
            // TODO You must implement this method to request a CKC from your KSM.
            return null;
        }

        private void PrepareAndSendContentKeyRequest(AVAssetResourceLoadingRequest loadingRequest)
        {
            var contentTypes = loadingRequest.ContentInformationRequest?.AllowedContentTypes;
            if (!contentTypes.Contains(AVStreamingKeyDelivery.ContentKeyType))
            {
                ProvideOnlineKey(loadingRequest);
                return;
            }

            var url = loadingRequest.Request.Url;
            var assetIdString = url.Host;

            if (ShouldRequestPersistableContentKey(assetIdString) ||
                PersistableContentKeyExistsOnDisk(assetIdString))
            {

            }
        }

        private void ProvideOnlineKey(AVAssetResourceLoadingRequest loadingRequest)
        {
            try
            {
                var url = loadingRequest.Request.Url;
                var assetIdString = url.Host;
                var assetIdData = NSData.FromString(assetIdString);

                if (string.IsNullOrEmpty(assetIdString))
                {
                    return;
                }

                var certificate = RequestApplicationCertificate();
                var spcData = loadingRequest.GetStreamingContentKey(certificate, assetIdData, null, out var error);
                error?.Dispose();

                // Send SPC to Key Server and obtain CKC.
                var ckcData = RequestContentKeyFromKeySecurityModule(spcData, assetIdString);

                loadingRequest.DataRequest?.Respond(ckcData);

                // You should always set the contentType before calling finishLoading() to make sure you
                // have a contentType that matches the key response.
                loadingRequest.ContentInformationRequest.ContentType = AVStreamingKeyDelivery.ContentKeyType;
                loadingRequest.FinishLoading();
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                loadingRequest.FinishLoadingWithError(null);
            }
        }

        private object RequestPersistableContentKeys()
        {
            var asset = new NSDataAsset("");
            //var asset = currentPlayerItem.Asset as AVUrlAsset;
            //foreach (var id in asset.key)
            return null;
        }


    }
}
