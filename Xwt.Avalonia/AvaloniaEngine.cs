//
// AvaloniaEngine.cs — ToolkitEngineBackend implementation for Avalonia.
//
// Xwt.Avalonia replaces Xwt.Gtk (legacy GTK# being retired after the 9.x
// branch): it maps the SAME Xwt frontend object model to Avalonia controls
// the way GtkEngine maps it to Gtk widgets and WPFEngine maps it to WPF.
// Registered here are the first components of the port plan:
//
//   1. Window/Dialog (shell host — the MonoDevelop workbench window)
//   2. Label, Button (the atomic widgets used by every dialog)
//   3. VBox/HBox (layout containers)
//   4. TextEntry (command inputs)
//   5. Canvas (custom surface — drawing handlers land in wave 1)
//
// NOTE: inside Xwt.AvaloniaBackend the enclosing Xwt namespaces shadow
// several Avalonia type names (Application, Window, …), so the Avalonia
// types are referenced through explicit aliases.
//

using System;
using Avalonia;
using Avalonia.Threading;
using Xwt.Backends;
using AApplication = Avalonia.Application;
using AWindow = Avalonia.Controls.Window;
using AWindowState = Avalonia.Controls.WindowState;
using AWindowDecorations = Avalonia.Controls.WindowDecorations;

namespace Xwt.AvaloniaBackend
{
	/// <summary>
	/// Toolkit engine for the Avalonia backend. Initialize with
	/// <c>Application.Initialize ("Xwt.AvaloniaBackend.AvaloniaEngine, Xwt.Avalonia")</c>
	/// from a process that already owns an Avalonia lifetime (the
	/// MonoDevelop Avalonia shell — guest mode), or let RunApplication own
	/// the dispatcher loop when the host does not provide one.
	/// </summary>
	public class AvaloniaEngine : ToolkitEngineBackend
	{
		bool ownsLifetime;

		public override void InitializeApplication ()
		{
			// Guest mode (the normal MonoDevelop case): an Avalonia
			// Application instance already exists, so nothing to bootstrap.
			if (AApplication.Current is null) {
				AppBuilder.Configure<AApplication> ()
					.UsePlatformDetect ()
					.SetupWithoutStarting ();
				ownsLifetime = true;
			}
		}

		public override void InitializeBackends ()
		{
			// First port wave (see the port plan in the class doc): the
			// widgets the MonoDevelop dialogs need first.
			RegisterBackend<IWindowBackend, WindowBackend> ();
			RegisterBackend<ILabelBackend, LabelBackend> ();
			RegisterBackend<IButtonBackend, ButtonBackend> ();
			RegisterBackend<IBoxBackend, BoxBackend> ();
			RegisterBackend<ITextEntryBackend, TextEntryBackend> ();
			RegisterBackend<ICanvasBackend, CanvasBackend> ();

			// Wave 1 (drawing): the Context/Font/TextLayout/Gradient/Image
			// handlers the Canvas custom draw and the dialogs need. They map
			// Xwt.Drawing to Avalonia.Media + SkiaSharp — the exact stack the
			// MonoDevelop Avalonia shell already proves (SkTextEditor). The
			// Toolkit creates these handlers from the registration table at
			// Toolkit.Initialize, so they MUST be registered here.
			RegisterBackend<ContextBackendHandler, ContextBackend> ();
			RegisterBackend<DrawingPathBackendHandler, ContextBackend> ();
			RegisterBackend<FontBackendHandler, FontBackend> ();
			RegisterBackend<TextLayoutBackendHandler, TextLayoutBackend> ();
			RegisterBackend<GradientBackendHandler, GradientBackend> ();
			RegisterBackend<ImagePatternBackendHandler, ImagePatternBackend> ();
			RegisterBackend<ImageBuilderBackendHandler, ImageBuilderBackend> ();
			RegisterBackend<ImageBackendHandler, AvaloniaImageBackend> ();

			// Wave 3 (lists): the Pads widgets, plus the store backends the
			// core requires (ListStore has no fallback; its rows and the tree
			// nodes are the TreePositions the selection APIs address).
			RegisterBackend<IListViewBackend, ListViewBackend> ();
			RegisterBackend<IListBoxBackend, ListViewBackend> ();
			RegisterBackend<ITreeViewBackend, TreeViewBackend> ();
			RegisterBackend<IComboBoxBackend, ComboBoxBackend> ();
			RegisterBackend<IListStoreBackend, ListStoreBackend> ();
			RegisterBackend<ITreeStoreBackend, TreeStoreBackend> ();

			// NEXT WAVES (following the Xwt.Gtk registration list, in the
			// order the MonoDevelop port needs them):
			//   2. ScrollView/Scrollbar, CheckBox/RadioButton/ToggleButton,
			//      Frame/Separator, ImageView.
			//   4. Menus, Dialogs/AlertDialogs, file choosers, Clipboard,
			//   4. Menu/MenuItem family, Dialog/AlertDialog, file choosers,
			//      Clipboard, StatusBar, Notebook, Paned.
			//   5. ICustomWidgetBackend guest hosting + platform services.
		}

