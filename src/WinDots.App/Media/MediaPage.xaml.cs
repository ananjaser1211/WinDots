using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using System.Threading;
using WinDots.App.LastFm;
using WinDots.App.Media.Controls;
using WinDots.App.Shell;
using WinDots.Core.Scrobbling;
using WinDots.Core.Visualiser;

namespace WinDots.App.Media;

/// <summary>
/// The Media tab surface: a single-item tab strip over a three-column body (blob artwork with a dotted progress ring,
/// metadata + seek + transport, and a lyrics slot with the player chooser). All state comes from a
/// <see cref="MediaViewModel"/> bound one-way; control events are routed to the view-model's fallible commands.
/// Keyboard handling follows _docs/03-ux-interaction-spec.md.
/// </summary>
public sealed partial class MediaPage : UserControl
{
    private MediaViewModel? _viewModel;

    // Reflows to a two-row layout above this text-scale factor or below this width (_docs/03-ux-interaction-spec.md).
    private const double TextScaleReflowThreshold = 1.5;
    private const double NarrowReflowWidth = 620;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    // Idle-motion state for the background blobs (_docs/04-visual-design.md). Loops are suspended under reduced
    // motion or high contrast, and the whole canvas is hidden when appearance.backgroundBlobs is off.
    private bool _backgroundBlobs = true;
    private bool _reducedMotion;
    private bool _highContrast;
    private bool _blobDriftRunning;

    // The drawer is a tray-resident window shown/hidden via AppWindow.Hide, which does not unload this page. Idle
    // motion (background-blob drift and the artwork phase drift) must pause while the drawer is hidden, or the
    // composition loops and the 10 Hz geometry rebuild keep running on the UI thread forever. Starts false so nothing
    // animates before the drawer is first shown.
    private bool _drawerVisible;

    public MediaPage()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        ActualThemeChanged += OnActualThemeChanged;

