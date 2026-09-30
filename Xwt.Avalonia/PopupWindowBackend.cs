//
// PopupWindowBackend.cs — Xwt PopupWindow on Avalonia (Xwt.AvaloniaBackend).
//
// A PopupWindow (PopupType.Menu/Tooltip) is the legacy GtkWindow type-hint
// Popup: undecorated, not in the taskbar, auto-sized to its content, shown
// near the pointer and always on top while open (override-redirect in Gtk).
// The IDE widget popups — the ProjectPad tree context menu is its flagship —
// pack Xwt widgets into it and Show(); the content's own interactions (menu
// item activation, Esc) close it. A borderless Avalonia Window with
// ShowActivated=false never steals focus from the workbench.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions: (MIT)
//
using System;
using Avalonia.Media;
using Xwt.Backends;
using ABrush = Avalonia.Media.SolidColorBrush;
using AColor = Avalonia.Media.Color;
using AKey = Avalonia.Input.Key;
using AWindowDecorations = Avalonia.Controls.WindowDecorations;
using ASizeToContent = Avalonia.Controls.SizeToContent;

namespace Xwt.AvaloniaBackend
{
	public class PopupWindowBackend : WindowBackend, IPopupWindowBackend
	{
		public void Initialize (IWindowFrameEventSink sink, PopupWindow.PopupType type)
		{
			Sink = sink;
			if (window is not null) {
				window.SystemDecorations = AWindowDecorations.None;
				window.ShowInTaskbar = false;
				window.CanResize = false;
				window.ShowActivated = false;
				window.Topmost = true;
				window.SizeToContent = ASizeToContent.WidthAndHeight;
				// Legacy menu palette (IdeChromeBg) so widget popups read as menus.
				window.Background = new ABrush (AColor.FromArgb (255, 45, 45, 45));
				window.KeyDown += (_, e) => {
					if (e.Key == AKey.Escape) {
						e.Handled = true;
						ClosePopup ();
					}
				};
			}
		}

		/// <summary>Shows the popup anchored at a screen position (the Gtk
		/// Gtk.Menu.Popup-at-pointer flow: Move + Show).</summary>
		public void ShowAt (double x, double y)
		{
			Move (x, y);
			ShowCore ();
		}

		/// <summary>Closes the popup and reports it to the frontend (Window.Closed
		/// resets Visible and completes the menu interaction).</summary>
		public void ClosePopup ()
		{
			if (window is null || !window.IsVisible)
				return;
			window.Hide ();
			Context?.InvokeUserCode (() => Sink?.OnClosed ());
		}
	}
}
