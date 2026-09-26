//
// AvaloniaWidgetBackend.cs — shared implementation of the Xwt widget
// backend contract over an Avalonia Control.
//
// Every Xwt widget backend wraps ONE native Avalonia control (the same
// one-to-one mapping GtkBackend does with Gtk.Widget). This base class
// implements the plumbing of IWidgetBackend (event routing through the
// Xwt ApplicationContext, visibility/size/coordinate plumbing) so the
// concrete backends (Label/Button/Box/…) only override what differs.
//
// NOTE: inside Xwt.AvaloniaBackend the enclosing Xwt namespaces shadow
// several Avalonia type names (Control, Window, Canvas, Cursor, …), so
// the Avalonia types are referenced through explicit aliases.
//

using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Xwt.Backends;
using AControl = Avalonia.Controls.Control;
using ACursor = Avalonia.Input.Cursor;
using ATemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using APoint = Avalonia.Point;
using AToolTip = Avalonia.Controls.ToolTip;

namespace Xwt.AvaloniaBackend
{
	/// <summary>Base class of every Xwt-on-Avalonia widget backend.</summary>
	public abstract class AvaloniaWidgetBackend : IWidgetBackend
	{
		protected IWidgetEventSink EventSink;
		protected ApplicationContext Context;

		/// <summary>The native Avalonia control wrapped by this backend.</summary>
		public AControl Widget { get; protected set; }

		public virtual void Initialize (IWidgetEventSink eventSink)
		{
			EventSink = eventSink ?? throw new ArgumentNullException (nameof (eventSink));
			Widget ??= CreateNativeWidget ();
			HookNativeEvents ();
		}

		public virtual void InitializeBackend (object frontend, ApplicationContext context)
			=> Context = context;

		public abstract void Dispose ();

		/// <summary>Creates the native control (when none was injected).</summary>
		protected abstract AControl CreateNativeWidget ();

		/// <summary>Subscribes the native control events routed to the EventSink.</summary>
		protected virtual void HookNativeEvents () { }

		/// <summary>Runs an event-sink callback as user code (Xwt contract).</summary>
		protected void InvokeUser (Action sinkCall)
			=> Context?.InvokeUserCode (sinkCall);

		// ----- IWidgetBackend plumbing -----

		public virtual bool Visible {
			get => Widget.IsVisible;
			set => Widget.IsVisible = value;
		}

		public virtual bool Sensitive {
			get => Widget.IsEnabled;
			set => Widget.IsEnabled = value;
		}

		public virtual string Name {
			get => Widget.Name ?? "";
			set => Widget.Name = value;
		}

		public virtual bool CanGetFocus {
			get => Widget.Focusable;
			set => Widget.Focusable = value;
		}

		public virtual bool HasFocus => Widget.IsFocused;

		public virtual double Opacity {
			get => Widget.Opacity;
			set => Widget.Opacity = value;
		}

		public virtual Size Size => new Size (Widget.Bounds.Width, Widget.Bounds.Height);

		public virtual object Font {
			get => Widget is ATemplatedControl tc ? tc.FontFamily : null;
			set { }
		}

		// Xwt.Color (double components) ↔ Avalonia.Media.Color (bytes).
		public virtual Xwt.Drawing.Color BackgroundColor {
			get {
				return Widget is ATemplatedControl tc && tc.Background is SolidColorBrush brush
					? FromAvalonia (brush.Color)
					: new Xwt.Drawing.Color (0, 0, 0, 0);
			}
			set {
				if (Widget is ATemplatedControl tc)
					tc.Background = new SolidColorBrush (ToAvalonia (value));
			}
		}

		public virtual string TooltipText {
			get => AToolTip.GetTip (Widget) as string ?? "";
			set => AToolTip.SetTip (Widget, value);
		}

		public virtual Point ConvertToParentCoordinates (Point widgetCoordinates)
			=> widgetCoordinates; // AVALONIA-PORT: TranslatePoint for real nesting

		public virtual Point ConvertToWindowCoordinates (Point widgetCoordinates)
			=> widgetCoordinates;

		public virtual Point ConvertToScreenCoordinates (Point widgetCoordinates)
		{
			var origin = Widget.PointToScreen (new APoint (0, 0));
			return new Point (origin.X + widgetCoordinates.X, origin.Y + widgetCoordinates.Y);
		}

