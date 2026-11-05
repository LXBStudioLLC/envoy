using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace Envoy.UI;

/// <summary>
/// The notification-area presence (#92): while a queue drains, Envoy keeps a
/// tray icon with live queue counts, balloons the transitions that need the
/// user (captcha, ready-to-fill, failed, queue finished), and restores the
/// window on click / menu OPEN. Closing the window minimizes to the tray; the
/// menu's EXIT is the way out. Pure plumbing: never touches the apply flow —
/// the queue worker owns its loop, this only observes it.
/// </summary>
public sealed class TrayPresence : IDisposable
{
    private readonly Func<string> _tooltipProvider;
    private readonly NotifyIcon _icon;
    private DateTime _lastToastUtc = DateTime.MinValue;

    public TrayPresence(Func<string> tooltipProvider, Action restoreWindow, Action exitApplication)
    {
        _tooltipProvider = tooltipProvider;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("OPEN ENVOY", null, (_, _) => restoreWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("EXIT", null, (_, _) => exitApplication());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Envoy",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                restoreWindow();
        };
    }

    /// <summary>The current tooltip line (queue counts + verified today). Computed by the owner so the tray owns no state.</summary>
    public void RefreshTooltip()
    {
        var text = _tooltipProvider();
        // NotifyIcon.Text is capped at 63 chars by the shell.
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    /// <summary>Balloon a transition that needs the user. Coalesced: the shell
    /// drops toasts that pile up, so rapid transitions collapse to the newest
    /// of any 12-second window instead of a wall of popups.</summary>
    public void Toast(string title, string text, bool warning)
    {
        var now = DateTime.UtcNow;
        var isCaptchaOrFailure = warning;
        if (now - _lastToastUtc < TimeSpan.FromSeconds(12) && !isCaptchaOrFailure)
            return;

        _lastToastUtc = now;
        RefreshTooltip();
        _icon.BalloonTipTitle = title.Length > 63 ? title[..63] : title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
        try { _icon.ShowBalloonTip(6000); }
        catch { /* balloon delivery is best-effort; never wedge the flow */ }
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(exe)
                && Icon.ExtractAssociatedIcon(exe) is { } appIcon)
                return appIcon;
        }
        catch { /* falls back to the generic application icon */ }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false; // removes it from the tray immediately
        _icon.Dispose();
    }
}
