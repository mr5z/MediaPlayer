using System;
using Xamarin.Forms.Platform.iOS;

namespace MediaPlayer.iOS
{
    public abstract class CustomViewRenderer<TView, TNativeView> : ViewRenderer<TView, TNativeView>
        where TView : Xamarin.Forms.View
        where TNativeView : UIKit.UIView
    {
        protected override sealed void OnElementChanged(ElementChangedEventArgs<TView> e)
        {
            base.OnElementChanged(e);

            if (Control == null)
            {
                var nativeView = OnPrepareControl(e);
                SetNativeControl(nativeView);
            }

            if (e.NewElement != null)
            {
                OnInitialize(e);
            }

            if (e.OldElement != null)
            {
                // TODO should we put the OnCleanUp here?
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                OnCleanUp();
            }
        }

        protected virtual void OnInitialize(ElementChangedEventArgs<TView> e)
        {

        }

        protected virtual void OnCleanUp()
        {

        }

        protected abstract TNativeView OnPrepareControl(ElementChangedEventArgs<TView> e);
    }
}
