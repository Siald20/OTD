using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OTD.Controlls.InterlockingEnlements.BlockTiles;

public partial class BlockTile : UserControl
{
    private readonly DispatcherTimer _blinkTimer;
    private bool _blinkOnPhase = true;
    private bool _directionRight;
    private bool _directionVisible;
    private bool _preBlocked;
    private bool _blocked;
    private bool _locked;
    private bool _releaseRequested;

    public BlockTile()
    {
        InitializeComponent();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOnPhase = !_blinkOnPhase;
            ApplyState();
        };
        ApplyState();
    }

    public void SetState(
        bool directionRight,
        bool directionVisible,
        bool preBlocked,
        bool blocked,
        bool locked,
        bool releaseRequested)
    {
        _directionRight = directionRight;
        _directionVisible = directionVisible;
        _preBlocked = preBlocked;
        _blocked = blocked;
        _locked = locked;
        _releaseRequested = releaseRequested;

        if (_releaseRequested && !_blinkTimer.IsEnabled)
        {
            _blinkOnPhase = true;
            _blinkTimer.Start();
        }
        else if (!_releaseRequested && _blinkTimer.IsEnabled)
        {
            _blinkTimer.Stop();
            _blinkOnPhase = true;
        }

        ApplyState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyState()
    {
        if (WhiteLeftArrow is null || WhiteRightArrow is null ||
            RedLeftArrow is null || RedRightArrow is null || LockIndicator is null)
        {
            return;
        }

        var showWhite = _directionVisible && !_blocked;
        var showRed = _preBlocked || _blocked;

        WhiteLeftArrow.Classes.Set("active", showWhite && !_directionRight);
        WhiteRightArrow.Classes.Set("active", showWhite && _directionRight);
        RedLeftArrow.Classes.Set("active", showRed && !_directionRight);
        RedRightArrow.Classes.Set("active", showRed && _directionRight);

        LockIndicator.Classes.Set("active", _locked && (!_releaseRequested || _blinkOnPhase));
    }
}
