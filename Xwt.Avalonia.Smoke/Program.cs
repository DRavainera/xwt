//
// Program.cs — headless smoke test of the Xwt.Avalonia backend skeleton.
//
// Exercises the FULL Xwt frontend → Avalonia backend path without a
// display: Application.Initialize with the backend type name (the same
// entry MonoDevelop uses), frontend widget construction (the backend
// registry materializes each native control), the native handles
// (GetNativeWidget/GetBackendForWindow/GetNativeWindow) and the event
// routing contract (ButtonBackend firing the frontend Clicked handler).
//
// Deterministic output: "[smoke] ok N/M" lines and exit code 0 on green.
//

using System;

namespace Xwt.AvaloniaBackend.Smoke
{
	class Program
	{
		static int Main ()
		{
			Console.WriteLine ("[smoke] enter");
			int pass = 0, total = 0;
			void Check (string name, bool ok)
			{
				total++;
				if (ok) pass++;
				Console.WriteLine ($"[smoke] {name}={ok}");
			}

			// 1. Initialize the toolkit through the standard Xwt entry with
			// the backend type name (guest mode: no host lifetime here, the
			// engine only materializes backends without running a loop).
			Xwt.Application.Initialize ("Xwt.AvaloniaBackend.AvaloniaEngine, Xwt.Avalonia");
			Check ("initialized", true);

			// 2. Frontend widgets → native Avalonia controls materialized
			// through the registered backends.
			var label = new Xwt.Label { Text = "hello" };
			var button = new Xwt.Button { Label = "click" };
			var entry = new Xwt.TextEntry ();
			var box = new Xwt.VBox ();
			Check ("label-native", Xwt.Toolkit.CurrentEngine.GetNativeWidget (label) is Avalonia.Controls.TextBlock tb && tb.Text == "hello");
			Check ("button-native", Xwt.Toolkit.CurrentEngine.GetNativeWidget (button) is Avalonia.Controls.Button ab && ab.Content as string == "click");
			Check ("entry-native", Xwt.Toolkit.CurrentEngine.GetNativeWidget (entry) is Avalonia.Controls.TextBox);
			Check ("box-native", Xwt.Toolkit.CurrentEngine.GetNativeWidget (box) is Avalonia.Controls.Canvas);

			// 3. Box composition: frontend children land in the native panel.
			box.PackStart (label);
			box.PackStart (button);
			var panel = (Avalonia.Controls.Canvas)Xwt.Toolkit.CurrentEngine.GetNativeWidget (box);
			Check ("box-children", panel.Children.Count == 2);

			// 4. Window handle round trip: the frontend window's registered
			// IWindowBackend exposes the native Avalonia Window.
			var win = new Xwt.Window ();
			win.Content = box;
			var winBackend = (Xwt.Backends.IWindowBackend)Xwt.Toolkit.GetBackend (win);
			Check ("window-native", winBackend.Window is Avalonia.Controls.Window aw && aw.Content is Avalonia.Controls.Canvas);

			// 5. Event routing: backend click → frontend Clicked handler.
			bool clicked = false;
			button.Clicked += (_, _) => clicked = true;
			FireClick (button);
			Check ("button-clicked", clicked);

			// 6. TextEntry text round trip through the native control.
			entry.Text = "typed";
			Check ("entry-text", ((Avalonia.Controls.TextBox)Xwt.Toolkit.CurrentEngine.GetNativeWidget (entry)).Text == "typed");

			// ---- Wave 1: drawing handlers (Context/Font/TextLayout/Gradient/
			// Image over Avalonia.Media + SkiaSharp) ----

			// 7. Offscreen raster through ImageBuilder + Context primitives:
			// fill a red rect on a 16x16 builder and read pixels back.
			using (var ib = new Xwt.Drawing.ImageBuilder (16, 16)) {
				var ctx = ib.Context;
				ctx.SetColor (Xwt.Drawing.Colors.Red);
				ctx.Rectangle (2, 2, 8, 8);
				ctx.Fill ();
				using var bmp = ib.ToBitmap ();
				Check ("imagebuilder-fill", bmp.GetPixel (5, 5).Red > 0.7 && bmp.GetPixel (0, 0).Alpha < 0.1);
			}

			// 8. Text layout: measure + index mapping through the Font handler.
			var layout = new Xwt.Drawing.TextLayout { Text = "Hello Wave1", Font = Xwt.Drawing.Font.FromName ("Segoe UI 12") };
			var laySize = layout.GetSize ();
			Check ("textlayout-size", laySize.Width > 20 && laySize.Height > 8);
			var idx = layout.GetIndexFromCoordinates (1, 5);
			Check ("textlayout-index", idx == 0 || idx == 1);

			// 9. Gradient fill through Context.Pattern (ImagePattern path).
			var grad = new Xwt.Drawing.LinearGradient (0, 0, 16, 0);
			grad.AddColorStop (0, Xwt.Drawing.Colors.Black);
			grad.AddColorStop (1, Xwt.Drawing.Colors.White);
			using (var ib2 = new Xwt.Drawing.ImageBuilder (16, 16)) {
				var ctx2 = ib2.Context;
				ctx2.Pattern = grad;
				ctx2.Rectangle (0, 0, 16, 16);
				ctx2.Fill ();
				using var bmp2 = ib2.ToBitmap ();
				Check ("gradient-fill", bmp2.GetPixel (14, 8).Red > bmp2.GetPixel (1, 8).Red + 0.25);
			}

			// 10. Draw text into an offscreen image through the wave-1 path.
			using (var ib3 = new Xwt.Drawing.ImageBuilder (24, 24)) {
				var ctx3 = ib3.Context;
				ctx3.SetColor (Xwt.Drawing.Colors.Blue);
				ctx3.DrawTextLayout (layout, 1, 1);
				using var bmp3 = ib3.ToBitmap ();
				bool drewSomething = false;
				for (int x = 0; x < 24 && !drewSomething; x++)
					for (int y = 0; y < 24 && !drewSomething; y++)
						drewSomething = bmp3.GetPixel (x, y).Blue > 0.3;
				Check ("draw-text-into-image", drewSomething);
			}

			Console.WriteLine ($"[smoke] ok {pass}/{total}");
			return pass == total ? 0 : 1;
		}



		// Raises the Avalonia Click on the native button; the dispatcher is
		// not running in the smoke test, so the routing happens inline.
		static void FireClick (Xwt.Button b)
			=> ((Avalonia.Controls.Button)Xwt.Toolkit.CurrentEngine.GetNativeWidget (b)).RaiseEvent (new Avalonia.Interactivity.RoutedEventArgs (Avalonia.Controls.Button.ClickEvent));
	}
}