        // Composition animations need a live compositor, which only exists once the page is loaded. Re-apply the
        // stored effects then; before Loaded, SetVisualEffects only records intent.
        Loaded += (_, _) =>
        {
            ApplyBlobDrift();
            EvaluateLayout();
        };
        Unloaded += (_, _) =>
        {
            StopBlobDrift();
            _uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
        };
        SizeChanged += (_, _) => EvaluateLayout();
        _uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
    }

    private void OnTextScaleFactorChanged(global::Windows.UI.ViewManagement.UISettings sender, object args) =>
        DispatcherQueue?.TryEnqueue(EvaluateLayout);

    // Picks the wide (three-column) or stacked (two-row) layout. Stacks when the OS text scale exceeds 150 % or the
    // available width is too narrow for the lyrics column, keeping everything inside the 720x300 drawer via the
    // surrounding ScrollViewer.
    private void EvaluateLayout()
    {
        bool stacked = _uiSettings.TextScaleFactor > TextScaleReflowThreshold || (ActualWidth > 0 && ActualWidth < NarrowReflowWidth);
        VisualStateManager.GoToState(this, stacked ? "StackedLayout" : "WideLayout", useTransitions: false);
    }

    /// <summary>
    /// Applies the appearance settings that drive idle motion: the background-blob canvas visibility and drift, the
    /// artwork blob's phase drift, and the high-contrast treatment of the ring and blob. Called at startup and on
    /// every live settings / accessibility change.
    /// </summary>
    public void SetVisualEffects(bool backgroundBlobs, bool reducedMotion, bool highContrast)
    {
        _backgroundBlobs = backgroundBlobs;
        _reducedMotion = reducedMotion;
        _highContrast = highContrast;

        ProgressRing.HighContrast = highContrast;
        Artwork.HighContrast = highContrast;
        Lyrics.SetReduceMotion(reducedMotion || highContrast);

        bool motionAllowed = !reducedMotion && !highContrast;
        Artwork.SetReduceMotion(reducedMotion || highContrast);
        Artwork.SetIdleMotion(motionAllowed);

        BlobCanvas.Visibility = backgroundBlobs ? Visibility.Visible : Visibility.Collapsed;
        ApplyBlobDrift();
    }

    private void ApplyBlobDrift()
    {
        // The composition visuals only exist once the page is loaded.
        if (!IsLoaded)
        {
            return;
        }

        if (_backgroundBlobs && !_reducedMotion && !_highContrast && _drawerVisible)
        {
            StartBlobDrift();
        }
        else
        {
            StopBlobDrift();
        }
    }

    /// <summary>
    /// Pauses or resumes idle motion with the drawer's visibility. Called by <see cref="Shell.DrawerWindow"/> on
    /// show/hide because <c>AppWindow.Hide</c> leaves this page loaded (see <see cref="_drawerVisible"/>).
    /// </summary>
    public void SetDrawerVisible(bool visible)
    {
        if (_drawerVisible == visible)
        {
            return;
        }

        _drawerVisible = visible;
        Artwork.SetActive(visible);
        ApplyBlobDrift();
    }

    // Each blob drifts +/-12 px on a looping composition Translation animation over 30 s (offset phases so they do
    // not move in lockstep). Composition-driven so it runs off the UI thread.
    private void StartBlobDrift()
    {
        if (_blobDriftRunning)
        {
            return;
        }

        _blobDriftRunning = true;
        AnimateBlob(Blob1, new Vector3(12, -12, 0), 0.0);
        AnimateBlob(Blob2, new Vector3(-12, 12, 0), 0.33);
        AnimateBlob(Blob3, new Vector3(10, 12, 0), 0.66);
    }

    private void StopBlobDrift()
    {
        // Stopping an animation on a visual whose "Translation" was never enabled throws E_INVALIDARG and takes the
        // process down (it happened at startup when the drift was disabled before ever starting); only undo what ran.
        if (!_blobDriftRunning)
        {
            return;
        }

        _blobDriftRunning = false;
        ResetBlob(Blob1);
        ResetBlob(Blob2);
        ResetBlob(Blob3);
    }

    private static void AnimateBlob(Ellipse blob, Vector3 delta, double phase)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(blob, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(blob);
        Compositor compositor = visual.Compositor;

        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0.0f, Vector3.Zero);
        animation.InsertKeyFrame(0.5f, delta);
        animation.InsertKeyFrame(1.0f, Vector3.Zero);
        animation.Duration = TimeSpan.FromSeconds(30);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.DelayTime = TimeSpan.FromSeconds(30 * phase);
        visual.StartAnimation("Translation", animation);
    }

    private static void ResetBlob(Ellipse blob)
    {
        // Translation must be enabled before the property can be referenced at all.
        ElementCompositionPreview.SetIsTranslationEnabled(blob, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(blob);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    // A live Windows theme switch changes the palette's contrast floors; recompute against the new theme.
    private void OnActualThemeChanged(FrameworkElement sender, object args) => _viewModel?.RefreshPalette();

    /// <summary>The bound presentation model. Set once via <see cref="Initialize"/> before the drawer is shown.</summary>
    public MediaViewModel ViewModel => _viewModel!;

    /// <summary>Wires the page to its view-model and refreshes all bindings. Idempotent.</summary>
    public void Initialize(MediaViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Bindings.Update();
        HookLastFm();
    }

    // --- Last.fm love button (E4) ---

    private LastFmService? _lastFm;

    private void HookLastFm()
    {
        if (_lastFm is not null)
        {
            return;
        }

        _lastFm = DrawerHost.Instance?.LastFm;
        if (_lastFm is not null)
        {
            _lastFm.StateChanged += (_, _) => DispatcherQueue?.TryEnqueue(RefreshLoveButton);
        }

        RefreshLoveButton();
    }

    private void RefreshLoveButton()
    {
        LastFmService? svc = _lastFm;
        TrackIdentity? id = svc?.CurrentTrack();
        bool show = svc is { Enabled: true, IsSignedIn: true } && id is not null;
        LoveButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            bool loved = svc!.IsLoved(id!);
            LoveButton.IsChecked = loved;
            LoveIcon.Glyph = loved ? "" : "";
        }
    }

    private async void OnLoveClicked(object sender, RoutedEventArgs e)
    {
        LastFmService? svc = _lastFm;
        TrackIdentity? id = svc?.CurrentTrack();
        if (svc is null || id is null)
        {
            return;
        }

        bool want = LoveButton.IsChecked == true;
        bool ok = await svc.SetLovedAsync(id, want, CancellationToken.None);
        if (!ok)
        {
            LoveButton.IsChecked = !want;
        }

        RefreshLoveButton();
    }

    // Announces play/pause transitions through the hidden Polite live region so Narrator speaks the new state even
    // when focus is elsewhere. Runs on the view-model's UI thread (all its notifications are marshalled there).
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MediaViewModel.IsPlaying):
                PlayStateAnnouncer.Text = _viewModel.IsPlaying ? "Playing" : "Paused";
                break;

            case nameof(MediaViewModel.VisualiserEnergy):
            case nameof(MediaViewModel.VisualiserActive):
            case nameof(MediaViewModel.VisualiserStyle):
                ApplyBlobPulse();
                break;

            default:
                break;
        }
    }

    // The blobPulse style scales the album blob itself (the visualiser control draws nothing for it). Driven by the
    // view-model's energy; reset to identity when the style is inactive. Composition scale so no layout runs.
    private void ApplyBlobPulse()
    {
        bool pulse = _viewModel is not null && _viewModel.VisualiserActive &&
                     _viewModel.VisualiserStyle == VisualiserStyle.BlobPulse;

        Visual visual = ElementCompositionPreview.GetElementVisual(Artwork);
        double w = Artwork.ActualWidth > 0 ? Artwork.ActualWidth : Artwork.Width;
        double h = Artwork.ActualHeight > 0 ? Artwork.ActualHeight : Artwork.Height;
        visual.CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0f);

        if (pulse)
        {
            float s = 1f + (float)(Math.Clamp(_viewModel!.VisualiserEnergy, 0, 1) * 0.10);
            visual.Scale = new Vector3(s, s, 1f);
        }
        else
        {
            visual.Scale = Vector3.One;
        }
    }

    /// <summary>Moves initial focus to the play/pause button (called when the drawer opens).</summary>
    public void FocusDefault() => Transport.FocusPlayPause();

    // --- x:Bind helper functions ---

    private Visibility Shown(bool isEmpty) => isEmpty ? Visibility.Collapsed : Visibility.Visible;

    // The dotted ring shows in the empty state as an all-track ring (DottedRingLayout.ElapsedDots(null,..) is 0)
    // and whenever a duration is known. It is hidden for an active unknown-duration livestream (A3), and while the
    // ring-style visualiser occupies the artwork cell (the radial bars replace the dotted ring, per E5) — but not when
    // the ring visualiser has been placed in the bottom strip, where the dotted ring stays visible around the blob.
    private Visibility RingVisible(
        bool isEmpty,
        TimeSpan? duration,
        bool visualiserActive,
        VisualiserStyle style,
        VisualiserPlacement placement)
    {
        if (visualiserActive && style == VisualiserStyle.Ring && VisualiserLayout.ShowsInArtArea(style, placement))
        {
            return Visibility.Collapsed;
        }

        return isEmpty || duration is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Placement decides the region: art placements keep art-area styles (ring / halo / particles) around the blob;
    // Bottom moves them to the strip. Strip styles (bars / waveform) always sit in the bottom band. See VisualiserLayout.
    private Visibility ArtVisualiserVisible(bool active, VisualiserStyle style, VisualiserPlacement placement) =>
        active && VisualiserLayout.ShowsInArtArea(style, placement) ? Visibility.Visible : Visibility.Collapsed;

    private Visibility StripVisualiserVisible(bool active, VisualiserStyle style, VisualiserPlacement placement) =>
        active && VisualiserLayout.ShowsInStrip(style, placement) ? Visibility.Visible : Visibility.Collapsed;

    // Artwork-cell depth for the art-area visualiser, driven by placement (over / between / behind the blob).
    private int ArtVisualiserZ(VisualiserPlacement placement) => VisualiserLayout.ArtZIndex(placement);

    private Visibility HasText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility Vis(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private string Percent(int level) => level.ToString(System.Globalization.CultureInfo.CurrentCulture) + "%";

    private string MuteName(bool muted) => muted ? "Unmute" : "Mute";

    // Segoe Fluent Icons: E74F mute, E992 (silent), E993 low, E994 medium, E995 high.
    private string VolumeGlyph(bool muted, int level) => muted ? "" : level switch
    {
        0 => "",
        < 34 => "",
        < 67 => "",
        _ => "",
    };

    // --- Volume row ---

    private void OnMuteClick(object sender, RoutedEventArgs e) => _ = ViewModel.ToggleMuteAsync();

    private void OnVolumeSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // The slider also moves when the view-model pushes a new level; only user-originated changes go back.
        if (_viewModel is null)
        {
            return;
        }

        int value = (int)Math.Round(e.NewValue);
        if (value != ViewModel.VolumeLevel)
        {
            ViewModel.SetVolume(value);
        }
    }

    private void OnVolumeWheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            ViewModel.NudgeVolume(delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    private async void OnLyricsWhyVolumeHidden(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ViewModel.VolumeAvailable ? "Volume is available" : "Why is volume hidden?",
            Content = ViewModel.VolumeExplanation,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // --- Control event routing ---

    private void OnShuffleRequested(object? sender, EventArgs e) => _ = ViewModel.ToggleShuffleAsync();

    private void OnPreviousRequested(object? sender, EventArgs e) => _ = ViewModel.PreviousAsync();

    private void OnPlayPauseRequested(object? sender, EventArgs e) => _ = ViewModel.PlayPauseAsync();

    private void OnNextRequested(object? sender, EventArgs e) => _ = ViewModel.NextAsync();

    private void OnRepeatRequested(object? sender, EventArgs e) => _ = ViewModel.CycleRepeatAsync();

    private void OnSeekRequested(object? sender, TimeSpan target) => _ = ViewModel.SeekAsync(target);

    private void OnPlayerSelected(object? sender, string? id) => ViewModel.SelectPlayer(id);

    private void OnLyricsCopy(object? sender, EventArgs e)
    {
        // Copy/open/settings land in later milestones; the menu items exist for parity with the reference.
    }

    private void OnLyricsOpen(object? sender, EventArgs e)
    {
    }

    private void OnLyricsSettings(object? sender, EventArgs e)
    {
    }

    private void OnLyricsEnable(object? sender, EventArgs e) => ViewModel.RequestEnableLyrics();

    private void OnLyricsOffsetIncrease(object? sender, EventArgs e) => ViewModel.AdjustLyricsOffset(500);

    private void OnLyricsOffsetDecrease(object? sender, EventArgs e) => ViewModel.AdjustLyricsOffset(-500);

    private void OnLyricsOffsetReset(object? sender, EventArgs e) => ViewModel.ResetLyricsOffset();

    // --- Keyboard map (_docs/03-ux-interaction-spec.md) ---

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // Seek nudges (+/-5 s, Shift +/-30 s) are handled inside SeekBar itself, because a focused Slider
        // consumes Left/Right in its own class handler before this bubbling handler would run.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;

        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Left when IsWithin(focused, Transport):
                _ = ViewModel.PreviousAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.Right when IsWithin(focused, Transport):
                _ = ViewModel.NextAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.M:
                _ = ViewModel.ToggleMuteAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.P:
                Chooser.OpenFlyout();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }
}
