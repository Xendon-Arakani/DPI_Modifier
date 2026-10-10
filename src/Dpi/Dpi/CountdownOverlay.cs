using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Dpi;

internal sealed class CountdownOverlay : NativeWindow, IDisposable
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct Blend
	{
		public byte Operation;

		public byte Flags;

		public byte Alpha;

		public byte Format;
	}

	private delegate void WindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);

	private readonly Bitmap bitmap;

	private readonly Graphics graphics;

	private readonly StringFormat format;

	private readonly IntPtr sourceDc;

	private readonly WindowEvent windowEvent;

	private IntPtr foregroundHook;

	private IntPtr orderHook;

	private Font font;

	private int number = -1;

	private int phase = -1;

	private bool disposed;

	public bool Visible { get; private set; }

	public CountdownOverlay()
	{
		Size iconSpacingSize = SystemInformation.IconSpacingSize;
		CreateHandle(new CreateParams
		{
			Caption = "DPI Overlay",
			Style = int.MinValue,
			ExStyle = 134742184,
			Width = iconSpacingSize.Width,
			Height = iconSpacingSize.Height
		});
		bitmap = new Bitmap(iconSpacingSize.Width, iconSpacingSize.Height, PixelFormat.Format32bppPArgb);
		graphics = Graphics.FromImage(bitmap);
		graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		format = new StringFormat(StringFormat.GenericTypographic);
		format.Alignment = StringAlignment.Center;
		format.LineAlignment = StringAlignment.Center;
		format.FormatFlags |= StringFormatFlags.NoWrap;
		sourceDc = CreateCompatibleDC(IntPtr.Zero);
		WindowEvent windowEvent = delegate
		{
			if (Visible && !disposed)
			{
				Place();
			}
		};
		this.windowEvent = windowEvent;
	}

	public bool Display(int value, int colorPhase = 0)
	{
		if (disposed || sourceDc == IntPtr.Zero || base.Handle == IntPtr.Zero || colorPhase < 0 || colorPhase > 2)
		{
			return false;
		}
		if (Visible && number == value && phase == colorPhase)
		{
			return true;
		}
		string text = value.ToString();
		float val = (float)Math.Min(bitmap.Width, bitmap.Height) * 0.55f;
		val = Math.Min(val, (float)bitmap.Width / ((float)text.Length * 0.7f));
		if (font == null || font.Size != val)
		{
			if (font != null)
			{
				font.Dispose();
			}
			font = new Font("Segoe UI", val, FontStyle.Regular, GraphicsUnit.Pixel);
		}
		graphics.Clear(Color.Transparent);
		using (GraphicsPath graphicsPath = new GraphicsPath())
		{
			using Pen pen = new Pen(Color.Black, 2f);
			graphicsPath.AddString(text, font.FontFamily, (int)font.Style, font.Size, new RectangleF(0f, 0f, bitmap.Width, bitmap.Height), format);
			graphics.DrawPath(pen, graphicsPath);
			graphics.FillPath(colorPhase switch
			{
				1 => Brushes.Lime,
				0 => Brushes.Red,
				_ => Brushes.Blue,
			}, graphicsPath);
		}
		Rectangle bounds = Screen.FromHandle(GetForegroundWindow()).Bounds;
		Point destination = new Point(bounds.Right - bitmap.Width - 50, bounds.Top + 50);
		Point source = Point.Empty;
		Size size = bitmap.Size;
		Blend blend = new Blend
		{
			Alpha = byte.MaxValue,
			Format = 1
		};
		IntPtr hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
		IntPtr value2 = SelectObject(sourceDc, hbitmap);
		bool flag;
		try
		{
			flag = UpdateLayeredWindow(base.Handle, IntPtr.Zero, ref destination, ref size, sourceDc, ref source, 0u, ref blend, 2u);
		}
		finally
		{
			SelectObject(sourceDc, value2);
			DeleteObject(hbitmap);
		}
		if (!flag)
		{
			return false;
		}
		flag = Place(destination);
		if (flag)
		{
			number = value;
			phase = colorPhase;
			Visible = true;
			if (foregroundHook == IntPtr.Zero)
			{
				foregroundHook = SetWinEventHook(3u, 3u, IntPtr.Zero, windowEvent, 0u, 0u, 2u);
			}
			if (orderHook == IntPtr.Zero)
			{
				orderHook = SetWinEventHook(32770u, 32772u, IntPtr.Zero, windowEvent, 0u, 0u, 2u);
			}
		}
		return flag;
	}

	private bool Place()
	{
		Rectangle bounds = Screen.FromHandle(GetForegroundWindow()).Bounds;
		return Place(new Point(bounds.Right - bitmap.Width - 50, bounds.Top + 50));
	}

	private bool Place(Point destination)
	{
		return SetWindowPos(base.Handle, new IntPtr(-1), destination.X, destination.Y, 0, 0, 81u);
	}

	public void Hide()
	{
		if (foregroundHook != IntPtr.Zero)
		{
			UnhookWinEvent(foregroundHook);
			foregroundHook = IntPtr.Zero;
		}
		if (orderHook != IntPtr.Zero)
		{
			UnhookWinEvent(orderHook);
			orderHook = IntPtr.Zero;
		}
		if (Visible)
		{
			ShowWindow(base.Handle, 0);
		}
		Visible = false;
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 132)
		{
			message.Result = new IntPtr(-1);
		}
		else if (message.Msg == 33)
		{
			message.Result = new IntPtr(3);
		}
		else
		{
			base.WndProc(ref message);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Hide();
			if (base.Handle != IntPtr.Zero)
			{
				DestroyHandle();
			}
			if (font != null)
			{
				font.Dispose();
			}
			graphics.Dispose();
			bitmap.Dispose();
			format.Dispose();
			if (sourceDc != IntPtr.Zero)
			{
				DeleteDC(sourceDc);
			}
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WindowEvent callback, uint processId, uint threadId, uint flags);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(IntPtr hook);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr window, int command);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, uint key, ref Blend blend, uint flags);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleDC(IntPtr dc);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(IntPtr dc);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr value);
}
