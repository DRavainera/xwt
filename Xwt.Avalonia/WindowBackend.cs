//
// WindowBackend.cs — Xwt Window and Dialog on Avalonia Windows.
//
// The Xwt Window/Dialog family hosts the MonoDevelop surfaces that are NOT
// part of the main workbench (popups and secondary windows); the workbench
// itself stays on the shell's own MainWindow.
//
// Avalonia type names that collide with the Xwt frontend are used through
// explicit aliases (the enclosing Xwt.* namespaces shadow them).
//

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Xwt.Backends;
using AColor = Avalonia.Media.Color;
using ABrush = Avalonia.Media.SolidColorBrush;
using AButton = Avalonia.Controls.Button;
using AGridLength = Avalonia.Controls.GridLength;
using AGridUnitType = Avalonia.Controls.GridUnitType;
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
		protected ApplicationContext Context;

		public virtual void Initialize (IWindowFrameEventSink eventSink)
			=> Sink = eventSink ?? throw new ArgumentNullException (nameof (eventSink));

		public virtual void InitializeBackend (object frontend, ApplicationContext context)
			=> Context = context;

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

		/// <summary>Any live window of this process — the desktop backend reads
		/// the platform screen list through it (Avalonia 12 exposes screens on
		/// WindowBase.Screens, not on a static type).</summary>
		public static AWindow? AnyLiveWindow;

		protected void TrackWindow ()
			=> AnyLiveWindow = NativeWindow;
	}

	/// <summary>
	/// Xwt Window backend over an Avalonia Window. The child widget becomes
	/// the window content (SetChild), like WindowBackend in the Gtk backend.
	/// </summary>
	public class WindowBackend : AvaloniaWindowBackendBase, IWindowBackend
	{
		protected AWindow window;

		public WindowBackend ()
		{
			window = new AWindow { Width = 640, Height = 480 };
			NativeWindow = window;
			TrackWindow ();
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

	/// <summary>
	/// Xwt Dialog backend: an Avalonia Window hosting the dialog content plus
	/// a bottom button bar (SetButtons/UpdateButton/DefaultButton), and the
	/// RunLoop/EndLoop pair the Xwt Dialog frontend drives its modal loop
	/// with — the same shape as the Gtk/WPF DialogBackend implementations.
	/// The frontend's Run() blocks inside InvokePlatformCode on the UI thread
	/// while the platform loop keeps pumping; here that pump is RunJobs +
	/// short sleeps for the duration of the dialog (input stays live because
	/// the dispatcher processes queued jobs each iteration).
	/// </summary>
	public class DialogBackend : WindowBackend, IDialogBackend
	{
		readonly Avalonia.Controls.Grid layout = new ();
		Avalonia.Controls.StackPanel buttonBar;
		readonly List<DialogButton> buttons = new ();
		readonly List<AButton> buttonControls = new ();
		DialogButton defaultButton;
		bool endLoopRequested;

		public DialogBackend ()
		{
			window = new AWindow { Width = 420, Height = 160, CanResize = false };
			NativeWindow = window;
			layout.RowDefinitions.Add (new Avalonia.Controls.RowDefinition (new AGridLength (1, AGridUnitType.Star)));
			layout.RowDefinitions.Add (new Avalonia.Controls.RowDefinition (AGridLength.Auto));
			window.Content = layout;
		}

		void RebuildButtonBar ()
		{
			if (buttonBar is not null) {
				layout.Children.Remove (buttonBar);
				buttonBar = null;
			}
			buttonControls.Clear ();
			if (buttons.Count == 0)
				return;
			buttonBar = new Avalonia.Controls.StackPanel {
				Orientation = Avalonia.Layout.Orientation.Horizontal,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
				Spacing = 8,
				Margin = new AThickness (0, 12, 0, 0),
			};
			foreach (var b in buttons) {
				var control = new AButton { Content = b.Label };
				var captured = b;
				control.Click += (_, _) => {
					if (Sink is IDialogEventSink sink)
						Context?.InvokeUserCode (() => sink.OnDialogButtonClicked (captured));
				};
				control.IsEnabled = b.Sensitive;
				control.IsVisible = b.Visible;
				if (ReferenceEquals (b, defaultButton))
					control.Classes.Add ("accent");
				buttonBar.Children.Add (control);
				buttonControls.Add (control);
			}
			Avalonia.Controls.Grid.SetRow (buttonBar, 1);
			layout.Children.Add (buttonBar);
		}

		public void SetButtons (IEnumerable<DialogButton> newButtons)
		{
			buttons.Clear ();
			if (newButtons is not null)
				buttons.AddRange (newButtons);
			RebuildButtonBar ();
		}

		public void UpdateButton (DialogButton btn)
		{
			int index = buttons.IndexOf (btn);
			if (index < 0 || index >= buttonControls.Count)
				return;
			var control = buttonControls [index];
			control.Content = btn.Label;
			control.IsEnabled = btn.Sensitive;
			control.IsVisible = btn.Visible;
		}

		public DialogButton DefaultButton {
			get => defaultButton;
			set => defaultButton = value;
		}

		public void RunLoop (IWindowFrameBackend parent)
		{
			if (window is null)
				return;
			endLoopRequested = false;
			window.Show ();
			// Pump until EndLoop: a nested DispatcherFrame keeps the platform
			// loop running (input, layout, TIMERS) exactly like the GTK main
			// iteration the Gtk backend runs inside its RunLoop — RunJobs
			// alone does not service due timers, which would freeze any
			// timer-driven automation or animation inside the dialog.
			var frame = new Avalonia.Threading.DispatcherFrame ();
			void OnClosed (object? s, EventArgs e) => frame.Continue = false;
			window.Closed += OnClosed;
			try {
				Avalonia.Threading.Dispatcher.UIThread.PushFrame (frame);
			} finally {
				window.Closed -= OnClosed;
			}
			window.Hide ();
		}

		public void EndLoop ()
		{
			endLoopRequested = true;
			if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess ())
				window?.Close ();
			else
				Avalonia.Threading.Dispatcher.UIThread.Post (() => window?.Close ());
		}
	}

	/// <summary>
	/// Desktop backend over Avalonia's screen list (screens, mouse location,
	/// scale factors) — Dialog.AdjustSize needs the primary screen bounds.
	/// </summary>
	public class AvaloniaDesktopBackend : global::Xwt.Backends.DesktopBackend
	{
		public override Point GetMouseLocation ()
		{
			// No process-wide mouse position in Avalonia: the shell only reads
			// it for popups, so (0,0) is a safe neutral answer (wave: platform
			// screen service on IPlatformSettings).
			return new Point (0, 0);
		}

		public override IEnumerable<object> GetScreens ()
		{
			var all = AvaloniaWindowBackendBase.AnyLiveWindow?.Screens?.All;
			return all is null ? Enumerable.Empty<object> () : all.Cast<object> ();
		}

		public override bool IsPrimaryScreen (object backend)
			=> backend is Avalonia.Platform.Screen s && s.IsPrimary;

		public override Rectangle GetScreenBounds (object backend)
		{
			var b = (backend as Avalonia.Platform.Screen)?.Bounds;
			return b is null ? new Rectangle () : new Rectangle (b.Value.X, b.Value.Y, b.Value.Width, b.Value.Height);
		}

		public override Rectangle GetScreenVisibleBounds (object backend)
		{
			var b = (backend as Avalonia.Platform.Screen)?.WorkingArea;
			return b is null ? new Rectangle () : new Rectangle (b.Value.X, b.Value.Y, b.Value.Width, b.Value.Height);
		}

		public override string GetScreenDeviceName (object backend)
			=> (backend as Avalonia.Platform.Screen)?.DisplayName ?? "";

		public override double GetScaleFactor (object backend)
			=> (backend as Avalonia.Platform.Screen)?.Scaling ?? 1d;
	}
}
