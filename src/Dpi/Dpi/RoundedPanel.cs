using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Dpi;

internal sealed class RoundedPanel : Panel
{
	public int Radius { get; set; }

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		Region region = base.Region;
		using (GraphicsPath path = RoundedRectangle(base.ClientRectangle, Radius))
		{
			base.Region = new Region(path);
		}
		region?.Dispose();
	}

	private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = radius * 2;
		Rectangle rect = new Rectangle(bounds.Location, new Size(num, num));
		graphicsPath.AddArc(rect, 180f, 90f);
		rect.X = bounds.Right - num;
		graphicsPath.AddArc(rect, 270f, 90f);
		rect.Y = bounds.Bottom - num;
		graphicsPath.AddArc(rect, 0f, 90f);
		rect.X = bounds.Left;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