		// ----- Application lifecycle (guest + standalone) -----

		public override void RunApplication ()
		{
			if (ownsLifetime)
				Dispatcher.UIThread.MainLoop (System.Threading.CancellationToken.None);
			// Guest mode: the host (the Avalonia MonoDevelop shell) already
			// runs the dispatcher loop; nothing to own here.
		}

		public override void ExitApplication ()
		{
			// The lifetime belongs to the host in guest mode; a standalone
			// engine exits the dispatcher loop.
			Dispatcher.UIThread.InvokeShutdown ();
		}

		// ----- Threading -----

		public override void InvokeAsync (Action action)
			=> Dispatcher.UIThread.Post (action, DispatcherPriority.Normal);

		public override object TimerInvoke (Func<bool> action, TimeSpan timeSpan)
		{
			var timer = default (DispatcherTimer);
			timer = new DispatcherTimer (timeSpan, DispatcherPriority.Normal, (_, _) => {
				if (!action ())
					timer.Stop ();
			});
			timer.Start ();
			return timer;
		}

		public override void CancelTimerInvoke (object id)
		{
			if (id is DispatcherTimer timer)
				timer.Stop ();
		}

		public override void DispatchPendingEvents ()
			=> Dispatcher.UIThread.RunJobs ();

		// ----- Native handles -----

		public override object GetNativeWidget (Widget w)
		{
			return Toolkit.GetBackend (w) is AvaloniaWidgetBackend backend && backend.Widget is not null
				? backend.Widget
				: throw new InvalidOperationException ("The widget does not belong to the Avalonia toolkit backend");
		}

		public override IWindowFrameBackend GetBackendForWindow (object nativeWindow)
			=> nativeWindow is AWindow win
				? new WindowFrameBackendHolder (win)
				: throw new NotSupportedException ($"Not an Avalonia Window: {nativeWindow?.GetType ()}");

		public override object GetNativeWindow (IWindowFrameBackend backend)
			=> backend is AvaloniaWindowBackendBase awb && awb.Window is not null
				? awb.Window
				: throw new NotSupportedException ("Not an Avalonia window backend");

		public override bool HasNativeParent (Widget w)
			=> GetNativeWidget (w) is Avalonia.Controls.Control control && control.Parent is not null;

		// Guest-mode support: when MonoDevelop hosts a Xwt control inside an
		// Avalonia window created by the shell, this returns the shell window.
		public override object GetNativeParentWindow (Widget w)
			=> GetNativeWidget (w) is Avalonia.Controls.Control control
				&& Avalonia.Controls.TopLevel.GetTopLevel (control) is AWindow win
				? win
				: base.GetNativeParentWindow (w);
	}

	/// <summary>
	/// Adapter exposing an existing Avalonia Window through the Xwt
	/// IWindowFrameBackend contract (used by GetBackendForWindow).
	/// </summary>
	class WindowFrameBackendHolder : IWindowFrameBackend
	{
		readonly AWindow window;

		public WindowFrameBackendHolder (AWindow window) => this.window = window;

		public void Initialize (IWindowFrameEventSink eventSink) { }

		public void InitializeBackend (object frontend, ApplicationContext context) { }

		public void EnableEvent (object eventId) { }

		public void DisableEvent (object eventId) { }

		public void Dispose () { }

		public string Name { get; set; } = "";

		public Rectangle Bounds {
			get => new Rectangle (window.Position.X, window.Position.Y, window.Width, window.Height);
			set { }
		}

		public void Move (double x, double y) => window.Position = new PixelPoint ((int)x, (int)y);

		public void SetSize (double width, double height)
		{
			window.Width = width;
			window.Height = height;
		}

		public bool Visible { get => window.IsVisible; set { } }

		public bool Sensitive { get => window.IsEnabled; set => window.IsEnabled = value; }

		public string Title { get => window.Title ?? ""; set => window.Title = value; }

		public bool Decorated { get => window.SystemDecorations != AWindowDecorations.None; set { } }

		public bool ShowInTaskbar { get => window.ShowInTaskbar; set { } }

		public void SetTransientFor (IWindowFrameBackend window) { }

		public bool Resizable { get => window.CanResize; set { } }

		public double Opacity { get => window.Opacity; set { } }

		public bool HasFocus => window.IsFocused;

		public void SetIcon (ImageDescription image) { }

		public void Present () => window.Activate ();

		public bool Close ()
		{
			window.Close ();
			return true;
		}

		public bool FullScreen { get => window.WindowState == AWindowState.FullScreen; set { } }

		public object Screen => null;

		public object Window => window;

		public IntPtr NativeHandle => IntPtr.Zero;
	}
}