		public virtual void SetMinSize (double width, double height)
		{
			Widget.MinWidth = Math.Max (0, width);
			Widget.MinHeight = Math.Max (0, height);
		}

		public virtual void SetSizeRequest (double width, double height)
		{
			if (width >= 0) Widget.Width = width;
			if (height >= 0) Widget.Height = height;
		}

		public virtual void SetFocus () => Widget.Focus ();

		public virtual void UpdateLayout () => Widget.InvalidateMeasure ();

		public virtual Size GetPreferredSize (SizeConstraint widthConstraint, SizeConstraint heightConstraint)
			=> new Size (Widget.Bounds.Width, Widget.Bounds.Height);

		public virtual object NativeWidget => Widget;

		public virtual void DragStart (DragStartData data) { }

		public virtual void SetDragSource (TransferDataType[] types, DragDropAction dragAction) { }

		public virtual void SetDragTarget (TransferDataType[] types, DragDropAction dragAction) { }

		public virtual void SetCursor (CursorType cursorType)
			=> Widget.Cursor = MapCursor (cursorType);

		// Xwt.CursorType is a class of static instances (not an enum).
		internal static ACursor MapCursor (CursorType type)
		{
			if (ReferenceEquals (type, CursorType.Hand) || ReferenceEquals (type, CursorType.Hand2))
				return new ACursor (StandardCursorType.Hand);
			if (ReferenceEquals (type, CursorType.IBeam))
				return new ACursor (StandardCursorType.Ibeam);
			if (ReferenceEquals (type, CursorType.Crosshair))
				return new ACursor (StandardCursorType.Cross);
			if (ReferenceEquals (type, CursorType.Wait))
				return new ACursor (StandardCursorType.Wait);
			if (ReferenceEquals (type, CursorType.ResizeLeftRight))
				return new ACursor (StandardCursorType.SizeWestEast);
			if (ReferenceEquals (type, CursorType.ResizeUpDown))
				return new ACursor (StandardCursorType.SizeNorthSouth);
			if (ReferenceEquals (type, CursorType.ResizeLeft))
				return new ACursor (StandardCursorType.LeftSide);
			if (ReferenceEquals (type, CursorType.ResizeRight))
				return new ACursor (StandardCursorType.RightSide);
			if (ReferenceEquals (type, CursorType.ResizeUp))
				return new ACursor (StandardCursorType.TopSide);
			if (ReferenceEquals (type, CursorType.ResizeDown))
				return new ACursor (StandardCursorType.BottomSide);
			if (ReferenceEquals (type, CursorType.Move))
				return new ACursor (StandardCursorType.SizeAll);
			if (ReferenceEquals (type, CursorType.NotAllowed))
				return new ACursor (StandardCursorType.No);
			return new ACursor (StandardCursorType.Arrow);
		}

		public virtual void EnableEvent (object eventId)
		{
			// Event enable/disable bookkeeping is additive; the skeleton
			// routes events unconditionally (AVALONIA-PORT: honor the flags
			// like GtkBackend does to skip unused sinks).
		}

		public virtual void DisableEvent (object eventId)
		{
		}

		// ----- Xwt ↔ Avalonia color conversion -----

		protected static Avalonia.Media.Color ToAvalonia (Xwt.Drawing.Color c)
			=> Avalonia.Media.Color.FromArgb (
				(byte)Math.Round (c.Alpha * 255), (byte)Math.Round (c.Red * 255),
				(byte)Math.Round (c.Green * 255), (byte)Math.Round (c.Blue * 255));

		protected static Xwt.Drawing.Color FromAvalonia (Avalonia.Media.Color c)
			=> new Xwt.Drawing.Color (c.R / 255d, c.G / 255d, c.B / 255d, c.A / 255d);
	}

	/// <summary>Access to the native control of a Xwt widget backend.</summary>
	public static class AvaloniaBackendExtensions
	{
		public static T Native<T> (this IWidgetBackend backend) where T : AvaloniaObject
			=> backend is AvaloniaWidgetBackend awb && awb.Widget is T typed
				? typed
				: throw new InvalidOperationException ("Not an Avalonia widget backend of the requested type");
	}
}
