//
// WindowBackend.cs — Xwt Window on an Avalonia Window (first port wave).
//
// The Xwt Window/Dialog family hosts the MonoDevelop surfaces that are NOT
// part of the main workbench (popups and secondary windows); the workbench
// itself stays on the shell's own MainWindow.
//
// Avalonia type names that collide with the Xwt frontend are used through
// explicit aliases (the enclosing Xwt.* namespaces shadow them).
//

using System;
using Avalonia;
using Avalonia.Media;
using Xwt.Backends;
using AColor = Avalonia.Media.Color;
using ABrush = Avalonia.Media.SolidColorBrush;
using AThickness = Avalonia.Thickness;
using AWindow = Avalonia.Controls.Window;
using AWindowState = Avalonia.Controls.WindowState;
using AWindowDecorations = Avalonia.Controls.WindowDecorations;

namespace Xwt.AvaloniaBackend
{
	/// <summary>Shared plumbing for window-backed Xwt surfaces.</summary>
	public abstract class AvaloniaWindowBackendBase : IWindowFrameBackend
	{
		protected IWindowFrameEventSink Sink;
		protected AWindow NativeWindow;

		public virtual void Initialize (IWindowFrameEventSink eventSink)
			=> Sink = eventSink ?? throw new ArgumentNullException (nameof (eventSink));

		public virtual void InitializeBackend (object frontend, ApplicationContext context) { }

		public virtual void EnableEvent (object eventId) { }

		public virtual void DisableEvent (object eventId) { }

		public virtual void Dispose () { }

		public string Name { get; set; } = "";

		public virtual Rectangle Bounds {
			get => NativeWindow is null
				? default
				: new Rectangle (NativeWindow.Position.X, NativeWindow.Position.Y, NativeWindow.Width, NativeWindow.Height);
			set { }
		}

		public virtual void Move (double x, double y)
			=> NativeWindow?.Position = new PixelPoint ((int)x, (int)y);

		public virtual void SetSize (double width, double height)
		{
			if (NativeWindow is not null) {
				NativeWindow.Width = width;
				NativeWindow.Height = height;
			}
		}

		public virtual bool Visible {
			get => NativeWindow?.IsVisible ?? false;
			set { if (value) ShowCore (); else NativeWindow?.Hide (); }
		}

		public virtual bool Sensitive {
			get => NativeWindow?.IsEnabled ?? false;
			set { if (NativeWindow is not null) NativeWindow.IsEnabled = value; }
		}

		public virtual string Title {
			get => NativeWindow?.Title ?? "";
			set { if (NativeWindow is not null) NativeWindow.Title = value; }
		}

		public virtual bool Decorated {
			get => NativeWindow?.SystemDecorations != AWindowDecorations.None;
			set { if (NativeWindow is not null) NativeWindow.SystemDecorations = value ? AWindowDecorations.Full : AWindowDecorations.None; }
		}

		public virtual bool ShowInTaskbar {
			get => NativeWindow?.ShowInTaskbar ?? false;
			set { if (NativeWindow is not null) NativeWindow.ShowInTaskbar = value; }
		}

		public virtual void SetTransientFor (IWindowFrameBackend window) { }

		public virtual bool Resizable {
			get => NativeWindow?.CanResize ?? false;
			set { if (NativeWindow is not null) NativeWindow.CanResize = value; }
		}

		public virtual double Opacity {
			get => NativeWindow?.Opacity ?? 1;
			set { if (NativeWindow is not null) NativeWindow.Opacity = value; }
		}

		public virtual bool HasFocus => NativeWindow?.IsFocused ?? false;

		public virtual void SetIcon (ImageDescription image) { }

		public virtual void Present () => NativeWindow?.Activate ();

		public virtual bool Close ()
		{
			NativeWindow?.Close ();
			return true;
		}

		public virtual bool FullScreen {
			get => NativeWindow?.WindowState == AWindowState.FullScreen;
			set { if (NativeWindow is not null) NativeWindow.WindowState = value ? AWindowState.FullScreen : AWindowState.Normal; }
		}

		public virtual object Screen => null;

		public virtual object Window => NativeWindow;

		public virtual IntPtr NativeHandle => IntPtr.Zero;

		protected virtual void ShowCore () => NativeWindow?.Show ();
	}

	/// <summary>
	/// Xwt Window backend over an Avalonia Window. The child widget becomes
	/// the window content (SetChild), like WindowBackend in the Gtk backend.
	/// </summary>
	public class WindowBackend : AvaloniaWindowBackendBase, IWindowBackend
	{
		AWindow window;

		public WindowBackend ()
		{
			window = new AWindow { Width = 640, Height = 480 };
			NativeWindow = window;
		}

		public WindowBackend (AWindow existing)
		{
			window = existing ?? throw new ArgumentNullException (nameof (existing));
			NativeWindow = window;
		}

		public Xwt.Drawing.Color BackgroundColor {
			get => (window.Background is ABrush b)
				? new Xwt.Drawing.Color (b.Color.R / 255d, b.Color.G / 255d, b.Color.B / 255d, b.Color.A / 255d)
				: default;
			set => window.Background = new ABrush (AColor.FromArgb (
				(byte)Math.Round (value.Alpha * 255), (byte)Math.Round (value.Red * 255),
				(byte)Math.Round (value.Green * 255), (byte)Math.Round (value.Blue * 255)));
		}

		public void SetChild (IWidgetBackend child)
			=> window.Content = ((AvaloniaWidgetBackend)child).Widget;

		public void SetMainMenu (IMenuBackend menu) { } // AVALONIA-PORT: wave 4 (menu family)

		public void SetPadding (double left, double top, double right, double bottom)
			=> window.Padding = new AThickness (left, top, right, bottom);

		public void GetMetrics (out Size minSize, out Size decorationSize)
		{
			minSize = new Size (0, 0);
			decorationSize = new Size (0, 0);
		}

		public void SetMinSize (Size size)
		{
			window.MinWidth = size.Width;
			window.MinHeight = size.Height;
		}

		public void UpdateChildPlacement (IWidgetBackend childBackend) { }
	}
}
